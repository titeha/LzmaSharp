using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipFolderDecoderTests
{
  [Fact]
  public void DecodeFolderToStream_СовпадаетС_DecodeFolderToArray_Lzma2()
  {
    // Многочанковый (>64 КБ) сжимаемый файл → реальный LZMA2-folder.
    byte[] plain = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Folder в поток 0123456789 ", 6000)));

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("big.bin", plain)],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive));

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out _));
    Assert.True(reader.Header.HasValue);

    SevenZipStreamsInfo streamsInfo = reader.Header.Value.StreamsInfo;
    ReadOnlySpan<byte> packed = reader.PackedStreams.Span;

    // Эталон — folder в массив.
    SevenZipFolderDecodeResult arrayResult = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo, packed, folderIndex: 0, out byte[] fromArray);
    Assert.Equal(SevenZipFolderDecodeResult.Ok, arrayResult);
    Assert.Equal(plain, fromArray);

    // Потоковый — folder напрямую в Stream (без накопления всего выхода).
    using var ms = new MemoryStream();
    SevenZipFolderDecodeResult streamResult = SevenZipFolderDecoder.DecodeFolderToStream(
        streamsInfo, packed, folderIndex: 0, SevenZipDecodeOptions.Default, ms, out long written);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, streamResult);
    Assert.Equal(fromArray.LongLength, written);
    Assert.Equal(fromArray, ms.ToArray());
  }

  [Fact]
  public void DecodeFolder_SingleFolder_Lzma2Copy_Returns_OriginalBytes()
  {
    byte[] plain =
    [
        0x41, 0x42, 0x43, 0x44, 0x45,
            0x00,
            0xFF,
            0x10, 0x20, 0x30,
            0x41, 0x42, 0x43, 0x44, 0x45,
        ];

    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(
        plainFileBytes: plain,
        fileName: "file.bin",
        dictionarySize: 1 << 20);

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult r = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.True(reader.Header.HasValue);
    SevenZipHeader header = reader.Header.Value;

    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;
    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(1UL, filesInfo.FileCount);
    Assert.True(filesInfo.HasNames);
    Assert.Equal("file.bin", filesInfo.Names![0]);

    ReadOnlySpan<byte> packedStreams = reader.PackedStreams.Span;

    SevenZipFolderDecodeResult decodeResult = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] folderBytes);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, decodeResult);
    Assert.Equal(plain, folderBytes);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma2_InvalidDictionaryProp_ReturnsInvalidData()
  {
    // pack stream пустой — нам не важно: мы должны отвалиться ДО вызова LZMA2-декодера,
    // потому что properties битые (41 > 40).
    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [0]);

    var coder = new SevenZipCoderInfo(
        methodId: [0x21],     // LZMA2
        properties: [41],     // invalid
        numInStreams: 1,
        numOutStreams: 1);

    var folder = new SevenZipFolder(
        Coders: [coder],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 1,
        NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes: [[0]]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams: [],
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma2_EmptyProperties_ReturnsInvalidData()
  {
    var coder = new SevenZipCoderInfo(
        methodId: [0x21], // LZMA2
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: [],
        expectedUnpackSize: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma2_DictionaryGreaterThanIntMaxValue_ReturnsNotSupported()
  {
    // p == 40 => dictionarySize = 0xFFFFFFFF, то есть больше Int32.MaxValue.
    var coder = new SevenZipCoderInfo(
        methodId: [0x21], // LZMA2
        properties: [40],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: [],
        expectedUnpackSize: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma2_НулевойХвостPackedStream_ВозвращаетOk()
  {
    // Здесь нам важно не то, какой именно вид LZMA2-чанка использован,
    // а то, что после корректного конца потока останется хвост,
    // который SevenZipFolderDecoder отдельно провалидирует.
    byte[] plain = [0x41, 0x42, 0x00, 0xFF, 0x10, 0x20];

    const int dictionarySize = 1 << 20;
    byte[] packedCore = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    byte[] packed = [.. packedCore, 0x00, 0x00, 0x00];

    var coder = new SevenZipCoderInfo(
        methodId: [0x21], // LZMA2
        properties: [lzma2PropertiesByte],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: plain.Length,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(plain, output);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma2_НенулевойХвостPackedStream_ВозвращаетInvalidData()
  {
    byte[] plain = [0x41, 0x42, 0x00, 0xFF, 0x10, 0x20];

    const int dictionarySize = 1 << 20;
    byte[] packedCore = Lzma2CopyEncoder.Encode(
        plain,
        dictionarySize,
        out byte lzma2PropertiesByte);

    byte[] packed = [.. packedCore, 0x00, 0x01];

    var coder = new SevenZipCoderInfo(
        methodId: [0x21], // LZMA2
        properties: [lzma2PropertiesByte],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: plain.Length,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolder_SingleFolder_Lzma2LzmaChunkedLiteralOnly_Returns_OriginalBytes()
  {
    // Важно: вход больше maxUnpackChunkSize, чтобы гарантировать несколько LZMA-чанков.
    var plain = new byte[256];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2LzmaChunkedLiteralOnly(
        plainFileBytes: plain,
        fileName: "file-lzma2.bin",
        dictionarySize: 1 << 20,
        maxUnpackChunkSize: 64);

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult r = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.True(reader.Header.HasValue);
    SevenZipHeader header = reader.Header.Value;

    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;
    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(1UL, filesInfo.FileCount);
    Assert.True(filesInfo.HasNames);
    Assert.Equal("file-lzma2.bin", filesInfo.Names![0]);

    ReadOnlySpan<byte> packedStreams = reader.PackedStreams.Span;

    SevenZipFolderDecodeResult decodeResult = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] folderBytes);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, decodeResult);
    Assert.Equal(plain, folderBytes);
  }

  [Fact]
  public void DecodeSingleFile_ОдинФайл_ОдинFolder_Lzma2LzmaChunkedLiteralOnly_ВозвращаетИсходныеБайты()
  {
    // Важно: вход больше maxUnpackChunkSize, чтобы гарантировать несколько LZMA-чанков.
    var plain = new byte[256];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    const string fileName = "file-lzma2.bin";

    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2LzmaChunkedLiteralOnly(
      plainFileBytes: plain,
      fileName: fileName,
      dictionarySize: 1 << 20,
      maxUnpackChunkSize: 64);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive,
      out byte[] decoded,
      out string? decodedName,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(plain, decoded);
    Assert.Equal(fileName, decodedName);
    Assert.True(bytesConsumed > 0);
    Assert.True(bytesConsumed <= archive.Length);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma1_LiteralOnly_Returns_OriginalBytes()
  {
    var plain = new byte[256];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 17 + 3);

    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    const int dictionarySize = 1 << 20;

    // Raw LZMA stream (без LZMA-Alone header).
    byte[] packed = new LzmaEncoder(lzmaProps, dictionarySize).EncodeLiteralOnly(plain);

    byte[] coderProps = new byte[5];
    coderProps[0] = lzmaProps.ToByteOrThrow();
    BinaryPrimitives.WriteUInt32LittleEndian(coderProps.AsSpan(1, 4), (uint)dictionarySize);

    var packInfo = new SevenZipPackInfo(packPos: 0, packSizes: [(ulong)packed.Length]);

    var coder = new SevenZipCoderInfo(
      methodId: [0x03, 0x01, 0x01], // LZMA
      properties: coderProps,
      numInStreams: 1,
      numOutStreams: 1);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[(ulong)plain.Length]]);

    var streamsInfo = new SevenZipStreamsInfo(
      packInfo: packInfo,
      unpackInfo: unpackInfo,
      subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
      streamsInfo,
      packedStreams: packed,
      folderIndex: 0,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(plain, output);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma_ZeroDictionary_ReturnsInvalidData()
  {
    byte[] coderProps = new byte[5];
    coderProps[0] = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2).ToByteOrThrow();
    BinaryPrimitives.WriteUInt32LittleEndian(coderProps.AsSpan(1, 4), 0u);

    var coder = new SevenZipCoderInfo(
        methodId: [0x03, 0x01, 0x01], // LZMA
        properties: coderProps,
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: [],
        expectedUnpackSize: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma_DictionaryGreaterThanIntMaxValue_ReturnsNotSupported()
  {
    byte[] coderProps = new byte[5];
    coderProps[0] = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2).ToByteOrThrow();
    BinaryPrimitives.WriteUInt32LittleEndian(coderProps.AsSpan(1, 4), 0x8000_0000u);

    var coder = new SevenZipCoderInfo(
        methodId: [0x03, 0x01, 0x01], // LZMA
        properties: coderProps,
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: [],
        expectedUnpackSize: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma_InvalidPropertiesLength_ReturnsInvalidData()
  {
    var coder = new SevenZipCoderInfo(
        methodId: [0x03, 0x01, 0x01], // LZMA
        properties: [0x5D, 0x00, 0x00, 0x10], // 4 байта вместо обязательных 5
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: [],
        expectedUnpackSize: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma_InvalidPropertiesByte_ReturnsInvalidData()
  {
    byte[] coderProps = new byte[5];
    coderProps[0] = 0xE1; // 225 > 224, недопустимый байт свойств LZMA
    BinaryPrimitives.WriteUInt32LittleEndian(coderProps.AsSpan(1, 4), 4u);

    var coder = new SevenZipCoderInfo(
        methodId: [0x03, 0x01, 0x01], // LZMA
        properties: coderProps,
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: [],
        expectedUnpackSize: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_BcjArm64_BlInstruction_IsDecoded()
  {
    // Делаем буфер: 64 байта мусора + 1 инструкция BL (4 байта).
    // На вход подаём "фильтрованные" данные: imm26 хранит (target >> 2) = 64 => 0x94000040.
    // На выходе ожидаем "оригинал": imm26 должен стать (target>>2 - pc>>2).
    // pc = 64 => pc>>2 = 16. 64 - 16 = 48 => 0x94000030.

    byte[] packed = new byte[64 + 4];
    packed[64] = 0x40;
    packed[65] = 0x00;
    packed[66] = 0x00;
    packed[67] = 0x94;

    byte[] expected = new byte[64 + 4];
    expected[64] = 0x30;
    expected[65] = 0x00;
    expected[66] = 0x00;
    expected[67] = 0x94;

    var packInfo = new SevenZipPackInfo(packPos: 0, packSizes: [(ulong)packed.Length]);

    var copyCoder = new SevenZipCoderInfo(
      methodId: [0x00],      // Copy
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var arm64Coder = new SevenZipCoderInfo(
      methodId: [0x0A],      // ARM64 BCJ
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var folder = new SevenZipFolder(
      Coders: [copyCoder, arm64Coder],
      BindPairs: [new SevenZipBindPair(InIndex: 1, OutIndex: 0)], // out0 -> in1
      PackedStreamIndices: [0],                                   // packed = in0
      NumInStreams: 2,
      NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[(ulong)packed.Length, (ulong)packed.Length]]);

    var streamsInfo = new SevenZipStreamsInfo(
      packInfo: packInfo,
      unpackInfo: unpackInfo,
      subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
      streamsInfo: streamsInfo,
      packedStreams: packed,
      folderIndex: 0,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(expected, output);
  }

  [Fact]
  public void DecodeFolderToArray_Bcj_LengthMismatch_ReturnsInvalidData()
  {
    // Для всех BCJ-фильтров размер не должен изменяться.
    byte[] packed = [0x00, 0x00, 0x00, 0x00];

    byte[][] methodIds =
    [
      [0x04], // x86
    [0x07], // ARM
    [0x08], // ARMT
    [0x05], // PPC
    [0x09], // SPARC
    [0x06], // IA64
    [0x0A], // ARM64
  ];

    foreach (byte[] methodId in methodIds)
    {
      var coder = new SevenZipCoderInfo(
          methodId: methodId,
          properties: [],
          numInStreams: 1,
          numOutStreams: 1);

      SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
          coder: coder,
          packed: packed,
          expectedUnpackSize: packed.Length - 1,
          output: out byte[] output);

      Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
      Assert.Empty(output);
    }
  }

  [Fact]
  public void DecodeFolderToArray_Swap2_ReturnsSwappedPairs()
  {
    byte[] packed = [0x10, 0x20, 0x30, 0x40];

    var coder = new SevenZipCoderInfo(
      methodId: [0x02, 0x03, 0x02],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0x20, 0x10, 0x40, 0x30 }, output);
  }

  [Fact]
  public void DecodeFolderToArray_Swap2_OddTail_PreservesTail()
  {
    byte[] packed = [0x10, 0x20, 0x30, 0x40, 0x55];

    var coder = new SevenZipCoderInfo(
      methodId: [0x02, 0x03, 0x02],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0x20, 0x10, 0x40, 0x30, 0x55 }, output);
  }

  [Fact]
  public void DecodeFolderToArray_Swap2_WithProperties_ReturnsInvalidData()
  {
    byte[] packed = [0x10, 0x20, 0x30, 0x40];

    var coder = new SevenZipCoderInfo(
      methodId: [0x02, 0x03, 0x02],
      properties: [0x01],
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Swap4_ReturnsWordsWithReversedByteOrder()
  {
    byte[] packed = [0x10, 0x20, 0x30, 0x40, 0xAA, 0xBB, 0xCC, 0xDD];

    var coder = new SevenZipCoderInfo(
      methodId: [0x02, 0x03, 0x04],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0x40, 0x30, 0x20, 0x10, 0xDD, 0xCC, 0xBB, 0xAA }, output);
  }

  [Fact]
  public void DecodeFolderToArray_Swap4_TailShorterThanWord_PreservesTail()
  {
    byte[] packed = [0x10, 0x20, 0x30, 0x40, 0x55, 0x66];

    var coder = new SevenZipCoderInfo(
      methodId: [0x02, 0x03, 0x04],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0x40, 0x30, 0x20, 0x10, 0x55, 0x66 }, output);
  }

  [Fact]
  public void DecodeFolderToArray_Swap4_WithProperties_ReturnsInvalidData()
  {
    byte[] packed = [0x10, 0x20, 0x30, 0x40];

    var coder = new SevenZipCoderInfo(
      methodId: [0x02, 0x03, 0x04],
      properties: [0x01],
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Delta_EmptyProperties_UsesDelta1()
  {
    // Для delta=1 поток хранит первый байт как есть,
    // а дальше — разности относительно предыдущего декодированного байта.
    byte[] packed = [0x10, 0x10, 0x10, 0x10];

    var coder = new SevenZipCoderInfo(
      methodId: [0x03],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 0x40 }, output);
  }

  [Fact]
  public void DecodeFolderToArray_Delta_PropertyOne_UsesDelta2()
  {
    // properties[0] = delta - 1, поэтому 0x01 => delta = 2.
    byte[] packed = [0x10, 0x20, 0x20, 0x20, 0x20, 0x20];

    var coder = new SevenZipCoderInfo(
      methodId: [0x03],
      properties: [0x01],
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60 }, output);
  }

  [Fact]
  public void DecodeFolderToArray_Delta_InvalidPropertiesLength_ReturnsInvalidData()
  {
    byte[] packed = [0x10, 0x20, 0x30, 0x40];

    var coder = new SevenZipCoderInfo(
      methodId: [0x03],
      properties: [0x00, 0x01],
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Swap2_НесовпадениеРазмера_ВозвращаетInvalidData()
  {
    byte[] packed = [0x10, 0x20, 0x30, 0x40];

    var coder = new SevenZipCoderInfo(
        methodId: [0x02, 0x03, 0x02],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: packed.Length - 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Swap4_НесовпадениеРазмера_ВозвращаетInvalidData()
  {
    byte[] packed = [0x10, 0x20, 0x30, 0x40, 0xAA, 0xBB, 0xCC, 0xDD];

    var coder = new SevenZipCoderInfo(
        methodId: [0x02, 0x03, 0x04],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: packed.Length - 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Delta_НесовпадениеРазмера_ВозвращаетInvalidData()
  {
    byte[] packed = [0x10, 0x10, 0x10, 0x10];

    var coder = new SevenZipCoderInfo(
        methodId: [0x03],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: packed.Length - 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_BcjArm64_WithStartOffset_UsesOffsetInProgramCounter()
  {
    // Та же BL-инструкция, что и в тесте без props,
    // но теперь pc начинается не с 64, а с 64 + startOffset.
    // startOffset = 4 => pc = 68 => pc>>2 = 17.
    // 64 - 17 = 47 => 0x9400002F.
    byte[] packed = new byte[64 + 4];
    packed[64] = 0x40;
    packed[65] = 0x00;
    packed[66] = 0x00;
    packed[67] = 0x94;

    byte[] properties = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(properties, 4u);

    var coder = new SevenZipCoderInfo(
      methodId: [0x0A],
      properties: properties,
      numInStreams: 1,
      numOutStreams: 1);

    SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
      coder: coder,
      packed: packed,
      expectedUnpackSize: packed.Length,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(0x2F, output[64]);
    Assert.Equal(0x00, output[65]);
    Assert.Equal(0x00, output[66]);
    Assert.Equal(0x94, output[67]);
  }

  [Fact]
  public void DecodeFolderToArray_Bcj_InvalidPropertiesLength_ReturnsInvalidData()
  {
    byte[] packed = [0x00, 0x00, 0x00, 0x00];

    byte[][] methodIds =
    [
      [0x04], // x86
      [0x07], // ARM
      [0x08], // ARMT
      [0x05], // PPC
      [0x09], // SPARC
      [0x06], // IA64
      [0x0A], // ARM64
    ];

    foreach (byte[] methodId in methodIds)
    {
      var coder = new SevenZipCoderInfo(
        methodId: methodId,
        properties: [0x01],
        numInStreams: 1,
        numOutStreams: 1);

      SevenZipFolderDecodeResult result = DecodeSingleCoderFolderToArray(
        coder: coder,
        packed: packed,
        expectedUnpackSize: packed.Length,
        output: out byte[] output);

      Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
      Assert.Empty(output);
    }
  }

  private static SevenZipFolderDecodeResult DecodeSingleCoderFolderToArray(
    SevenZipCoderInfo coder,
    ReadOnlySpan<byte> packed,
    int expectedUnpackSize,
    out byte[] output)
  {
    var packInfo = new SevenZipPackInfo(
      packPos: 0,
      packSizes: [(ulong)packed.Length]);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[(ulong)expectedUnpackSize]]);

    var streamsInfo = new SevenZipStreamsInfo(
      packInfo: packInfo,
      unpackInfo: unpackInfo,
      subStreamsInfo: null);

    return SevenZipFolderDecoder.DecodeFolderToArray(
      streamsInfo: streamsInfo,
      packedStreams: packed,
      folderIndex: 0,
      output: out output);
  }

  private static byte[] Build7zArchive_SingleFile_SingleFolder_Lzma2LzmaChunkedLiteralOnly(
      ReadOnlySpan<byte> plainFileBytes,
      string fileName,
      int dictionarySize,
      int maxUnpackChunkSize)
  {
    // Упакованные данные (LZMA2 stream с LZMA-чанками).
    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);

    byte[] packedStream = Lzma2LzmaEncoder.EncodeLiteralOnlyChunked(
        data: plainFileBytes,
        lzmaProperties: lzmaProps,
        dictionarySize: dictionarySize,
        maxUnpackChunkSize: maxUnpackChunkSize,
        out _);

    // Байт properties для кодера LZMA2 в 7z — это кодировка dictionary size.
    // Чтобы не дублировать маппинг dictionarySize -> propertiesByte в тесте,
    // берём его из Lzma2CopyEncoder (для пустого ввода).
    _ = Lzma2CopyEncoder.Encode([], dictionarySize, out byte lzma2PropertiesByte);

    return Build7zArchive_SingleFile_SingleFolder_Lzma2PackedStream(
        packedStream: packedStream,
        lzma2PropertiesByte: lzma2PropertiesByte,
        plainFileBytesLength: plainFileBytes.Length,
        fileName: fileName);
  }

  private static byte[] Build7zArchive_SingleFile_SingleFolder_Lzma2PackedStream(
      ReadOnlySpan<byte> packedStream,
      byte lzma2PropertiesByte,
      int plainFileBytesLength,
      string fileName)
  {
    // ----- NextHeader ("Header") -----
    // 7z Header = [Header][MainStreamsInfo][FilesInfo][End]
    List<byte> headerPayload =
    [
      SevenZipNid.Header,
      // MainStreamsInfo
      SevenZipNid.MainStreamsInfo,
      // StreamsInfo = [PackInfo][UnpackInfo][SubStreamsInfo][End]
      SevenZipNid.PackInfo,
    ];
    // PackPos
    WriteU64(headerPayload, 0);
    // NumPackStreams
    WriteU64(headerPayload, 1);
    // Size
    headerPayload.Add(SevenZipNid.Size);
    WriteU64(headerPayload, (ulong)packedStream.Length);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.UnpackInfo);
    // Folder
    headerPayload.Add(SevenZipNid.Folder);
    WriteU64(headerPayload, 1); // NumFolders
    headerPayload.Add(0); // External = 0

    // Folder описывает цепочку кодеров.
    // NumCoders
    WriteU64(headerPayload, 1);

    // Coder info:
    //   MainByte:
    //     - IDSize = 1 (младшие 4 бита: 1 => 1 байт; 0 считается ошибкой)
    //     - IsSimple = 1 (один входной и один выходной поток)
    //     - HasProperties = 1 (есть свойства кодера)
    //     - HasAttributes = 0
    //     - MoreAlternativeMethods = 0
    //
    // Важно: в 7z флаг "simple" кодируется как (MainByte & 0x10) == 0.
    headerPayload.Add(0b0010_0001); // MainByte: idSize=1 (младшие 4 бита), hasProps=1
                                    // MethodID (LZMA2 = 0x21)
    headerPayload.Add(0x21);
    // Properties
    headerPayload.Add(1); // properties size
    headerPayload.Add(lzma2PropertiesByte);

    // BindPairs и PackedStreamIndices можно не писать:
    //  - при одном выходном потоке NumBindPairs == 0,
    //  - при NumPackedStreams == 1 индекс подразумевается равным 0.

    headerPayload.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(headerPayload, (ulong)plainFileBytesLength);

    headerPayload.Add(SevenZipNid.End); // End of UnpackInfo

    // SubStreamsInfo
    headerPayload.Add(SevenZipNid.SubStreamsInfo);
    headerPayload.Add(SevenZipNid.NumUnpackStream);
    WriteU64(headerPayload, 1);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.End); // End of MainStreamsInfo

    // FilesInfo
    headerPayload.Add(SevenZipNid.FilesInfo);
    WriteU64(headerPayload, 1); // NumFiles

    // Name property.
    headerPayload.Add(SevenZipNid.Name);

    // Данные свойства "Name" идут как:
    //   Size (u64)
    //   External (byte)
    //   UTF-16LE null-terminated strings
    // Здесь: один файл.
    var nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(headerPayload, (ulong)(1 + nameBytes.Length));
    headerPayload.Add(0); // External = 0
    headerPayload.AddRange(nameBytes);

    headerPayload.Add(SevenZipNid.End); // End of FilesInfo

    headerPayload.Add(SevenZipNid.End); // End of Header

    byte[] nextHeader = [.. headerPayload];
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    // ----- SignatureHeader -----
    // next header сразу после signature header
    ulong nextHeaderOffset = (ulong)packedStream.Length;
    ulong nextHeaderSize = (ulong)nextHeader.Length;

    SevenZipSignatureHeader sig = new(
        NextHeaderOffset: nextHeaderOffset,
        NextHeaderSize: nextHeaderSize,
        NextHeaderCrc: nextHeaderCrc);

    byte[] sigBytes = new byte[SevenZipSignatureHeader.TotalSize];
    sig.Write(sigBytes);

    // ----- Archive = [SignatureHeader][PackedStreams][NextHeader] -----
    var archive = new byte[sigBytes.Length + packedStream.Length + nextHeader.Length];
    sigBytes.CopyTo(archive, 0);
    packedStream.CopyTo(archive.AsSpan(sigBytes.Length));
    nextHeader.CopyTo(archive, sigBytes.Length + packedStream.Length);

    return archive;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }

  private static byte[] Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(
      ReadOnlySpan<byte> plainFileBytes,
      string fileName,
      int dictionarySize)
  {
    // Упакованные данные (LZMA2 stream с COPY-чанками).
    byte[] packedHeader = Lzma2CopyEncoder.Encode(plainFileBytes, dictionarySize, out byte lzma2PropertiesByte);

    // ----- NextHeader ("Header") -----
    // 7z Header = [Header][MainStreamsInfo][FilesInfo][End]
    List<byte> headerPayload =
    [
      SevenZipNid.Header,
      // MainStreamsInfo
      SevenZipNid.MainStreamsInfo,
      // StreamsInfo = [PackInfo][UnpackInfo][SubStreamsInfo][End]
      SevenZipNid.PackInfo,
    ];
    // PackPos
    WriteU64(headerPayload, 0);
    // NumPackStreams
    WriteU64(headerPayload, 1);
    // Size
    headerPayload.Add(SevenZipNid.Size);
    WriteU64(headerPayload, (ulong)packedHeader.Length);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.UnpackInfo);
    // Folder
    headerPayload.Add(SevenZipNid.Folder);
    WriteU64(headerPayload, 1); // NumFolders
    headerPayload.Add(0); // External = 0

    // Folder описывает цепочку кодеров.
    // NumCoders
    WriteU64(headerPayload, 1);

    // Coder info:
    //   MainByte:
    //     - IDSize = 1 (младшие 4 бита: 1 => 1 байт; 0 считается ошибкой)
    //     - IsSimple = 1 (один входной и один выходной поток)
    //     - HasProperties = 1 (есть свойства кодера)
    //     - HasAttributes = 0
    //     - MoreAlternativeMethods = 0
    //
    // Важно: в 7z флаг "simple" кодируется как (MainByte & 0x10) == 0.
    headerPayload.Add(0b0010_0001); // MainByte: idSize=1 (младшие 4 бита), hasProps=1
                                    // MethodID (LZMA2 = 0x21)
    headerPayload.Add(0x21);
    // Properties
    headerPayload.Add(1); // properties size
    headerPayload.Add(lzma2PropertiesByte);

    // BindPairs и PackedStreamIndices можно не писать:
    //  - при одном выходном потоке NumBindPairs == 0,
    //  - при NumPackedStreams == 1 индекс подразумевается равным 0.

    headerPayload.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(headerPayload, (ulong)plainFileBytes.Length);

    headerPayload.Add(SevenZipNid.End); // End of UnpackInfo

    // SubStreamsInfo
    headerPayload.Add(SevenZipNid.SubStreamsInfo);
    headerPayload.Add(SevenZipNid.NumUnpackStream);
    WriteU64(headerPayload, 1);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.End); // End of MainStreamsInfo

    // FilesInfo
    headerPayload.Add(SevenZipNid.FilesInfo);
    WriteU64(headerPayload, 1); // NumFiles

    // Name property.
    headerPayload.Add(SevenZipNid.Name);

    // Данные свойства "Name" идут как:
    //   Size (u64)
    //   External (byte)
    //   UTF-16LE null-terminated strings
    // Здесь: один файл.
    var nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(headerPayload, (ulong)(1 + nameBytes.Length));
    headerPayload.Add(0); // External = 0
    headerPayload.AddRange(nameBytes);

    headerPayload.Add(SevenZipNid.End); // End of FilesInfo

    headerPayload.Add(SevenZipNid.End); // End of Header

    byte[] nextHeader = [.. headerPayload];
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    // ----- SignatureHeader -----
    // next header сразу после signature header
    ulong nextHeaderOffset = (ulong)packedHeader.Length;
    ulong nextHeaderSize = (ulong)nextHeader.Length;

    SevenZipSignatureHeader sig = new(
        NextHeaderOffset: nextHeaderOffset,
        NextHeaderSize: nextHeaderSize,
        NextHeaderCrc: nextHeaderCrc);

    byte[] sigBytes = new byte[SevenZipSignatureHeader.TotalSize];
    sig.Write(sigBytes);

    // ----- Archive = [SignatureHeader][PackedStreams][NextHeader] -----
    var archive = new byte[sigBytes.Length + packedHeader.Length + nextHeader.Length];
    sigBytes.CopyTo(archive, 0);
    packedHeader.CopyTo(archive, sigBytes.Length);
    nextHeader.CopyTo(archive, sigBytes.Length + packedHeader.Length);

    return archive;
  }

  // ----- RES-002: limit on in-memory folder unpack (C4) -----

  private static SevenZipCoderInfo CreateLzma1CoderForRed()
  {
    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    const int dictionarySize = 1 << 20;

    // Raw LZMA stream (без LZMA-Alone header), маленький, но достаточный для старта декода.
    byte[] packed = new LzmaEncoder(lzmaProps, dictionarySize).EncodeLiteralOnly(new byte[256]);

    byte[] coderProps = new byte[5];
    coderProps[0] = lzmaProps.ToByteOrThrow();
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(coderProps.AsSpan(1, 4), (uint)dictionarySize);

    return new SevenZipCoderInfo(
      methodId: [0x03, 0x01, 0x01], // LZMA
      properties: coderProps,
      numInStreams: 1,
      numOutStreams: 1);
  }

  private static SevenZipStreamsInfo CreateSingleCoderStreamsInfo(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> packed,
      ulong declaredUnpackSize)
  {
    var packInfo = new SevenZipPackInfo(
      packPos: 0,
      packSizes: [(ulong)packed.Length]);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[declaredUnpackSize]]);

    return new SevenZipStreamsInfo(
      packInfo: packInfo,
      unpackInfo: unpackInfo,
      subStreamsInfo: null);
  }

  [Fact]
  public void C4_RED_DeclaredUnpack300MiB_SeveralPackedBytes_ReturnsNotSupportedWithoutAllocating()
  {
    // Главный RED: заявленный unpack 300 МиБ (> FLOOR 256 МиБ, < int.MaxValue), packed — несколько байт.
    // До правки декодер выделяет new byte[300 МиБ] → тест падает (дельта аллокаций велика, либо
    // результат не NotSupported). После правки: NotSupported и дельта аллокаций < 1 МиБ.
    const long declaredUnpack = 300L << 20;

    SevenZipCoderInfo coder = CreateLzma1CoderForRed();
    byte[] packedStreams = [0x01, 0x02, 0x03, 0x04, 0x05];
    SevenZipStreamsInfo streamsInfo = CreateSingleCoderStreamsInfo(coder, packedStreams, (ulong)declaredUnpack);

    long before = GC.GetAllocatedBytesForCurrentThread();
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo, packedStreams, 0, SevenZipDecodeOptions.Default, out byte[] _);
    long after = GC.GetAllocatedBytesForCurrentThread();
    long delta = after - before;

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.True(
      delta < 1_048_576,
      $"дельта аллокаций {delta} байт — ожидается меньше 1 МиБ после правки");
  }

  private static SevenZipCoderInfo CreateCopyCoder()
  {
    return new SevenZipCoderInfo(
      methodId: [0x00], // Copy
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);
  }

  // Тесты самой политики (чистая функция, без ФС и потоков).
  [Fact]
  public void C4_Policy_ZeroPacked_ReturnsFloor()
  {
    Assert.Equal(SevenZipMemoryPolicy.FloorBytes, SevenZipMemoryPolicy.GetMaxUnpackBytes(0));
  }

  [Fact]
  public void C4_Policy_NegativePacked_TreatedAsZero_ReturnsFloor()
  {
    Assert.Equal(SevenZipMemoryPolicy.FloorBytes, SevenZipMemoryPolicy.GetMaxUnpackBytes(-1024));
  }

  [Fact]
  public void C4_Policy_AtCapDividedAmplification_ReturnsCap()
  {
    long packedLength = SevenZipMemoryPolicy.CapBytes / SevenZipMemoryPolicy.Amplification;
    Assert.Equal(SevenZipMemoryPolicy.CapBytes, SevenZipMemoryPolicy.GetMaxUnpackBytes(packedLength));
  }

  [Fact]
  public void C4_Policy_LongMaxValue_SaturatesToCapWithoutOverflow()
  {
    Assert.Equal(SevenZipMemoryPolicy.CapBytes, SevenZipMemoryPolicy.GetMaxUnpackBytes(long.MaxValue));
  }

  [Fact]
  public void C4_Policy_FloorBoundary_GetMaxUnpackBytesOne_ReturnsFloor()
  {
    Assert.Equal(SevenZipMemoryPolicy.FloorBytes, SevenZipMemoryPolicy.GetMaxUnpackBytes(1));
  }

  [Fact]
  public void C4_Policy_Monotonic_OnSamplePoints()
  {
    long a = SevenZipMemoryPolicy.GetMaxUnpackBytes(0);
    long[] points = [1L, 100L, 1000L, 4096L, 1 << 20, 3 << 20, 100 << 20,
      SevenZipMemoryPolicy.CapBytes / SevenZipMemoryPolicy.Amplification,
      SevenZipMemoryPolicy.CapBytes / SevenZipMemoryPolicy.Amplification + 1,
      long.MaxValue];
    foreach (long p in points)
    {
      long b = SevenZipMemoryPolicy.GetMaxUnpackBytes(p);
      Assert.True(b >= a, $"non-decreasing at {p}: {a} -> {b}");
      Assert.True(b <= SevenZipMemoryPolicy.CapBytes, $"within cap at {p}: {b}");
      a = b;
    }
  }

  // Граница: ровно на пределе. Пакет такой длины, что предел ровно равен заявленному размеру →
  // предел НЕ срабатывает (результат определяется данными, а не лимитом: Copy-кодер даёт InvalidData).
  [Fact]
  public void C4_Boundary_ExactlyAtLimit_IsNotRejected()
  {
    long packedLength = 5000L;
    long limit = SevenZipMemoryPolicy.GetMaxUnpackBytes(packedLength);

    byte[] packedStreams = new byte[packedLength];
    for (long i = 0; i < packedLength; i++)
      packedStreams[i] = (byte)i;

    SevenZipStreamsInfo streamsInfo = CreateSingleCoderStreamsInfo(
        CreateCopyCoder(), packedStreams, (ulong)limit);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo, packedStreams, 0, SevenZipDecodeOptions.Default, out byte[] _);

    Assert.NotEqual(SevenZipFolderDecodeResult.NotSupported, result);
  }

  // Граница: на один байт больше лимита → NotSupported.
  [Fact]
  public void C4_Boundary_OneByteOverLimit_IsRejected()
  {
    long packedLength = 5000L;
    long limit = SevenZipMemoryPolicy.GetMaxUnpackBytes(packedLength);

    byte[] packedStreams = new byte[packedLength];

    SevenZipStreamsInfo streamsInfo = CreateSingleCoderStreamsInfo(
        CreateCopyCoder(), packedStreams, (ulong)(limit + 1));

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo, packedStreams, 0, SevenZipDecodeOptions.Default, out byte[] _);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
  }

  // Пол (FLOOR). Пакет 1 байт, заявленный размер ровно 256 МиБ → предел не срабатывает;
  // 256 МиБ + 1 байт → NotSupported.
  [Fact]
  public void C4_Floor_256MiBAtFloorWithOnePackedByte_IsNotRejected()
  {
    const long declaredUnpack = 256L << 20; // == FloorBytes
    Assert.Equal(SevenZipMemoryPolicy.FloorBytes, SevenZipMemoryPolicy.GetMaxUnpackBytes(1));

    byte[] packedStreams = [0x42];
    SevenZipStreamsInfo streamsInfo = CreateSingleCoderStreamsInfo(
        CreateCopyCoder(), packedStreams, (ulong)declaredUnpack);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo, packedStreams, 0, SevenZipDecodeOptions.Default, out byte[] _);

    Assert.NotEqual(SevenZipFolderDecodeResult.NotSupported, result);
  }

  [Fact]
  public void C4_Floor_256MiBPlusOneByte_IsRejected()
  {
    const long declaredUnpack = (256L << 20) + 1; // > FloorBytes

    byte[] packedStreams = [0x42];
    SevenZipStreamsInfo streamsInfo = CreateSingleCoderStreamsInfo(
        CreateCopyCoder(), packedStreams, (ulong)declaredUnpack);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo, packedStreams, 0, SevenZipDecodeOptions.Default, out byte[] _);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
  }
}
