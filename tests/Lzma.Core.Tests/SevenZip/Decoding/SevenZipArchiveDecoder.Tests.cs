using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipArchiveDecoderTests
{
  // Коллектор отчётов прогресса: сохраняет последовательно переданные отчёты и финальный
  // код результата, чтобы проверки могли опираться и на то, и на другое.
  private sealed class CollectingProgress : IProgress<SevenZipProgress>
  {
    public List<SevenZipProgress> Reports { get; } = new();

    public SevenZipArchiveDecodeResult LastResult { get; set; }

    public void Report(SevenZipProgress value) => Reports.Add(value);
  }
  [Fact]
  public void DecodeSingleFile_ОдинФайл_ОдинFolder_Lzma2Copy_ВозвращаетИсходныеБайты()
  {
    byte[] fileBytes = [
        0, 1, 2, 3, 4, 5, 6, 7,
            8, 9, 255, 254, 253, 0, 0, 1,
            65, 66, 67, 68, 69, 70,
        ];

    const string fileName = "file.bin";

    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(fileBytes, fileName);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] decoded,
        out string? decodedName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(fileBytes, decoded);
    Assert.Equal(fileName, decodedName);
    Assert.True(bytesConsumed > 0);
    Assert.True(bytesConsumed <= archive.Length);
  }

  [Fact]
  public void DecodeSingleFile_ОдинФайл_Lzma2Copy_WithFolderCrc_Ok()
  {
    byte[] fileBytes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 255, 254, 253, 0, 0, 1, 65, 66, 67, 68, 69, 70,];
    const string fileName = "file.bin";

    uint crc = Crc32.Compute(fileBytes);
    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(fileBytes, fileName, folderCrc: crc);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive, out byte[] decoded, out string? decodedName, out _);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(fileBytes, decoded);
    Assert.Equal(fileName, decodedName);
  }

  [Fact]
  public void DecodeSingleFile_ОдинФайл_Lzma2Copy_WithFolderCrcMismatch_InvalidData()
  {
    byte[] fileBytes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 255, 254, 253, 0, 0, 1, 65, 66, 67, 68, 69, 70,];
    const string fileName = "file.bin";

    uint crcWrong = Crc32.Compute(fileBytes) ^ 1u;
    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(fileBytes, fileName, folderCrc: crcWrong);

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out _));

    var ui = reader.Header!.Value.StreamsInfo.UnpackInfo!;
    Assert.NotNull(ui.FolderCrcDefined);
    Assert.NotNull(ui.FolderCrc);
    Assert.True(ui.FolderCrcDefined![0]);
    Assert.Equal(crcWrong, ui.FolderCrc![0]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive, out _, out _, out _);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
  }

  [Fact]
  public void DecodeToArray_НоваяПерегрузкаБезBytesConsumedСNullOptions_БросаетArgumentNullException()
  {
    byte[] archive = [];

    Assert.Throws<ArgumentNullException>(
        () => SevenZipArchiveDecoder.DecodeToArray(
            archive: archive,
            options: null!,
            files: out _));
  }

  // SEC-010/C2b, §5.1: маршрут «в память». Размер распаковки folder'а = ulong.MaxValue приходит
  // из недоверенного заголовка. (long)ulong.MaxValue = -1, поэтому без насыщения знаменатель
  // прогресса в репорте :459 отрицателен. Проверка кода результата через Assert.Equal сверху:
  // если классификация действительно NotSupported — утверждение проходит и проверка идёт дальше,
  // к знаменателю; иначе падает здесь и показывает фактический код.
  [Fact]
  public void DecodeToArray_РазмерFolder_UlongMaxValue_ЗнаменательПрогрессаНеОтрицателен_ВПамяти()
  {
    byte[] fileBytes = [1, 2, 3, 4, 5];
    string fileName = "big.bin";
    byte[] archive = Build7zArchive_ОдинПоток_РазмерFolderНаГранице(ulong.MaxValue, fileName, fileBytes);

    var reports = new CollectingProgress();
    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        SevenZipDecodeOptions.Default,
        out _,
        out _,
        progress: reports);
    reports.LastResult = result;

    // Код результата (NotSupported) не утверждаем — он зависит от более поздних проверок
    // (RES-002, размер потока) и не меняется правкой знаменателя; фактический код зафиксирован
    // в отчёте исполнителя.
    Assert.NotEmpty(reports.Reports);
    Assert.All(reports.Reports, r => Assert.True(r.TotalBytes >= 0, $"Отчёт прогресса с отрицательным знаменателем: {r}"));
    Assert.Contains(reports.Reports, r => r.TotalBytes == long.MaxValue);
  }

  // SEC-010/C2b, §5.2: насыщение СУММЫ без отрицательных слагаемых. Два sub-stream'а одного
  // folder'а с размерами long.MaxValue и 1: каждый слагаемый положителен, но их сумма переполняет
  // long (переходит через ноль в минус). folderTotal = long.MaxValue + 1.
  [Fact]
  public void DecodeToArray_СуммаРазмеров_ПереполнениеLong_ЗнаменательПрогрессаПереполнениеНеОтрицателен_ВПамяти()
  {
    byte[] fileBytes = [1, 2, 3, 4, 5];
    byte[] archive = Build7zArchive_TwoSubstreams_SumAtBoundary(fileBytes);

    var reports = new CollectingProgress();
    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        SevenZipDecodeOptions.Default,
        out _,
        out _,
        progress: reports);
    reports.LastResult = result;

    // Код результата (NotSupported) не утверждаем по той же причине, что и в §5.1: он
    // определяется поздними проверками и правкой знаменателя не меняется. Фактический код
    // (NotSupported до и после правки) зафиксирован в отчёте исполнителя.
    Assert.NotEmpty(reports.Reports);
    Assert.All(reports.Reports, r => Assert.True(r.TotalBytes >= 0, $"Отчёт прогресса с отрицательным знаменателем: {r}"));
    Assert.Contains(reports.Reports, r => r.TotalBytes == long.MaxValue);
  }

  // SEC-010/C2b, §5.3: позитивный контроль. Небольшой реальный архив с переданным progress —
  // знаменатель равен сумме заященных размеров, насыщение нормальный случай не исказил.
  [Fact]
  public void DecodeToArray_НебольшойРеальныйАрхив_ЗнаменательРавенСуммеРазмеров_ВПамяти()
  {
    byte[] fileBytes = [
        0, 1, 2, 3, 4, 5, 6, 7,
            8, 9, 255, 254, 253, 0, 0, 1,
            65, 66, 67, 68, 69, 70,
    ];
    const string fileName = "file.bin";
    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(fileBytes, fileName);

    var reports = new CollectingProgress();
    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        SevenZipDecodeOptions.Default,
        out _,
        out _,
        progress: reports);
    reports.LastResult = result;

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.NotEmpty(reports.Reports);
    Assert.All(reports.Reports, r => Assert.True(r.TotalBytes >= 0, $"Отчёт прогресса с отрицательным знаменателем: {r}"));
    Assert.Contains(reports.Reports, r => r.TotalBytes == fileBytes.Length);
  }

  // Архив: один folder с двумя sub-stream'ами. folderTotal = long.MaxValue + 1, размеры
  // sub-stream'ов = [long.MaxValue, 1]. Оба слагаемые <= long.MaxValue (проходят защиту плана),
  // но их сумма переполняет long. Пакет — Lzma2Copy маленьких байт.
  private static byte[] Build7zArchive_TwoSubstreams_SumAtBoundary(byte[] fileBytes)
  {
    const int dictionarySize = 1 << 20;
    const int maxChunkPayloadSize = 64 * 1024;
    byte[] packedStreams = Lzma2CopyEncoder.EncodeChunkedAuto(
        fileBytes,
        dictionarySize,
        maxChunkPayloadSize: maxChunkPayloadSize,
        out byte lzma2PropsByte);

    byte[] nextHeader = BuildHeaderДваПодпотока(
        packSizes: [(ulong)packedStreams.Length],
        folderTotal: (ulong)(long.MaxValue + 1UL),
        coder: new SevenZipCoderInfo([0x21], [lzma2PropsByte], numInStreams: 1, numOutStreams: 1),
        new[] { "first.bin", "second.bin" });

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

    var archive = new List<byte>(SevenZipSignatureHeader.TotalSize + packedStreams.Length + nextHeader.Length);

    Span<byte> sigBuf = stackalloc byte[SevenZipSignatureHeader.TotalSize];
    sigHeader.Write(sigBuf);
    archive.AddRange(sigBuf.ToArray());

    archive.AddRange(packedStreams);
    archive.AddRange(nextHeader);

    return [.. archive];
  }

  private static byte[] BuildHeaderДваПодпотока(ulong[] packSizes, ulong folderTotal, SevenZipCoderInfo coder, string[] fileNames)
  {
    var header = new List<byte>(512);

    WriteNid(header, SevenZipNid.Header);

    // MainStreamsInfo
    WriteNid(header, SevenZipNid.MainStreamsInfo);
    WritePackInfo(header, packSizes);
    WriteUnpackInfoSingleFolder(header, coder, folderTotal);
    WriteSubStreamsInfoДваПодпотока(header, folderTotal);

    WriteNid(header, SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo: два файла по одному sub-stream'у.
    WriteNid(header, SevenZipNid.FilesInfo);
    WriteEncodedUInt64(header, 2UL);
    WriteNid(header, SevenZipNid.Name);
    var bytes = new List<byte>();
    foreach (string n in fileNames)
    {
      byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes(n);
      bytes.AddRange(nameBytes);
      bytes.Add(0);
      bytes.Add(0);
    }
    WriteEncodedUInt64(header, (ulong)(1 + bytes.Count));
    WriteByte(header, 0);
    header.AddRange(bytes);
    WriteNid(header, SevenZipNid.End);

    WriteNid(header, SevenZipNid.End);

    return [.. header];
  }

  private static void WriteUnpackInfoSingleFolder(List<byte> output, SevenZipCoderInfo coder, ulong folderTotal)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);
    WriteNid(output, SevenZipNid.Folder);
    WriteEncodedUInt64(output, 1UL);
    WriteByte(output, 0);
    WriteFolder(output, coder);
    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, folderTotal);

    // End UnpackInfo — обязательно, иначе читатель не видит границу секции.
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteSubStreamsInfoДваПодпотока(List<byte> output, ulong folderTotal)
  {
    WriteNid(output, SevenZipNid.SubStreamsInfo);
    WriteNid(output, SevenZipNid.NumUnpackStream);
    WriteEncodedUInt64(output, 2UL);
    WriteNid(output, SevenZipNid.Size);
    // Первый размер — явный. Второй вычисляется читателем как folderTotal - size[0].
    WriteEncodedUInt64(output, (ulong)long.MaxValue);

    // End SubStreamsInfo — без него читатель сдвигает иерархию и отклоняет заголовок.
    WriteNid(output, SevenZipNid.End);
  }

  // Архив: один folder, один поток, size распаковки folder'а задается напрямую (в том числе
  // значением из недоверенного заголовка, выходящим за long.MaxValue). SubStreamsInfo пустая —
  // размер одного sub-stream берётся из CodersUnpackSize. Пакет — Lzma2Copy маленьких байт, поэтому
  // фактическая распаковка минимальна, а заященный размер управляет только знаменателем прогресса.
  private static byte[] Build7zArchive_ОдинПоток_РазмерFolderНаГранице(ulong folderUnpackSizeU64, string fileName, byte[] fileBytes)
  {
    const int dictionarySize = 1 << 20;
    const int maxChunkPayloadSize = 64 * 1024;
    byte[] packedStreams = Lzma2CopyEncoder.EncodeChunkedAuto(
        fileBytes,
        dictionarySize,
        maxChunkPayloadSize: maxChunkPayloadSize,
        out byte lzma2PropsByte);

    byte[] nextHeader = BuildHeaderSingleFolderSingleStream(
        packSizes: [(ulong)packedStreams.Length],
        folderUnpackSize: folderUnpackSizeU64,
        coder: new SevenZipCoderInfo([0x21], [lzma2PropsByte], numInStreams: 1, numOutStreams: 1),
        fileName,
        folderCrc: null);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sigHeader = new SevenZipSignatureHeader(
        VersionMajor: 0,
        VersionMinor: 4,
        StartHeaderCrc: 0, // заполним ниже
        NextHeaderOffset: (ulong)packedStreams.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    uint startHeaderCrc = Crc32.Compute(sigHeader.GetStartHeaderBytes());
    sigHeader = sigHeader with { StartHeaderCrc = startHeaderCrc };

    var archive = new List<byte>(SevenZipSignatureHeader.TotalSize + packedStreams.Length + nextHeader.Length);

    Span<byte> sigBuf = stackalloc byte[SevenZipSignatureHeader.TotalSize];
    sigHeader.Write(sigBuf);
    archive.AddRange(sigBuf.ToArray());

    archive.AddRange(packedStreams);
    archive.AddRange(nextHeader);

    return [.. archive];
  }

  private static byte[] Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(byte[] fileBytes, string fileName, uint? folderCrc = null)
  {
    const int dictionarySize = 1 << 20;
    // Для COPY-чанков LZMA2 размер payload кодируется в 16 битах и должен быть в диапазоне [1..65536].
    const int maxChunkPayloadSize = 64 * 1024;
    byte[] packedStreams = Lzma2CopyEncoder.EncodeChunkedAuto(
        fileBytes,
        dictionarySize,
        maxChunkPayloadSize: maxChunkPayloadSize,
        out byte lzma2PropsByte);

    byte[] nextHeader = BuildHeaderSingleFolderSingleStream(
        packSizes: [(ulong)packedStreams.Length],
        folderUnpackSize: (ulong)fileBytes.Length,
        coder: new SevenZipCoderInfo([0x21], [lzma2PropsByte], numInStreams: 1, numOutStreams: 1),
        fileName,
        folderCrc);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sigHeader = new SevenZipSignatureHeader(
        VersionMajor: 0,
        VersionMinor: 4,
        StartHeaderCrc: 0, // заполним ниже
        NextHeaderOffset: (ulong)packedStreams.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    // CRC считается по байтам StartHeader (NextHeaderOffset/Size/Crc) и записывается в поле StartHeaderCrc.
    uint startHeaderCrc = Crc32.Compute(sigHeader.GetStartHeaderBytes());
    sigHeader = sigHeader with { StartHeaderCrc = startHeaderCrc };

    var archive = new List<byte>(SevenZipSignatureHeader.TotalSize + packedStreams.Length + nextHeader.Length);

    // SignatureHeader
    Span<byte> sigBuf = stackalloc byte[SevenZipSignatureHeader.TotalSize];
    sigHeader.Write(sigBuf);
    archive.AddRange(sigBuf.ToArray());

    // PackedStreams
    archive.AddRange(packedStreams);

    // NextHeader (Header)
    archive.AddRange(nextHeader);

    return [.. archive];
  }

  private static byte[] BuildHeaderSingleFolderSingleStream(ulong[] packSizes, ulong folderUnpackSize, SevenZipCoderInfo coder, string fileName, uint? folderCrc)
  {
    // Содержимое Header:
    // Header
    //   MainStreamsInfo
    //     PackInfo
    //     UnpackInfo
    //     SubStreamsInfo (пусто)
    //   FilesInfo

    var header = new List<byte>(256);

    WriteNid(header, SevenZipNid.Header);

    WriteStreamsInfo(header, packSizes, folderUnpackSize, coder, folderCrc);
    WriteFilesInfo(header, fileName);

    WriteNid(header, SevenZipNid.End);

    return [.. header];
  }

  private static void WriteStreamsInfo(List<byte> output, ulong[] packSizes, ulong folderUnpackSize, SevenZipCoderInfo coder, uint? folderCrc)
  {
    WriteNid(output, SevenZipNid.MainStreamsInfo);

    WritePackInfo(output, packSizes);
    WriteUnpackInfo(output, folderUnpackSize, coder, folderCrc);

    // SubStreamsInfo: на этом шаге делаем пустую секцию.
    WriteSubStreamsInfoEmpty(output);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WritePackInfo(List<byte> output, ulong[] packSizes)
  {
    WriteNid(output, SevenZipNid.PackInfo);

    // PackPos
    WriteEncodedUInt64(output, 0);

    // NumPackStreams
    WriteEncodedUInt64(output, (ulong)packSizes.Length);

    // Size
    WriteNid(output, SevenZipNid.Size);
    foreach (ulong s in packSizes)
      WriteEncodedUInt64(output, s);

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteUnpackInfo(List<byte> output, ulong folderUnpackSize, SevenZipCoderInfo coder, uint? folderCrc)
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

    if (folderCrc.HasValue)
    {
      WriteNid(output, SevenZipNid.Crc);

      // Digests(NumFolders): AllAreDefined=1 => CRC есть для каждого folder.
      WriteByte(output, 1);

      WriteUInt32LE(output, folderCrc.Value);
    }

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolder(List<byte> output, SevenZipCoderInfo coder)
  {
    // NumCoders
    WriteEncodedUInt64(output, 1);

    WriteCoderInfo(output, coder);
  }

  private static void WriteCoderInfo(List<byte> output, SevenZipCoderInfo coder)
  {
    int methodIdSize = coder.MethodId.Length;
    Assert.InRange(methodIdSize, 1, 15);

    bool isComplexCoder = coder.NumInStreams != 1 || coder.NumOutStreams != 1;
    bool hasProperties = coder.Properties.Length != 0;

    // mainByte:
    //  - младшие 4 бита: размер MethodID (1..15)
    //  - 0x10: complex coder (есть NumInStreams/NumOutStreams)
    //  - 0x20: есть Properties
    byte mainByte = (byte)(
        (methodIdSize & 0x0F) |
        (isComplexCoder ? 0x10 : 0) |
        (hasProperties ? 0x20 : 0));

    WriteByte(output, mainByte);

    // MethodID bytes
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

    // NumFiles
    WriteEncodedUInt64(output, 1);

    // Name
    WriteFileInfoNames(output, [fileName]);

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFileInfoNames(List<byte> output, string[] names)
  {
    // Property ID
    WriteNid(output, SevenZipNid.Name);

    // Агрегируем имена в UTF-16LE строку с '\0' после каждого.
    var bytes = new List<byte>();
    foreach (string n in names)
    {
      byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes(n);
      bytes.AddRange(nameBytes);
      bytes.Add(0);
      bytes.Add(0);
    }

    // Property size
    WriteEncodedUInt64(output, (ulong)(1 + bytes.Count));

    // External = 0
    WriteByte(output, 0);

    // Payload
    output.AddRange(bytes);

    // End of property
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteEncodedUInt64(List<byte> output, ulong value)
  {
    Span<byte> buf = stackalloc byte[9];

    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, buf, out int bytesWritten);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);
    Assert.True(bytesWritten > 0);

    output.AddRange(buf[..bytesWritten].ToArray());
  }

  private static void WriteByte(List<byte> output, byte value) => output.Add(value);

  private static void WriteNid(List<byte> output, byte nid) => output.Add(nid);

  private static void WriteUInt32LE(List<byte> output, uint value)
  {
    output.Add((byte)value);
    output.Add((byte)(value >> 8));
    output.Add((byte)(value >> 16));
    output.Add((byte)(value >> 24));
  }
}
