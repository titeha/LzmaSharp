using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Crypto.Gost;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderGostKuznyechikSingleCoderTests
{
  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikSingleCoder_СПаролем_ВозвращаетФайл()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-single-coder.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikSingleCoder(
        plainFileBytes: plain,
        fileName: fileName,
        password: password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(fileName, decodedFileName);
    Assert.Equal(plain, fileBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikSingleCoder_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikSingleCoder(
        plainFileBytes: plain,
        fileName: "gost-single-coder.bin",
        password: password);

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
  public void DecodeSingleFileToArray_GostKuznyechikSingleCoder_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikSingleCoder(
        plainFileBytes: plain,
        fileName: "gost-single-coder.bin",
        password: correctPassword);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, decodedFileName);
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikSingleCoder_СПаролем_ЗаписываетФайл()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-single-coder.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikSingleCoder(
        plainFileBytes: plain,
        fileName: fileName,
        password: password);

    string root = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(password),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
      Assert.Equal(archive.Length, bytesConsumed);

      string filePath = Path.Combine(root, fileName);
      Assert.True(File.Exists(filePath));
      Assert.Equal(plain, File.ReadAllBytes(filePath));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikSingleCoder_БезПароля_ВозвращаетNotSupportedИНичегоНеПишет()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-single-coder.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikSingleCoder(
        plainFileBytes: plain,
        fileName: fileName,
        password: password);

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
  public void ExtractToDirectory_GostKuznyechikSingleCoder_СНевернымПаролем_ВозвращаетInvalidDataИНичегоНеПишет()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-single-coder.bin";

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikSingleCoder(
        plainFileBytes: plain,
        fileName: fileName,
        password: correctPassword);

    string root = CreateTempRoot();

    try
    {
      using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(wrongPassword),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
      Assert.Equal(archive.Length, bytesConsumed);
      AssertDestinationIsEmptyOrMissing(root, fileName);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static string CreateTempRoot()
  {
    return Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderGostKuznyechikSingleCoderTests),
        Guid.NewGuid().ToString("N"));
  }

  private static void AssertDestinationIsEmptyOrMissing(
      string root,
      string fileName)
  {
    Assert.False(File.Exists(Path.Combine(root, fileName)));

    if (Directory.Exists(root))
    {
      Assert.Empty(Directory.GetFileSystemEntries(root));
    }
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

  private static byte[] CreatePlainForTest()
  {
    var plain = new byte[256];

    for (int i = 0; i < plain.Length; i++)
      plain[i] = unchecked((byte)(i * 29 + 11));

    return plain;
  }

  private static byte[] Build7zArchive_SingleFile_GostKuznyechikSingleCoder(
      ReadOnlySpan<byte> plainFileBytes,
      string fileName,
      SevenZipPassword password)
  {
    byte[] plainFileBytesArray = plainFileBytes.ToArray();

    byte[] salt = [0xA1, 0xA2];
    byte[] iv = [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0];
    byte[] gostProperties = CreateGostDirectProperties(salt, iv);

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        propertiesBytes: gostProperties,
        password: password,
        plain: plainFileBytesArray);

    byte[] nextHeader = BuildHeaderSingleFolderGostKuznyechikSingleCoder(
        packSize: (ulong)encrypted.Length,
        gostProperties: gostProperties,
        unpackSize: (ulong)plainFileBytesArray.Length,
        fileName: fileName,
        folderCrc: Crc32.Compute(plainFileBytesArray));

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var signatureHeader = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)encrypted.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] signatureHeaderBytes = new byte[SevenZipSignatureHeader.TotalSize];
    signatureHeader.Write(signatureHeaderBytes);

    byte[] archive = new byte[
        signatureHeaderBytes.Length +
        encrypted.Length +
        nextHeader.Length];

    signatureHeaderBytes.CopyTo(archive.AsSpan(0));
    encrypted.CopyTo(archive.AsSpan(signatureHeaderBytes.Length));
    nextHeader.CopyTo(archive.AsSpan(signatureHeaderBytes.Length + encrypted.Length));

    return archive;
  }

  private static byte[] BuildHeaderSingleFolderGostKuznyechikSingleCoder(
      ulong packSize,
      byte[] gostProperties,
      ulong unpackSize,
      string fileName,
      uint? folderCrc)
  {
    var header = new List<byte>(256);

    WriteNid(header, SevenZipNid.Header);

    WriteStreamsInfo(
        header,
        packSize: packSize,
        gostProperties: gostProperties,
        unpackSize: unpackSize,
        folderCrc: folderCrc);

    WriteFilesInfo(header, fileName);
    WriteNid(header, SevenZipNid.End);

    return [.. header];
  }

  private static void WriteStreamsInfo(
      List<byte> output,
      ulong packSize,
      byte[] gostProperties,
      ulong unpackSize,
      uint? folderCrc)
  {
    WriteNid(output, SevenZipNid.MainStreamsInfo);
    WritePackInfo(output, packSize);

    WriteUnpackInfo(
        output,
        gostProperties: gostProperties,
        unpackSize: unpackSize,
        folderCrc: folderCrc);

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
      ulong unpackSize,
      uint? folderCrc)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);
    WriteNid(output, SevenZipNid.Folder);

    // Один folder.
    WriteEncodedUInt64(output, 1);

    // Folder описан прямо в header, не external.
    WriteByte(output, 0);

    WriteFolderGostKuznyechikSingleCoder(output, gostProperties);

    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, unpackSize);

    if (folderCrc.HasValue)
    {
      WriteNid(output, SevenZipNid.Crc);

      // AllAreDefined = true.
      WriteByte(output, 1);

      WriteUInt32LE(output, folderCrc.Value);
    }

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolderGostKuznyechikSingleCoder(
      List<byte> output,
      byte[] gostProperties)
  {
    var gostCoder = new SevenZipCoderInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
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

  private static byte[] EncryptKuznyechikDirectKeyForTest(
      byte[] propertiesBytes,
      SevenZipPassword password,
      byte[] plain)
  {
    Assert.True(SevenZipGostCoder.TryParseProperties(
        propertiesBytes,
        out SevenZipGostProperties? properties));

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    try
    {
      Assert.True(SevenZipGostKeyDerivation.TryDeriveDirectKey(
          properties!,
          password,
          key));

      Assert.True(GostKuznyechikCtrTransform.TryTransform(
          key,
          properties!.InitializationVector,
          plain,
          out byte[] encrypted));

      return encrypted;
    }
    finally
    {
      Array.Clear(key);
    }
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
      byte nid)
  {
    output.Add(nid);
  }

  private static void WriteByte(
      List<byte> output,
      byte value)
  {
    output.Add(value);
  }

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

  private static void WriteUInt32LE(
      List<byte> output,
      uint value)
  {
    output.Add((byte)value);
    output.Add((byte)(value >> 8));
    output.Add((byte)(value >> 16));
    output.Add((byte)(value >> 24));
  }
}
