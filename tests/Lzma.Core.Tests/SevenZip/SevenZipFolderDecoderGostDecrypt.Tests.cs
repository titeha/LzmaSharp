using Lzma.Core.Crypto.Gost;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderGostDecryptTests
{
  [Fact]
  public void DecodeFolderToArray_KuznyechikCopyPipeline_СDirectKey_ДекодируетЗашифрованныйВход()
  {
    byte[] plain = System.Text.Encoding.UTF8.GetBytes(
        "LzmaSharp GOST Kuznyechik FolderDecoder test");

    byte[] salt = [0xA1, 0xA2];
    byte[] iv = [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0];
    byte[] properties = CreateGostDirectProperties(salt, iv);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        properties,
        password,
        plain);

    SevenZipStreamsInfo streamsInfo = CreateGostThenCopyStreamsInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        gostProperties: properties,
        gostUnpackSize: (ulong)plain.Length,
        finalUnpackSize: (ulong)plain.Length,
        packSize: (ulong)encrypted.Length);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encrypted,
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(plain, output);
  }

  [Fact]
  public void DecodeFolderToArray_KuznyechikCopyPipeline_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = new byte[16];

    byte[] properties = CreateGostDirectProperties(
        salt: [],
        iv: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        properties,
        password,
        plain);

    SevenZipStreamsInfo streamsInfo = CreateGostThenCopyStreamsInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        gostProperties: properties,
        gostUnpackSize: (ulong)plain.Length,
        finalUnpackSize: (ulong)plain.Length,
        packSize: (ulong)encrypted.Length);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encrypted,
        folderIndex: 0,
        options: SevenZipDecodeOptions.Default,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_KuznyechikCopyPipeline_СНекорректнымIv_ВозвращаетInvalidData()
  {
    byte[] properties = CreateGostDirectProperties(
        salt: [],
        iv: [0x12, 0x34, 0x56]);

    SevenZipStreamsInfo streamsInfo = CreateGostThenCopyStreamsInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        gostProperties: properties,
        gostUnpackSize: 16UL,
        finalUnpackSize: 16UL,
        packSize: 16UL);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: new byte[16],
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_KuznyechikCopyPipeline_ПослеРасшифровкиРазмерНеСовпадает_ВозвращаетInvalidData()
  {
    byte[] plain = new byte[16];

    byte[] properties = CreateGostDirectProperties(
        salt: [],
        iv: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        properties,
        password,
        plain);

    SevenZipStreamsInfo streamsInfo = CreateGostThenCopyStreamsInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        gostProperties: properties,
        gostUnpackSize: 15UL,
        finalUnpackSize: 16UL,
        packSize: (ulong)encrypted.Length);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encrypted,
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_MagmaCopyPipeline_ПокаВозвращаетNotSupported()
  {
    byte[] properties = CreateGostDirectProperties(
        salt: [],
        iv: [0x12, 0x34, 0x56, 0x78]);

    SevenZipStreamsInfo streamsInfo = CreateGostThenCopyStreamsInfo(
        methodId: SevenZipGostCoder.MagmaMethodId.ToArray(),
        gostProperties: properties,
        gostUnpackSize: 16UL,
        finalUnpackSize: 16UL,
        packSize: 16UL);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: new byte[16],
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_KuznyechikCopyPipeline_БезDirectKey_ВозвращаетNotSupported()
  {
    byte[] properties =
    [
      SevenZipGostCoder.CurrentPropertiesVersion,
      0x00,
      0x03, // обычный KDF пока не подключён
      0x00,
      0x08,
      0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0,
    ];

    SevenZipStreamsInfo streamsInfo = CreateGostThenCopyStreamsInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        gostProperties: properties,
        gostUnpackSize: 16UL,
        finalUnpackSize: 16UL,
        packSize: 16UL);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: new byte[16],
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_KuznyechikLzma2Pipeline_СDirectKey_ДекодируетИсходныеБайты()
  {
    byte[] plain = System.Text.Encoding.UTF8.GetBytes(
        "LzmaSharp GOST Kuznyechik LZMA2 FolderDecoder test\r\n"
      + "LzmaSharp GOST Kuznyechik LZMA2 FolderDecoder test\r\n");

    byte[] salt = [0xA1, 0xA2];
    byte[] iv = [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0];
    byte[] properties = CreateGostDirectProperties(salt, iv);

    const int dictionarySize = 1 << 20;

    byte[] lzma2Packed = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        properties,
        password,
        lzma2Packed);

    SevenZipStreamsInfo streamsInfo = CreateGostThenLzma2StreamsInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        gostProperties: properties,
        lzma2Properties: [lzma2PropertiesByte],
        gostUnpackSize: (ulong)lzma2Packed.Length,
        finalUnpackSize: (ulong)plain.Length,
        packSize: (ulong)encrypted.Length);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encrypted,
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(plain, output);
  }

  [Fact]
  public void DecodeFolderToArray_KuznyechikLzma2Pipeline_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = System.Text.Encoding.UTF8.GetBytes(
        "LzmaSharp GOST Kuznyechik LZMA2 FolderDecoder test\r\n");

    byte[] properties = CreateGostDirectProperties(
        salt: [],
        iv: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    const int dictionarySize = 1 << 20;

    byte[] lzma2Packed = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        properties,
        password,
        lzma2Packed);

    SevenZipStreamsInfo streamsInfo = CreateGostThenLzma2StreamsInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        gostProperties: properties,
        lzma2Properties: [lzma2PropertiesByte],
        gostUnpackSize: (ulong)lzma2Packed.Length,
        finalUnpackSize: (ulong)plain.Length,
        packSize: (ulong)encrypted.Length);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encrypted,
        folderIndex: 0,
        options: SevenZipDecodeOptions.Default,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_KuznyechikLzma2Pipeline_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] plain = System.Text.Encoding.UTF8.GetBytes(
        "LzmaSharp GOST Kuznyechik LZMA2 FolderDecoder test\r\n");

    byte[] properties = CreateGostDirectProperties(
        salt: [0xA1, 0xA2],
        iv: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    const int dictionarySize = 1 << 20;

    byte[] lzma2Packed = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        properties,
        correctPassword,
        lzma2Packed);

    SevenZipStreamsInfo streamsInfo = CreateGostThenLzma2StreamsInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        gostProperties: properties,
        lzma2Properties: [lzma2PropertiesByte],
        gostUnpackSize: (ulong)lzma2Packed.Length,
        finalUnpackSize: (ulong)plain.Length,
        packSize: (ulong)encrypted.Length);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encrypted,
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_KuznyechikLzma2Pipeline_ПослеРасшифровкиРазмерGostНеСовпадает_ВозвращаетInvalidData()
  {
    byte[] plain = System.Text.Encoding.UTF8.GetBytes(
        "LzmaSharp GOST Kuznyechik LZMA2 FolderDecoder test\r\n");

    byte[] properties = CreateGostDirectProperties(
        salt: [],
        iv: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    const int dictionarySize = 1 << 20;

    byte[] lzma2Packed = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    byte[] encrypted = EncryptKuznyechikDirectKeyForTest(
        properties,
        password,
        lzma2Packed);

    SevenZipStreamsInfo streamsInfo = CreateGostThenLzma2StreamsInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        gostProperties: properties,
        lzma2Properties: [lzma2PropertiesByte],
        gostUnpackSize: (ulong)lzma2Packed.Length + 1,
        finalUnpackSize: (ulong)plain.Length,
        packSize: (ulong)encrypted.Length);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encrypted,
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
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

  private static SevenZipStreamsInfo CreateGostThenCopyStreamsInfo(
      byte[] methodId,
      byte[] gostProperties,
      ulong gostUnpackSize,
      ulong finalUnpackSize,
      ulong packSize)
  {
    var gostCoder = new SevenZipCoderInfo(
        methodId: methodId,
        properties: gostProperties,
        numInStreams: 1,
        numOutStreams: 1);

    var copyCoder = new SevenZipCoderInfo(
        methodId: [0x00],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    var folder = new SevenZipFolder(
        Coders:
        [
          gostCoder,
          copyCoder,
        ],
        BindPairs:
        [
          // GOST.out0 -> Copy.in1
          new SevenZipBindPair(InIndex: 1, OutIndex: 0),
        ],
        PackedStreamIndices: [0UL],
        NumInStreams: 2,
        NumOutStreams: 2);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [packSize]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [
            gostUnpackSize,
            finalUnpackSize,
          ],
        ]);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }

  private static SevenZipStreamsInfo CreateGostThenLzma2StreamsInfo(
    byte[] methodId,
    byte[] gostProperties,
    byte[] lzma2Properties,
    ulong gostUnpackSize,
    ulong finalUnpackSize,
    ulong packSize)
  {
    var gostCoder = new SevenZipCoderInfo(
        methodId: methodId,
        properties: gostProperties,
        numInStreams: 1,
        numOutStreams: 1);

    var lzma2Coder = new SevenZipCoderInfo(
        methodId: [0x21],
        properties: lzma2Properties,
        numInStreams: 1,
        numOutStreams: 1);

    var folder = new SevenZipFolder(
        Coders:
        [
          gostCoder,
        lzma2Coder,
        ],
        BindPairs:
        [
          // GOST.out0 -> LZMA2.in1
          new SevenZipBindPair(InIndex: 1, OutIndex: 0),
        ],
        PackedStreamIndices: [0UL],
        NumInStreams: 2,
        NumOutStreams: 2);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [packSize]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [
          gostUnpackSize,
          finalUnpackSize,
        ],
        ]);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }
}
