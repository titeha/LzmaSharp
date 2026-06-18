using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipArchiveDecoderMultiFileTests
{
  [Fact]
  public void DecodeAllFiles_ОдинFolder_ДваФайла_Lzma2Copy_ВозвращаетИсходныеБайты()
  {
    byte[] file1Bytes = Encoding.UTF8.GetBytes("Hello, ");
    byte[] file2Bytes = Encoding.UTF8.GetBytes("world!");

    (string name, byte[] bytes)[] files =
    [
      ("file1.txt", file1Bytes),
      ("file2.txt", file2Bytes),
    ];

    byte[] archive = Build7zArchive_SolidSingleFolder_Lzma2Copy(files);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] decoded);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, decoded.Length);

    Assert.Equal("file1.txt", decoded[0].Name);
    Assert.Equal(file1Bytes, decoded[0].Bytes);

    Assert.Equal("file2.txt", decoded[1].Name);
    Assert.Equal(file2Bytes, decoded[1].Bytes);
  }

  private static byte[] Build7zArchive_SolidSingleFolder_Lzma2Copy((string name, byte[] bytes)[] files)
  {
    byte[] unpacked = Concat(files);

    byte[] packedStreams = Lzma2CopyEncoder.Encode(unpacked, out byte propertiesByte);

    SevenZipCoderInfo coder = new([SevenZipLzma2Coder.MethodIdByte], [propertiesByte], 1, 1);

    List<byte> nextHeaderBytes = [];
    WriteNid(nextHeaderBytes, SevenZipNid.Header);

    // MainStreamsInfo
    WriteNid(nextHeaderBytes, SevenZipNid.MainStreamsInfo);

    WritePackInfo(nextHeaderBytes, (ulong)packedStreams.Length);
    WriteUnpackInfo(nextHeaderBytes, folderUnpackSize: (ulong)unpacked.Length, coder);
    WriteSubStreamsInfo(nextHeaderBytes, files);

    WriteNid(nextHeaderBytes, SevenZipNid.End); // End MainStreamsInfo

    WriteFilesInfo(nextHeaderBytes, files);

    WriteNid(nextHeaderBytes, SevenZipNid.End); // End Header

    byte[] nextHeaderBytesArray = [.. nextHeaderBytes];
    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytesArray);

    SevenZipSignatureHeader signatureHeader = new((ulong)packedStreams.Length, (ulong)nextHeaderBytesArray.Length, nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packedStreams.Length + nextHeaderBytesArray.Length];

    signatureHeader.Write(archive.AsSpan(0, SevenZipSignatureHeader.Size));
    packedStreams.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));
    nextHeaderBytesArray.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size + packedStreams.Length));

    return archive;
  }

  private static byte[] Concat((string name, byte[] bytes)[] files)
  {
    int total = 0;
    for (int i = 0; i < files.Length; i++)
      total += files[i].bytes.Length;

    byte[] output = new byte[total];

    int cursor = 0;
    for (int i = 0; i < files.Length; i++)
    {
      files[i].bytes.CopyTo(output.AsSpan(cursor));
      cursor += files[i].bytes.Length;
    }

    return output;
  }

  private static void WritePackInfo(List<byte> output, ulong packedSize)
  {
    WriteNid(output, SevenZipNid.PackInfo);

    // PackPos
    WriteEncodedUInt64(output, 0);

    // NumPackStreams
    WriteEncodedUInt64(output, 1);

    // Size
    WriteNid(output, SevenZipNid.Size);
    WriteEncodedUInt64(output, packedSize);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteUnpackInfo(List<byte> output, ulong folderUnpackSize, SevenZipCoderInfo coder)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);

    // Folder
    WriteNid(output, SevenZipNid.Folder);

    // NumFolders
    WriteEncodedUInt64(output, 1);

    // External
    WriteByte(output, 0);

    WriteFolder(output, coder);

    // CodersUnpackSize
    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, folderUnpackSize);

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolder(List<byte> output, SevenZipCoderInfo coder)
  {
    // NumCoders
    WriteEncodedUInt64(output, 1);

    WriteCoderInfo(output, coder);

    // ВАЖНО:
    // В текущей реализации SevenZipUnpackInfoReader количество BindPairs и PackedStreams вычисляется
    // из NumInStreams/NumOutStreams, поэтому тут НЕ пишем NumBindPairs/NumPackedStreams/индексы.
  }

  private static void WriteCoderInfo(List<byte> output, SevenZipCoderInfo coder)
  {
    int methodIdSize = coder.MethodId.Length;
    if (methodIdSize <= 0 || methodIdSize > 15)
      throw new ArgumentOutOfRangeException(nameof(coder), "Размер MethodId должен быть в диапазоне [1..15].");

    bool isComplexCoder = coder.NumInStreams != 1 || coder.NumOutStreams != 1;
    bool hasProps = coder.Properties.Length != 0;

    byte mainByte = (byte)methodIdSize;
    if (isComplexCoder)
      mainByte |= 0x10;
    if (hasProps)
      mainByte |= 0x20;

    WriteByte(output, mainByte);
    WriteBytes(output, coder.MethodId);

    if (isComplexCoder)
    {
      WriteEncodedUInt64(output, coder.NumInStreams);
      WriteEncodedUInt64(output, coder.NumOutStreams);
    }

    if (hasProps)
    {
      WriteEncodedUInt64(output, (ulong)coder.Properties.Length);
      WriteBytes(output, coder.Properties);
    }
  }

  private static void WriteSubStreamsInfo(List<byte> output, (string name, byte[] bytes)[] files)
  {
    WriteNid(output, SevenZipNid.SubStreamsInfo);

    // NumUnpackStream (один folder)
    WriteNid(output, SevenZipNid.NumUnpackStream);
    WriteEncodedUInt64(output, (ulong)files.Length);

    // Unpack sizes (для N streams пишем N-1, последний вычисляется из FolderUnpackSize)
    WriteNid(output, SevenZipNid.Size);

    for (int i = 0; i < files.Length - 1; i++)
      WriteEncodedUInt64(output, (ulong)files[i].bytes.Length);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFilesInfo(List<byte> output, (string name, byte[] bytes)[] files)
  {
    WriteNid(output, SevenZipNid.FilesInfo);

    // NumFiles
    WriteEncodedUInt64(output, (ulong)files.Length);

    // Name property
    WriteNid(output, SevenZipNid.Name);

    byte[] namesPayload = BuildNamesPayload(files);
    WriteEncodedUInt64(output, (ulong)namesPayload.Length);

    WriteBytes(output, namesPayload);

    WriteNid(output, SevenZipNid.End);
  }

  private static byte[] BuildNamesPayload((string name, byte[] bytes)[] files)
  {
    List<byte> payload = [];

    // External = 0
    payload.Add(0);

    for (int i = 0; i < files.Length; i++)
    {
      payload.AddRange(Encoding.Unicode.GetBytes(files[i].name));
      payload.Add(0);
      payload.Add(0);
    }

    return [.. payload];
  }

  private static void WriteNid(List<byte> output, byte nid) => WriteByte(output, nid);

  private static void WriteByte(List<byte> output, byte value) => output.Add(value);

  private static void WriteBytes(List<byte> output, byte[] bytes) => output.AddRange(bytes);

  private static void WriteEncodedUInt64(List<byte> output, ulong value)
  {
    Span<byte> tmp = stackalloc byte[9];

    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int bytesWritten);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < bytesWritten; i++)
      output.Add(tmp[i]);
  }
}
