using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderMultiFolderTests
{
  [Fact]
  public void DecodeToArray_ДваФайла_ДваFolder_Lzma2Copy_ВозвращаетИсходныеБайты()
  {
    byte[] file1Bytes = [1, 2, 3, 4, 5, 6, 7];
    byte[] file2Bytes = [10, 20, 30, 40, 50];

    const string file1Name = "a.bin";
    const string file2Name = "b.bin";

    byte[] archive = Build7zArchive_MultiFolder_Lzma2Copy([(file1Name, file1Bytes), (file2Name, file2Bytes)]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.NotNull(files);
    Assert.Equal(2, files.Length);

    Assert.Equal(file1Name, files[0].Name);
    Assert.Equal(file1Bytes, files[0].Bytes);

    Assert.Equal(file2Name, files[1].Name);
    Assert.Equal(file2Bytes, files[1].Bytes);
  }

  private static byte[] Build7zArchive_MultiFolder_Lzma2Copy(params (string Name, byte[] Bytes)[] files)
  {
    if (files is null || files.Length == 0)
      throw new ArgumentException("Нужно передать хотя бы один файл.", nameof(files));

    const int dictionarySize = 1 << 20;

    // Для простоты используем одинаковые настройки LZMA2 для всех folder'ов.
    byte propertiesByte = 0;

    var packedStreams = new List<byte>(4096);
    var packSizes = new ulong[files.Length];
    var folderUnpackSizes = new ulong[files.Length];
    var fileNames = new string[files.Length];

    for (int i = 0; i < files.Length; i++)
    {
      fileNames[i] = files[i].Name;
      folderUnpackSizes[i] = (ulong)files[i].Bytes.Length;

      byte[] packed = Lzma2CopyEncoder.Encode(files[i].Bytes, dictionarySize, out propertiesByte);

      packSizes[i] = (ulong)packed.Length;
      packedStreams.AddRange(packed);
    }

    SevenZipCoderInfo coder = SevenZipLzma2Coder.Create(propertiesByte);
    byte[] headerBytes = Build7zHeader(fileNames, packSizes, folderUnpackSizes, coder);

    uint nextHeaderCrc = Crc32.Compute(headerBytes);
    var signatureHeader = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)packedStreams.Count,
      NextHeaderSize: (ulong)headerBytes.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[
      SevenZipSignatureHeader.Size +
      packedStreams.Count +
      headerBytes.Length];

    signatureHeader.Write(archive);

    Buffer.BlockCopy(
      src: packedStreams.ToArray(),
      srcOffset: 0,
      dst: archive,
      dstOffset: SevenZipSignatureHeader.Size,
      count: packedStreams.Count);

    Buffer.BlockCopy(
      src: headerBytes,
      srcOffset: 0,
      dst: archive,
      dstOffset: SevenZipSignatureHeader.Size + packedStreams.Count,
      count: headerBytes.Length);

    return archive;
  }

  private static byte[] Build7zHeader(string[] fileNames, ulong[] packSizes, ulong[] folderUnpackSizes, SevenZipCoderInfo coder)
  {
    var output = new List<byte>(512);

    WriteNid(output, SevenZipNid.Header);

    WriteStreamsInfo(output, packSizes, folderUnpackSizes, coder);
    WriteFilesInfo(output, fileNames);

    WriteNid(output, SevenZipNid.End);

    return [.. output];
  }

  private static void WriteStreamsInfo(List<byte> output, ulong[] packStreamSizes, ulong[] folderUnpackSizes, SevenZipCoderInfo coder)
  {
    WriteNid(output, SevenZipNid.MainStreamsInfo);

    WritePackInfo(output, packStreamSizes);
    WriteUnpackInfo(output, folderUnpackSizes, coder);

    // SubStreamsInfo не пишем: в этом сценарии на каждый folder приходится ровно один unpack-stream.

    WriteNid(output, SevenZipNid.End);
  }

  private static void WritePackInfo(List<byte> output, ulong[] packStreamSizes)
  {
    WriteNid(output, SevenZipNid.PackInfo);

    // PackPos
    WriteEncodedUInt64(output, 0);

    // NumPackStreams
    WriteEncodedUInt64(output, (ulong)packStreamSizes.Length);

    // Size
    WriteNid(output, SevenZipNid.Size);
    foreach (ulong s in packStreamSizes)
      WriteEncodedUInt64(output, s);

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteUnpackInfo(List<byte> output, ulong[] folderUnpackSizes, SevenZipCoderInfo coder)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);

    // Folder
    WriteNid(output, SevenZipNid.Folder);

    // NumFolders
    WriteEncodedUInt64(output, (ulong)folderUnpackSizes.Length);

    // External
    WriteByte(output, 0);

    for (int folderIndex = 0; folderIndex < folderUnpackSizes.Length; folderIndex++)
      WriteFolder(output, coder);

    // CodersUnpackSize
    WriteNid(output, SevenZipNid.CodersUnpackSize);
    foreach (ulong unpackSize in folderUnpackSizes)
      WriteEncodedUInt64(output, unpackSize);

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolder(List<byte> output, SevenZipCoderInfo coder)
  {
    // NumCoders
    WriteEncodedUInt64(output, 1);

    WriteCoderInfo(output, coder);

    // BindPairs: NumBindPairs = TotalOutStreams - 1
    ulong numBindPairs = coder.NumOutStreams - 1;
    for (ulong i = 0; i < numBindPairs; i++)
    {
      WriteEncodedUInt64(output, 0);
      WriteEncodedUInt64(output, i + 1);
    }

    // NumPackedStreams = TotalInStreams - NumBindPairs.
    // Для NumPackedStreams == 1 список PackedStreams не записываем (его не ждёт SevenZipUnpackInfoReader).

  }

  private static void WriteCoderInfo(List<byte> output, SevenZipCoderInfo coder)
  {
    int methodIdSize = coder.MethodId.Length;

    if (methodIdSize is < 1 or > 15)
      throw new InvalidOperationException("MethodId.Length должен быть в диапазоне [1..15].");

    byte mainByte = (byte)(methodIdSize & 0x0F);

    // IsComplexCoder
    if (coder.NumInStreams != 1 || coder.NumOutStreams != 1)
      mainByte |= 0x10;

    // HasAttributes
    if (coder.Properties.Length > 0)
      mainByte |= 0x20;

    WriteByte(output, mainByte);
    WriteBytes(output, coder.MethodId);

    if (coder.Properties.Length > 0)
    {
      WriteEncodedUInt64(output, (ulong)coder.Properties.Length);
      WriteBytes(output, coder.Properties);
    }

    if ((mainByte & 0x10) != 0)
    {
      WriteEncodedUInt64(output, coder.NumInStreams);
      WriteEncodedUInt64(output, coder.NumOutStreams);
    }
  }

  private static void WriteFilesInfo(List<byte> output, string[] fileNames)
  {
    WriteNid(output, SevenZipNid.FilesInfo);

    WriteEncodedUInt64(output, (ulong)fileNames.Length);

    // Name
    WriteNid(output, SevenZipNid.Name);

    byte[] payload = BuildNamesPayload(fileNames);

    // Property size (payload + external byte)
    WriteEncodedUInt64(output, (ulong)(payload.Length + 1));

    // External
    WriteByte(output, 0);

    WriteBytes(output, payload);

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static byte[] BuildNamesPayload(string[] fileNames)
  {
    var sb = new StringBuilder();
    foreach (string name in fileNames)
    {
      sb.Append(name);
      sb.Append('\0');
    }

    return Encoding.Unicode.GetBytes(sb.ToString());
  }

  private static void WriteNid(List<byte> output, byte nid) => output.Add(nid);

  private static void WriteByte(List<byte> output, byte value) => output.Add(value);

  private static void WriteBytes(List<byte> output, ReadOnlySpan<byte> bytes)
  {
    for (int i = 0; i < bytes.Length; i++)
      output.Add(bytes[i]);
  }

  private static void WriteEncodedUInt64(List<byte> output, ulong value)
  {
    Span<byte> buffer = stackalloc byte[9];

    Assert.Equal(
      SevenZipEncodedUInt64.WriteResult.Ok,
      SevenZipEncodedUInt64.TryWrite(value, buffer, out int bytesWritten));

    for (int i = 0; i < bytesWritten; i++)
      output.Add(buffer[i]);
  }
}
