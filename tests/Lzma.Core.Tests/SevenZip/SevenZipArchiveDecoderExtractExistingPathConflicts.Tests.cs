using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderExtractExistingPathConflictsTests
{
  [Fact]
  public void ExtractToDirectory_ПозднийКонфликтСуществующегоФайла_НеОставляетЧастичноеИзвлечение()
  {
    (string name, byte[] bytes)[] files =
    [
      ("first.bin", MakePattern(64, mul: 17, add: 3)),
      ("second.bin", MakePattern(96, mul: 29, add: 5)),
    ];

    byte[] archive = Build7zArchive_SolidSingleFolder_Lzma2Copy(files);

    string root = Path.Combine(
      Path.GetTempPath(),
      "LzmaSharpTests",
      nameof(SevenZipArchiveDecoderExtractExistingPathConflictsTests),
      Guid.NewGuid().ToString("N"));

    byte[] existingBytes = MakePattern(11, mul: 7, add: 1);

    try
    {
      Directory.CreateDirectory(root);
      File.WriteAllBytes(Path.Combine(root, "second.bin"), existingBytes);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        root,
        overwrite: false,
        out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.False(File.Exists(Path.Combine(root, "first.bin")));
      Assert.Equal(existingBytes, File.ReadAllBytes(Path.Combine(root, "second.bin")));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_ФайлНаПутиПозднегоЭлемента_НеОставляетЧастичноеИзвлечение()
  {
    (string name, byte[] bytes)[] files =
    [
      ("first.bin", MakePattern(48, mul: 13, add: 11)),
      ("dir/child.bin", MakePattern(80, mul: 31, add: 9)),
    ];

    byte[] archive = Build7zArchive_SolidSingleFolder_Lzma2Copy(files);

    string root = Path.Combine(
      Path.GetTempPath(),
      "LzmaSharpTests",
      nameof(SevenZipArchiveDecoderExtractExistingPathConflictsTests),
      Guid.NewGuid().ToString("N"));

    byte[] existingBytes = MakePattern(15, mul: 5, add: 2);

    try
    {
      Directory.CreateDirectory(root);
      File.WriteAllBytes(Path.Combine(root, "dir"), existingBytes);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        root,
        overwrite: false,
        out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.False(File.Exists(Path.Combine(root, "first.bin")));
      Assert.Equal(existingBytes, File.ReadAllBytes(Path.Combine(root, "dir")));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_OverwriteTrue_ПозднийСуществующийФайл_Перезаписывает()
  {
    (string name, byte[] bytes)[] files =
    [
      ("first.bin", MakePattern(32, mul: 19, add: 4)),
      ("second.bin", MakePattern(40, mul: 23, add: 6)),
    ];

    byte[] archive = Build7zArchive_SolidSingleFolder_Lzma2Copy(files);

    string root = Path.Combine(
      Path.GetTempPath(),
      "LzmaSharpTests",
      nameof(SevenZipArchiveDecoderExtractExistingPathConflictsTests),
      Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);
      File.WriteAllBytes(Path.Combine(root, "second.bin"), MakePattern(9, mul: 3, add: 1));

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        root,
        overwrite: true,
        out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.Equal(files[0].bytes, File.ReadAllBytes(Path.Combine(root, "first.bin")));
      Assert.Equal(files[1].bytes, File.ReadAllBytes(Path.Combine(root, "second.bin")));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static byte[] Build7zArchive_SolidSingleFolder_Lzma2Copy((string name, byte[] bytes)[] files)
  {
    byte[] unpacked = Concat(files);
    byte[] packedStreams = Lzma2CopyEncoder.Encode(unpacked, out byte propertiesByte);

    SevenZipCoderInfo coder = new([SevenZipLzma2Coder.MethodIdByte], [propertiesByte], 1, 1);

    List<byte> nextHeaderBytes = [];
    WriteNid(nextHeaderBytes, SevenZipNid.Header);

    WriteNid(nextHeaderBytes, SevenZipNid.MainStreamsInfo);
    WritePackInfo(nextHeaderBytes, (ulong)packedStreams.Length);
    WriteUnpackInfo(nextHeaderBytes, folderUnpackSize: (ulong)unpacked.Length, coder);
    WriteSubStreamsInfo(nextHeaderBytes, files);
    WriteNid(nextHeaderBytes, SevenZipNid.End);

    WriteFilesInfo(nextHeaderBytes, files);
    WriteNid(nextHeaderBytes, SevenZipNid.End);

    byte[] nextHeaderBytesArray = [.. nextHeaderBytes];
    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytesArray);

    SevenZipSignatureHeader signatureHeader = new(
      (ulong)packedStreams.Length,
      (ulong)nextHeaderBytesArray.Length,
      nextHeaderCrc);

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

  private static byte[] MakePattern(int length, int mul, int add)
  {
    byte[] bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));

    return bytes;
  }

  private static void WritePackInfo(List<byte> output, ulong packedSize)
  {
    WriteNid(output, SevenZipNid.PackInfo);
    WriteEncodedUInt64(output, 0);
    WriteEncodedUInt64(output, 1);
    WriteNid(output, SevenZipNid.Size);
    WriteEncodedUInt64(output, packedSize);
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteUnpackInfo(List<byte> output, ulong folderUnpackSize, SevenZipCoderInfo coder)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);
    WriteNid(output, SevenZipNid.Folder);
    WriteEncodedUInt64(output, 1);
    WriteByte(output, 0);
    WriteFolder(output, coder);
    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, folderUnpackSize);
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolder(List<byte> output, SevenZipCoderInfo coder)
  {
    WriteEncodedUInt64(output, 1);
    WriteCoderInfo(output, coder);
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
    WriteNid(output, SevenZipNid.NumUnpackStream);
    WriteEncodedUInt64(output, (ulong)files.Length);
    WriteNid(output, SevenZipNid.Size);

    for (int i = 0; i < files.Length - 1; i++)
      WriteEncodedUInt64(output, (ulong)files[i].bytes.Length);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFilesInfo(List<byte> output, (string name, byte[] bytes)[] files)
  {
    WriteNid(output, SevenZipNid.FilesInfo);
    WriteEncodedUInt64(output, (ulong)files.Length);
    WriteNid(output, SevenZipNid.Name);

    byte[] namesPayload = BuildNamesPayload(files);
    WriteEncodedUInt64(output, (ulong)namesPayload.Length);
    WriteBytes(output, namesPayload);
    WriteNid(output, SevenZipNid.End);
  }

  private static byte[] BuildNamesPayload((string name, byte[] bytes)[] files)
  {
    List<byte> payload = [0];

    for (int i = 0; i < files.Length; i++)
    {
      payload.AddRange(System.Text.Encoding.Unicode.GetBytes(files[i].name));
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

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }
    catch
    {
      // ignore
    }
  }
}
