using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Crypto.Gost;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderGostMagmaSingleCoderTests
{
  [Fact]
  public void DecodeSingleFileToArray_GostMagmaSingleCoder_СПаролем_ВозвращаетФайл()
  {
    byte[] plain = CreatePlainForTest();
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(plain, "gost-magma.bin", password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal("gost-magma.bin", decodedFileName);
    Assert.Equal(plain, fileBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostMagmaSingleCoder_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlainForTest();
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(plain, "gost-magma.bin", password);

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
  public void DecodeSingleFileToArray_GostMagmaSingleCoder_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();
    using SevenZipPassword correct = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(plain, "gost-magma.bin", correct);

    using SevenZipPassword wrong = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(wrong),
        fileBytes: out byte[] fileBytes,
        fileName: out _,
        bytesConsumed: out _);

    // Неверный ключ даёт мусор, который не сходится с folder-CRC.
    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Empty(fileBytes);
  }

  [Fact]
  public void DecodeToEntries_GostMagmaSingleCoder_СПаролем_ВозвращаетФайл()
  {
    byte[] plain = CreatePlainForTest();
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(plain, "gost-magma.bin", password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    SevenZipDecodedEntry entry = Assert.Single(entries);
    Assert.Equal(plain, entry.Bytes);
  }

  [Fact]
  public void ExtractToDirectory_GostMagmaSingleCoder_СПаролем_ПишетФайл()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-magma.bin";
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostMagmaSingleCoder(plain, fileName, password);

    string root = Path.Combine(Path.GetTempPath(), "LzmaSharpTests", "GostMagma", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(password),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out _);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
      Assert.Equal(plain, File.ReadAllBytes(Path.Combine(root, fileName)));
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
    }
  }

  private static byte[] CreatePlainForTest()
  {
    var plain = new byte[200]; // не кратно блоку Магмы (8) — проверяем хвост CTR
    for (int i = 0; i < plain.Length; i++)
      plain[i] = unchecked((byte)(i * 29 + 11));

    return plain;
  }

  private static byte[] EncryptMagmaDirectKeyForTest(
      byte[] propertiesBytes,
      SevenZipPassword password,
      byte[] plain)
  {
    Assert.True(SevenZipGostCoder.TryParseProperties(propertiesBytes, out SevenZipGostProperties? properties));

    Span<byte> key = stackalloc byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.True(SevenZipGostKeyDerivation.TryDeriveDirectKey(properties!, password, key));
    Assert.True(SevenZipGostInitializationVector.TryBuildMagmaCtr(properties!, out byte[] iv));
    Assert.True(GostMagmaCtrTransform.TryTransform(key, iv, plain, out byte[] encrypted));

    return encrypted;
  }

  private static byte[] Build7zArchive_SingleFile_GostMagmaSingleCoder(
      byte[] plain,
      string fileName,
      SevenZipPassword password)
  {
    byte[] salt = [0xA1, 0xA2];
    byte[] iv = [0x12, 0x34, 0x56, 0x78];
    byte[] gostProperties = CreateGostDirectProperties(salt, iv);

    byte[] encrypted = EncryptMagmaDirectKeyForTest(gostProperties, password, plain);

    byte[] nextHeader = BuildHeaderSingleFolderGostMagmaSingleCoder(
        packSize: (ulong)encrypted.Length,
        gostProperties: gostProperties,
        unpackSize: (ulong)plain.Length,
        fileName: fileName,
        folderCrc: Crc32.Compute(plain));

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var signatureHeader = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)encrypted.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] signatureHeaderBytes = new byte[SevenZipSignatureHeader.TotalSize];
    signatureHeader.Write(signatureHeaderBytes);

    byte[] archive = new byte[signatureHeaderBytes.Length + encrypted.Length + nextHeader.Length];
    signatureHeaderBytes.CopyTo(archive.AsSpan(0));
    encrypted.CopyTo(archive.AsSpan(signatureHeaderBytes.Length));
    nextHeader.CopyTo(archive.AsSpan(signatureHeaderBytes.Length + encrypted.Length));

    return archive;
  }

  private static byte[] BuildHeaderSingleFolderGostMagmaSingleCoder(
      ulong packSize,
      byte[] gostProperties,
      ulong unpackSize,
      string fileName,
      uint folderCrc)
  {
    var header = new List<byte>(256);

    WriteNid(header, SevenZipNid.Header);
    WriteStreamsInfo(header, packSize, gostProperties, unpackSize, folderCrc);
    WriteFilesInfo(header, fileName);
    WriteNid(header, SevenZipNid.End);

    return [.. header];
  }

  private static void WriteStreamsInfo(
      List<byte> output,
      ulong packSize,
      byte[] gostProperties,
      ulong unpackSize,
      uint folderCrc)
  {
    WriteNid(output, SevenZipNid.MainStreamsInfo);
    WritePackInfo(output, packSize);
    WriteUnpackInfo(output, gostProperties, unpackSize, folderCrc);
    WriteNid(output, SevenZipNid.End);
  }

  private static void WritePackInfo(List<byte> output, ulong packSize)
  {
    WriteNid(output, SevenZipNid.PackInfo);
    WriteEncodedUInt64(output, 0);
    WriteEncodedUInt64(output, 1);
    WriteNid(output, SevenZipNid.Size);
    WriteEncodedUInt64(output, packSize);
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteUnpackInfo(
      List<byte> output,
      byte[] gostProperties,
      ulong unpackSize,
      uint folderCrc)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);
    WriteNid(output, SevenZipNid.Folder);
    WriteEncodedUInt64(output, 1);
    WriteByte(output, 0);
    WriteFolderGostMagmaSingleCoder(output, gostProperties);

    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, unpackSize);

    // CRC распакованного folder-а: AllAreDefined=1, затем один digest.
    WriteNid(output, SevenZipNid.Crc);
    WriteByte(output, 0x01);
    WriteUInt32LittleEndian(output, folderCrc);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolderGostMagmaSingleCoder(List<byte> output, byte[] gostProperties)
  {
    var gostCoder = new SevenZipCoderInfo(
        methodId: SevenZipGostCoder.MagmaMethodId.ToArray(),
        properties: gostProperties,
        numInStreams: 1,
        numOutStreams: 1);

    WriteEncodedUInt64(output, 1);
    WriteCoderInfo(output, gostCoder);
  }

  private static void WriteFilesInfo(List<byte> output, string fileName)
  {
    WriteNid(output, SevenZipNid.FilesInfo);
    WriteEncodedUInt64(output, 1);
    WriteNid(output, SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteEncodedUInt64(output, (ulong)(1 + nameBytes.Length));
    WriteByte(output, 0);
    output.AddRange(nameBytes);

    WriteNid(output, SevenZipNid.End);
  }

  private static byte[] CreateGostDirectProperties(byte[] salt, byte[] iv)
  {
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

  private static void WriteCoderInfo(List<byte> output, SevenZipCoderInfo coder)
  {
    int methodIdSize = coder.MethodId.Length;
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

  private static void WriteNid(List<byte> output, byte nid) => output.Add(nid);

  private static void WriteByte(List<byte> output, byte value) => output.Add(value);

  private static void WriteUInt32LittleEndian(List<byte> output, uint value)
  {
    output.Add((byte)value);
    output.Add((byte)(value >> 8));
    output.Add((byte)(value >> 16));
    output.Add((byte)(value >> 24));
  }

  private static void WriteEncodedUInt64(List<byte> output, ulong value)
  {
    Span<byte> buffer = stackalloc byte[9];
    SevenZipEncodedUInt64.TryWrite(value, buffer, out int bytesWritten);
    output.AddRange(buffer[..bytesWritten].ToArray());
  }
}
