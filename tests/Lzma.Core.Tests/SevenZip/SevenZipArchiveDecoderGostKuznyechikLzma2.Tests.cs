using Lzma.Core.Checksums;
using Lzma.Core.Crypto.Gost;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderGostKuznyechikLzma2Tests
{
  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikLzma2Archive_СПаролем_ВозвращаетИсходныеБайты()
  {
    byte[] plain = CreatePlain();

    byte[] salt = [0xA1, 0xA2];
    byte[] iv = [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0];
    byte[] gostProperties = CreateGostDirectProperties(salt, iv);

    const int dictionarySize = 1 << 20;

    byte[] lzma2Packed = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        gostProperties,
        password,
        lzma2Packed);

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikThenLzma2(
        packedStreams: encrypted,
        fileName: "gost-lzma2.bin",
        gostProperties: gostProperties,
        lzma2Properties: [lzma2PropertiesByte],
        gostUnpackSize: (ulong)lzma2Packed.Length,
        finalUnpackSize: (ulong)plain.Length,
        folderCrc: Crc32.Compute(plain));

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] decoded,
        fileName: out string decodedName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal("gost-lzma2.bin", decodedName);
    Assert.Equal(plain, decoded);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikLzma2Archive_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlain();

    byte[] gostProperties = CreateGostDirectProperties(
        salt: [0xA1, 0xA2],
        iv: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    const int dictionarySize = 1 << 20;

    byte[] lzma2Packed = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        gostProperties,
        password,
        lzma2Packed);

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikThenLzma2(
        packedStreams: encrypted,
        fileName: "gost-lzma2.bin",
        gostProperties: gostProperties,
        lzma2Properties: [lzma2PropertiesByte],
        gostUnpackSize: (ulong)lzma2Packed.Length,
        finalUnpackSize: (ulong)plain.Length,
        folderCrc: Crc32.Compute(plain));

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out byte[] decoded,
        fileName: out string decodedName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(decoded);
    Assert.Equal(string.Empty, decodedName);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikLzma2Archive_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlain();

    byte[] gostProperties = CreateGostDirectProperties(
        salt: [0xA1, 0xA2],
        iv: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    const int dictionarySize = 1 << 20;

    byte[] lzma2Packed = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        gostProperties,
        correctPassword,
        lzma2Packed);

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikThenLzma2(
        packedStreams: encrypted,
        fileName: "gost-lzma2.bin",
        gostProperties: gostProperties,
        lzma2Properties: [lzma2PropertiesByte],
        gostUnpackSize: (ulong)lzma2Packed.Length,
        finalUnpackSize: (ulong)plain.Length,
        folderCrc: Crc32.Compute(plain));

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        fileBytes: out byte[] decoded,
        fileName: out string decodedName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(decoded);
    Assert.Equal(string.Empty, decodedName);
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikLzma2Archive_СПаролем_ЗаписываетФайл()
  {
    byte[] plain = CreateGostKuznyechikLzma2PlainForTest();
    const string fileName = "gost-lzma2.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");
    byte[] archive = BuildGostKuznyechikLzma2ArchiveForTest(
        plain: plain,
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
  public void ExtractToDirectory_GostKuznyechikLzma2Archive_БезПароля_ВозвращаетNotSupportedИНичегоНеПишет()
  {
    byte[] plain = CreateGostKuznyechikLzma2PlainForTest();
    const string fileName = "gost-lzma2.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");
    byte[] archive = BuildGostKuznyechikLzma2ArchiveForTest(
        plain: plain,
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
  public void ExtractToDirectory_GostKuznyechikLzma2Archive_СНевернымПаролем_ВозвращаетInvalidDataИНичегоНеПишет()
  {
    byte[] plain = CreateGostKuznyechikLzma2PlainForTest();
    const string fileName = "gost-lzma2.bin";

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");
    byte[] archive = BuildGostKuznyechikLzma2ArchiveForTest(
        plain: plain,
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

  private static byte[] CreateGostKuznyechikLzma2PlainForTest()
  {
    var plain = new byte[256];

    for (int i = 0; i < plain.Length; i++)
    {
      plain[i] = unchecked((byte)(i * 31 + 7));
    }

    return plain;
  }

  private static byte[] BuildGostKuznyechikLzma2ArchiveForTest(
      byte[] plain,
      string fileName,
      SevenZipPassword password)
  {
    byte[] salt = [0xA1, 0xA2];
    byte[] iv = [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0];
    byte[] gostProperties = CreateGostDirectProperties(salt, iv);

    const int dictionarySize = 1 << 20;
    byte[] lzma2Packed = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        gostProperties,
        password,
        lzma2Packed);

    return Build7zArchive_SingleFile_GostKuznyechikThenLzma2(
        packedStreams: encrypted,
        fileName: fileName,
        gostProperties: gostProperties,
        lzma2Properties: [lzma2PropertiesByte],
        gostUnpackSize: (ulong)lzma2Packed.Length,
        finalUnpackSize: (ulong)plain.Length,
        folderCrc: Crc32.Compute(plain));
  }

  private static void AssertDestinationIsEmptyOrMissing(string root, string fileName)
  {
    Assert.False(File.Exists(Path.Combine(root, fileName)));

    if (Directory.Exists(root))
      Assert.Empty(Directory.GetFileSystemEntries(root));
  }

  private static byte[] CreatePlain()
  {
    return System.Text.Encoding.UTF8.GetBytes(
        "LzmaSharp GOST Kuznyechik LZMA2 archive-level test\r\n"
      + "LzmaSharp GOST Kuznyechik LZMA2 archive-level test\r\n");
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

  private static byte[] Build7zArchive_SingleFile_GostKuznyechikThenLzma2(
      byte[] packedStreams,
      string fileName,
      byte[] gostProperties,
      byte[] lzma2Properties,
      ulong gostUnpackSize,
      ulong finalUnpackSize,
      uint? folderCrc)
  {
    byte[] nextHeader = BuildHeaderSingleFolderGostKuznyechikThenLzma2(
        packSize: (ulong)packedStreams.Length,
        gostProperties: gostProperties,
        lzma2Properties: lzma2Properties,
        gostUnpackSize: gostUnpackSize,
        finalUnpackSize: finalUnpackSize,
        fileName: fileName,
        folderCrc: folderCrc);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sigHeader = new SevenZipSignatureHeader(
        VersionMajor: 0,
        VersionMinor: 4,
        StartHeaderCrc: 0,
        NextHeaderOffset: (ulong)packedStreams.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    uint startHeaderCrc = Crc32.Compute(sigHeader.GetStartHeaderBytes());
    sigHeader = sigHeader with { StartHeaderCrc = startHeaderCrc };

    var archive = new List<byte>(
        SevenZipSignatureHeader.TotalSize + packedStreams.Length + nextHeader.Length);

    Span<byte> sigBuf = stackalloc byte[SevenZipSignatureHeader.TotalSize];
    sigHeader.Write(sigBuf);

    archive.AddRange(sigBuf.ToArray());
    archive.AddRange(packedStreams);
    archive.AddRange(nextHeader);

    return [.. archive];
  }

  private static byte[] BuildHeaderSingleFolderGostKuznyechikThenLzma2(
      ulong packSize,
      byte[] gostProperties,
      byte[] lzma2Properties,
      ulong gostUnpackSize,
      ulong finalUnpackSize,
      string fileName,
      uint? folderCrc)
  {
    var header = new List<byte>(256);

    WriteNid(header, SevenZipNid.Header);

    WriteStreamsInfo(
        header,
        packSize: packSize,
        gostProperties: gostProperties,
        lzma2Properties: lzma2Properties,
        gostUnpackSize: gostUnpackSize,
        finalUnpackSize: finalUnpackSize,
        folderCrc: folderCrc);

    WriteFilesInfo(header, fileName);

    WriteNid(header, SevenZipNid.End);

    return [.. header];
  }

  private static void WriteStreamsInfo(
      List<byte> output,
      ulong packSize,
      byte[] gostProperties,
      byte[] lzma2Properties,
      ulong gostUnpackSize,
      ulong finalUnpackSize,
      uint? folderCrc)
  {
    WriteNid(output, SevenZipNid.MainStreamsInfo);

    WritePackInfo(output, packSize);
    WriteUnpackInfo(output, gostProperties, lzma2Properties, gostUnpackSize, finalUnpackSize, folderCrc);
    WriteSubStreamsInfoEmpty(output);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WritePackInfo(
      List<byte> output,
      ulong packSize)
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
      byte[] lzma2Properties,
      ulong gostUnpackSize,
      ulong finalUnpackSize,
      uint? folderCrc)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);

    WriteNid(output, SevenZipNid.Folder);
    WriteEncodedUInt64(output, 1);
    WriteByte(output, 0);

    WriteFolderGostKuznyechikThenLzma2(output, gostProperties, lzma2Properties);

    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, gostUnpackSize);
    WriteEncodedUInt64(output, finalUnpackSize);

    if (folderCrc.HasValue)
    {
      WriteNid(output, SevenZipNid.Crc);
      WriteByte(output, 1);
      WriteUInt32LE(output, folderCrc.Value);
    }

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolderGostKuznyechikThenLzma2(
      List<byte> output,
      byte[] gostProperties,
      byte[] lzma2Properties)
  {
    var gostCoder = new SevenZipCoderInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        properties: gostProperties,
        numInStreams: 1,
        numOutStreams: 1);

    var lzma2Coder = new SevenZipCoderInfo(
        methodId: [0x21],
        properties: lzma2Properties,
        numInStreams: 1,
        numOutStreams: 1);

    WriteEncodedUInt64(output, 2);

    WriteCoderInfo(output, gostCoder);
    WriteCoderInfo(output, lzma2Coder);

    // GOST.out0 -> LZMA2.in1
    WriteEncodedUInt64(output, 1);
    WriteEncodedUInt64(output, 0);

    // PackedStreamIndices не пишем:
    // при NumPackedStreams == 1 parser вычисляет единственный unbound InIndex сам.
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

  private static void WriteSubStreamsInfoEmpty(List<byte> output)
  {
    WriteNid(output, SevenZipNid.SubStreamsInfo);
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFilesInfo(List<byte> output, string fileName)
  {
    WriteNid(output, SevenZipNid.FilesInfo);

    WriteEncodedUInt64(output, 1);

    WriteNid(output, SevenZipNid.Name);

    byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes(fileName);

    WriteEncodedUInt64(output, (ulong)(1 + nameBytes.Length + 2));
    WriteByte(output, 0);
    output.AddRange(nameBytes);
    output.Add(0);
    output.Add(0);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteEncodedUInt64(List<byte> output, ulong value)
  {
    Span<byte> buf = stackalloc byte[9];

    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(
        value,
        buf,
        out int bytesWritten);

    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);
    Assert.True(bytesWritten > 0);

    output.AddRange(buf[..bytesWritten].ToArray());
  }

  private static void WriteByte(List<byte> output, byte value)
  {
    output.Add(value);
  }

  private static void WriteNid(List<byte> output, byte nid)
  {
    output.Add(nid);
  }

  private static void WriteUInt32LE(List<byte> output, uint value)
  {
    output.Add((byte)value);
    output.Add((byte)(value >> 8));
    output.Add((byte)(value >> 16));
    output.Add((byte)(value >> 24));
  }

  private static string CreateTempRoot()
  {
    return Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderGostKuznyechikLzma2Tests),
        Guid.NewGuid().ToString("N"));
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
      // best-effort cleanup для тестового каталога
    }
  }
}
