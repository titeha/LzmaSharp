using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderGostMagmaSingleCoderTests
{
  [Fact]
  public void DecodeSingleFileToArray_GostMagmaSingleCoder_СПаролем_ВозвращаетNotSupported()
  {
    byte[] packed = CreatePackedForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(
        packedBytes: packed,
        fileName: "gost-magma-single-coder.bin");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, decodedFileName);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostMagmaSingleCoder_БезПароля_ВозвращаетNotSupported()
  {
    byte[] packed = CreatePackedForTest();

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(
        packedBytes: packed,
        fileName: "gost-magma-single-coder.bin");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, decodedFileName);
  }

  [Fact]
  public void ExtractToDirectory_GostMagmaSingleCoder_СПаролем_ВозвращаетNotSupportedИНичегоНеПишет()
  {
    byte[] packed = CreatePackedForTest();
    const string fileName = "gost-magma-single-coder.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(
        packedBytes: packed,
        fileName: fileName);

    string root = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(password),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
      Assert.Equal(archive.Length, bytesConsumed);
      AssertDestinationIsEmptyOrMissing(root, fileName);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_GostMagmaSingleCoder_БезПароля_ВозвращаетNotSupportedИНичегоНеПишет()
  {
    byte[] packed = CreatePackedForTest();
    const string fileName = "gost-magma-single-coder.bin";

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(
        packedBytes: packed,
        fileName: fileName);

    string root = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
      Assert.Equal(archive.Length, bytesConsumed);
      AssertDestinationIsEmptyOrMissing(root, fileName);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void DecodeToArray_GostMagmaSingleCoder_СПаролем_ВозвращаетNotSupported()
  {
    byte[] packed = CreatePackedForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(
        packedBytes: packed,
        fileName: "gost-magma-single-coder.bin");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToArray_GostMagmaSingleCoder_БезПароля_ВозвращаетNotSupported()
  {
    byte[] packed = CreatePackedForTest();

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(
        packedBytes: packed,
        fileName: "gost-magma-single-coder.bin");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToEntries_GostMagmaSingleCoder_СПаролем_ВозвращаетNotSupported()
  {
    byte[] packed = CreatePackedForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(
        packedBytes: packed,
        fileName: "gost-magma-single-coder.bin");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void DecodeToEntries_GostMagmaSingleCoder_БезПароля_ВозвращаетNotSupported()
  {
    byte[] packed = CreatePackedForTest();

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(
        packedBytes: packed,
        fileName: "gost-magma-single-coder.bin");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  private static string CreateTempRoot()
  {
    return Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderGostMagmaSingleCoderTests),
        Guid.NewGuid().ToString("N"));
  }

  private static void AssertDestinationIsEmptyOrMissing(
      string root,
      string fileName)
  {
    Assert.False(File.Exists(Path.Combine(root, fileName)));

    if (Directory.Exists(root))
      Assert.Empty(Directory.GetFileSystemEntries(root));
  }

  private static void TryDeleteTree(string path)
  {
    try
    {
      if (Directory.Exists(path))
      {
        Directory.Delete(path, recursive: true);
      }
    }
    catch
    {
      // Best-effort cleanup для тестового каталога.
    }
  }

  private static byte[] CreatePackedForTest()
  {
    var packed = new byte[32];

    for (int i = 0; i < packed.Length; i++)
      packed[i] = unchecked((byte)(i * 13 + 5));

    return packed;
  }

  private static byte[] Build7zArchive_SingleFile_GostMagmaSingleCoder(
      ReadOnlySpan<byte> packedBytes,
      string fileName)
  {
    byte[] packedBytesArray = packedBytes.ToArray();

    byte[] salt = [0xA1, 0xA2];
    byte[] iv = [0x12, 0x34, 0x56, 0x78];
    byte[] gostProperties = CreateGostDirectProperties(salt, iv);

    byte[] nextHeader = BuildHeaderSingleFolderGostMagmaSingleCoder(
        packSize: (ulong)packedBytesArray.Length,
        gostProperties: gostProperties,
        unpackSize: (ulong)packedBytesArray.Length,
        fileName: fileName);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var signatureHeader = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)packedBytesArray.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] signatureHeaderBytes = new byte[SevenZipSignatureHeader.TotalSize];
    signatureHeader.Write(signatureHeaderBytes);

    byte[] archive = new byte[
        signatureHeaderBytes.Length +
        packedBytesArray.Length +
        nextHeader.Length];

    signatureHeaderBytes.CopyTo(archive.AsSpan(0));
    packedBytesArray.CopyTo(archive.AsSpan(signatureHeaderBytes.Length));
    nextHeader.CopyTo(archive.AsSpan(signatureHeaderBytes.Length + packedBytesArray.Length));

    return archive;
  }

  private static byte[] BuildHeaderSingleFolderGostMagmaSingleCoder(
      ulong packSize,
      byte[] gostProperties,
      ulong unpackSize,
      string fileName)
  {
    var header = new List<byte>(256);

    WriteNid(header, SevenZipNid.Header);

    WriteStreamsInfo(
        header,
        packSize: packSize,
        gostProperties: gostProperties,
        unpackSize: unpackSize);

    WriteFilesInfo(header, fileName);
    WriteNid(header, SevenZipNid.End);

    return [.. header];
  }

  private static void WriteStreamsInfo(
      List<byte> output,
      ulong packSize,
      byte[] gostProperties,
      ulong unpackSize)
  {
    WriteNid(output, SevenZipNid.MainStreamsInfo);
    WritePackInfo(output, packSize);

    WriteUnpackInfo(
        output,
        gostProperties: gostProperties,
        unpackSize: unpackSize);

    WriteSubStreamsInfoEmpty(output);
    WriteNid(output, SevenZipNid.End);
  }

  private static void WritePackInfo(
      List<byte> output,
      ulong packSize)
  {
    WriteNid(output, SevenZipNid.PackInfo);

    // Данные файла начинаются сразу после SignatureHeader.
    WriteEncodedUInt64(output, 0);

    // Один packed stream.
    WriteEncodedUInt64(output, 1);

    WriteNid(output, SevenZipNid.Size);
    WriteEncodedUInt64(output, packSize);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteUnpackInfo(
      List<byte> output,
      byte[] gostProperties,
      ulong unpackSize)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);
    WriteNid(output, SevenZipNid.Folder);

    // Один folder.
    WriteEncodedUInt64(output, 1);

    // Folder описан прямо в header, не external.
    WriteByte(output, 0);

    WriteFolderGostMagmaSingleCoder(output, gostProperties);

    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, unpackSize);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolderGostMagmaSingleCoder(
      List<byte> output,
      byte[] gostProperties)
  {
    var gostCoder = new SevenZipCoderInfo(
        methodId: SevenZipGostCoder.MagmaMethodId.ToArray(),
        properties: gostProperties,
        numInStreams: 1,
        numOutStreams: 1);

    WriteEncodedUInt64(output, 1);
    WriteCoderInfo(output, gostCoder);

    // BindPairs не пишем: одиночный coder не имеет внутренних связей.
    // PackedStreamIndices тоже не пишем: при одном packed stream parser
    // вычисляет единственный входной stream сам.
  }

  private static void WriteSubStreamsInfoEmpty(List<byte> output)
  {
    WriteNid(output, SevenZipNid.SubStreamsInfo);
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFilesInfo(
      List<byte> output,
      string fileName)
  {
    WriteNid(output, SevenZipNid.FilesInfo);

    // Один файл.
    WriteEncodedUInt64(output, 1);

    WriteNid(output, SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");

    // В property payload первым байтом идёт external-флаг.
    WriteEncodedUInt64(output, (ulong)(1 + nameBytes.Length));
    WriteByte(output, 0);
    output.AddRange(nameBytes);

    WriteNid(output, SevenZipNid.End);
  }

  private static byte[] CreateGostDirectProperties(
      byte[] salt,
      byte[] iv)
  {
    Assert.InRange(salt.Length, 0, byte.MaxValue);
    Assert.InRange(iv.Length, 0, byte.MaxValue);

    var properties = new byte[5 + salt.Length + iv.Length];

    properties[0] = SevenZipGostCoder.CurrentPropertiesVersion;
    properties[1] = 0x00;
    properties[2] = SevenZipGostCoder.DirectKeyNumCyclesPower;
    properties[3] = (byte)salt.Length;
    properties[4] = (byte)iv.Length;

    salt.CopyTo(properties.AsSpan(5));
    iv.CopyTo(properties.AsSpan(5 + salt.Length));

    return properties;
  }

  private static void WriteCoderInfo(
      List<byte> output,
      SevenZipCoderInfo coder)
  {
    int methodIdSize = coder.MethodId.Length;
    Assert.InRange(methodIdSize, 1, 15);

    bool isComplexCoder = coder.NumInStreams != 1 || coder.NumOutStreams != 1;
    bool hasProperties = coder.Properties.Length != 0;

    byte mainByte = (byte)(
        (methodIdSize & 0x0F) |
        (isComplexCoder ? 0x10 : 0) |
        (hasProperties ? 0x20 : 0));

    WriteByte(output, mainByte);
    output.AddRange(coder.MethodId);

    if (isComplexCoder)
    {
      WriteEncodedUInt64(output, coder.NumInStreams);
      WriteEncodedUInt64(output, coder.NumOutStreams);
    }

    if (hasProperties)
    {
      WriteEncodedUInt64(output, (ulong)coder.Properties.Length);
      output.AddRange(coder.Properties);
    }
  }

  private static void WriteNid(
      List<byte> output,
      byte nid) => output.Add(nid);

  private static void WriteByte(
      List<byte> output,
      byte value) => output.Add(value);

  private static void WriteEncodedUInt64(
      List<byte> output,
      ulong value)
  {
    Span<byte> buffer = stackalloc byte[9];

    SevenZipEncodedUInt64.WriteResult result = SevenZipEncodedUInt64.TryWrite(
        value,
        buffer,
        out int bytesWritten);

    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, result);
    Assert.True(bytesWritten > 0);

    output.AddRange(buffer[..bytesWritten].ToArray());
  }
}
