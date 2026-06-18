using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipUnpackInfoReaderTests
{
  [Fact]
  public void TryRead_MinimalOneFolderOneCoder_ReturnsOk()
  {
    // UnpackInfo ::= kUnpackInfo
    //               kFolder NumFolders=1 External=0
    //                 Folder: NumCoders=1
    //                   Coder: mainByte(idSize=1), id=0x21 (LZMA2)
    //               kCodersUnpackSize [5]
    //               kEnd
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,

      0x01,
      0x01,
      0x21,

      SevenZipNid.CodersUnpackSize,
      0x05,
      SevenZipNid.End,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);
    Assert.Equal(SevenZipUnpackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, consumed);

    Assert.NotNull(unpackInfo);
    Assert.NotNull(unpackInfo.Folders);
    Assert.Single(unpackInfo.Folders);

    SevenZipFolder folder = unpackInfo.Folders[0];
    Assert.Equal((ulong)1, folder.NumInStreams);
    Assert.Equal((ulong)1, folder.NumOutStreams);

    Assert.Single(folder.Coders);
    SevenZipCoderInfo coder = folder.Coders[0];
    Assert.True(coder.MethodId.SequenceEqual(new byte[] { 0x21 }));
    Assert.Empty(coder.Properties);
    Assert.Equal((ulong)1, coder.NumInStreams);
    Assert.Equal((ulong)1, coder.NumOutStreams);

    Assert.NotNull(unpackInfo.FolderUnpackSizes);
    Assert.Single(unpackInfo.FolderUnpackSizes);
    Assert.Single(unpackInfo.FolderUnpackSizes[0]);
    Assert.Equal((ulong)5, unpackInfo.FolderUnpackSizes[0][0]);
  }

  [Fact]
  public void TryRead_Truncated_ReturnsNeedMoreInput_AndConsumesNothing()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,
      0x01,
      0x01,
      0x21,
      SevenZipNid.CodersUnpackSize,
      0x05,
      // нет kEnd
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);
    Assert.Equal(SevenZipUnpackInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_ExternalFolders_NotSupported()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x01, // External=1
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);
    Assert.Equal(SevenZipUnpackInfoReadResult.NotSupported, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_Crc_Truncated_ReturnsNeedMoreInput_AndConsumesNothing()
  {
    // То же, что минимальный кейс, но после unpack size идёт kCRC (и дальше данных не хватает).
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
    SevenZipNid.Folder,
    0x01,
    0x00,

    0x01,
    0x01,
    0x21,

    SevenZipNid.CodersUnpackSize,
    0x05,

    SevenZipNid.Crc,
    0x00, // AllAreDefined = 0, но дальше нужен битовый вектор Defined[NumFolders]
  ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_Crc_AllAreDefined_ReturnsOk()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
    SevenZipNid.Folder,
    0x01,
    0x00,

    0x01,
    0x01,
    0x21,

    SevenZipNid.CodersUnpackSize,
    0x05,

    SevenZipNid.Crc,
    0x01,                   // AllAreDefined = 1
    0x44, 0x33, 0x22, 0x11,  // CRC (1 шт)

    SevenZipNid.End,
  ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, consumed);
    Assert.NotNull(unpackInfo);
    Assert.Single(unpackInfo.Folders);
    Assert.Single(unpackInfo.FolderUnpackSizes);
    Assert.Single(unpackInfo.FolderUnpackSizes[0]);
    Assert.Equal((ulong)5, unpackInfo.FolderUnpackSizes[0][0]);

    Assert.NotNull(unpackInfo.FolderCrcDefined);
    Assert.Equal([true], unpackInfo.FolderCrcDefined!);

    Assert.NotNull(unpackInfo.FolderCrc);
    Assert.Equal(0x11223344u, unpackInfo.FolderCrc![0]); // 44 33 22 11 (LE)
  }

  [Fact]
  public void TryRead_TwoCoders_OneBindPair_NumPackedStreams1_DerivesPackedStreamIndex1()
  {
    // Folder:
    // Coders: [Copy, LZMA2]
    // BindPair: InIndex=0 <- OutIndex=1  (вход coder0 связан с выходом coder1)
    // Тогда единственный "не связанный" InIndex = 1 => PackedStreamIndices = [1]
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
    SevenZipNid.Folder,
    0x01, // NumFolders=1
    0x00, // External=0

    0x02, // NumCoders=2

    0x01, // coder0 mainByte: idSize=1, без props
    0x00, // methodId: Copy

    0x21, // coder1 mainByte: idSize=1 + hasProps
    0x21, // methodId: LZMA2
    0x01, // propsSize=1
    0x00, // props byte (неважно для этого теста)

    // BindPairs (TotalOutStreams-1 = 1)
    0x00, // InIndex = 0
    0x01, // OutIndex = 1

    SevenZipNid.CodersUnpackSize,
    0x05, // outSize[0]
    0x05, // outSize[1]

    SevenZipNid.End,
  ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, consumed);

    Assert.Single(unpackInfo.Folders);

    SevenZipFolder folder = unpackInfo.Folders[0];

    Assert.Equal(2, folder.Coders.Length);
    Assert.Single(folder.BindPairs);
    Assert.Equal(0UL, folder.BindPairs[0].InIndex);
    Assert.Equal(1UL, folder.BindPairs[0].OutIndex);

    Assert.Single(folder.PackedStreamIndices);
    Assert.Equal(1UL, folder.PackedStreamIndices[0]);

    Assert.Equal(2UL, folder.NumInStreams);
    Assert.Equal(2UL, folder.NumOutStreams);

    Assert.Single(unpackInfo.FolderUnpackSizes);
    Assert.Equal(2, unpackInfo.FolderUnpackSizes[0].Length);
  }

  [Fact]
  public void TryRead_Crc_PartialDefined_StoresCrcOnlyForDefinedFolders()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
    SevenZipNid.Folder,
    0x02, // NumFolders=2
    0x00, // External=0

    // Folder0: NumCoders=1, Coder: idSize=1, id=0x21 (LZMA2)
    0x01, 0x01, 0x21,

    // Folder1: NumCoders=1, Coder: idSize=1, id=0x21 (LZMA2)
    0x01, 0x01, 0x21,

    SevenZipNid.CodersUnpackSize,
    0x05, // folder0 outSize
    0x06, // folder1 outSize

    SevenZipNid.Crc,
    0x00, // AllAreDefined=0
    0x80, // Defined bitfield: [true,false] (MSB first)
    0x44, 0x33, 0x22, 0x11, // CRC только для folder0
    SevenZipNid.End,
  ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out var unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, consumed);

    Assert.NotNull(unpackInfo);
    Assert.NotNull(unpackInfo.FolderCrcDefined);
    Assert.Equal([true, false], unpackInfo.FolderCrcDefined!);

    Assert.NotNull(unpackInfo.FolderCrc);
    Assert.Equal(0x11223344u, unpackInfo.FolderCrc![0]);
    Assert.Equal(0u, unpackInfo.FolderCrc![1]);
  }


  [Fact]
  public void TryRead_EmptyBuffer_ReturnsNeedMoreInput_AndConsumesNothing()
  {
    var res = SevenZipUnpackInfoReader.TryRead([], out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_FirstByteIsNotUnpackInfo_ReturnsInvalidData_AndConsumesNothing()
  {
    byte[] data =
    [
      SevenZipNid.PackInfo,
      SevenZipNid.End,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_NumCodersIsZero_ReturnsInvalidData_AndConsumesNothing()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,
      0x00,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_MethodIdSizeIsZero_ReturnsInvalidData_AndConsumesNothing()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,
      0x01,
      0x00,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_ReservedBit6IsSet_ReturnsInvalidData_AndConsumesNothing()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,
      0x01,
      0x41,
      0x21,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_AlternativeMethodsBitIsSet_ReturnsInvalidData_AndConsumesNothing()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,
      0x01,
      0x81,
      0x21,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_ComplexCoderWithZeroInputStreams_ReturnsInvalidData_AndConsumesNothing()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,
      0x01,
      0x11,
      0x21,
      0x00,
      0x01,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_ComplexCoderWithZeroOutputStreams_ReturnsInvalidData_AndConsumesNothing()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,
      0x01,
      0x11,
      0x21,
      0x01,
      0x00,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }
}