using System.Buffers.Binary;

using Lzma.Core.BZip2;
using Lzma.Core.Deflate;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.Ppmd;

using static Lzma.Core.SevenZip.SevenZipCoderMethodIds;

namespace Lzma.Core.SevenZip;

public enum SevenZipFolderDecodeResult
{
  Ok,
  InvalidData,
  NotSupported,
}

/// <summary>
/// Декодирование данных папки (Folder) из 7z.
/// </summary>
/// <remarks>
/// Поддерживаются два основных пути:
/// <list type="bullet">
/// <item>
/// <description>специальная ветка BCJ2 для multi-stream folder-ов;</description>
/// </item>
/// <item>
/// <description>
/// линейный конвейер из одного packed stream и одного или нескольких coder-ов
/// с формой 1 вход / 1 выход.
/// </description>
/// </item>
/// </list>
/// В линейном конвейере поддерживаются обычные фильтры и распаковщики,
/// а также decoder-path для AES и экспериментальных GOST coder-ов LzmaSharp.
/// </remarks>
public static class SevenZipFolderDecoder
{
  private const byte _methodIdCopy = 0x00;
  private const byte _methodIdLzma2 = 0x21;
  private const byte _methodIdDelta = 0x03;

  /// <summary>
  /// Декодирует Folder в массив байт с настройками по умолчанию.
  /// </summary>
  public static SevenZipFolderDecodeResult DecodeFolderToArray(
      SevenZipStreamsInfo streamsInfo,
      ReadOnlySpan<byte> packedStreams,
      int folderIndex,
      out byte[] output)
  {
    return DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: folderIndex,
        options: SevenZipDecodeOptions.Default,
        output: out output);
  }

  /// <summary>
  /// Декодирует Folder напрямую в <see cref="System.IO.Stream"/>, не накапливая весь выход в
  /// <c>byte[]</c> (для больших файлов). Для простого folder-а из одного LZMA2-coder-а —
  /// потоковый декод; для остальных форм (цепочки, BCJ2, PPMd, Copy, AES/GOST) — fallback:
  /// декод в массив + запись в поток. <paramref name="bytesWritten"/> — число записанных байт.
  /// </summary>
  public static SevenZipFolderDecodeResult DecodeFolderToStream(
      SevenZipStreamsInfo streamsInfo,
      ReadOnlySpan<byte> packedStreams,
      int folderIndex,
      SevenZipDecodeOptions options,
      System.IO.Stream output,
      out long bytesWritten,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    bytesWritten = 0;

    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(streamsInfo);
    ArgumentNullException.ThrowIfNull(output);

    // Быстрый потоковый путь: folder — ровно один LZMA2-coder над одним packed stream.
    if (TryGetSingleLzma2Coder(streamsInfo, packedStreams, folderIndex,
            out ReadOnlySpan<byte> packStream, out byte lzma2PropertiesByte))
    {
      Lzma2DecodeResult r = Lzma2Decoder.DecodeToStream(
          packStream, lzma2PropertiesByte, output, out bytesWritten, out _, progress, token);

      return r switch
      {
        Lzma2DecodeResult.Finished => SevenZipFolderDecodeResult.Ok,
        Lzma2DecodeResult.NotSupported => SevenZipFolderDecodeResult.NotSupported,
        _ => SevenZipFolderDecodeResult.InvalidData,
      };
    }

    // Fallback — прочие формы folder-а: декодируем в массив и пишем в поток целиком.
    SevenZipFolderDecodeResult arrayResult = DecodeFolderToArray(
        streamsInfo, packedStreams, folderIndex, options, out byte[] decoded, progress, token);

    if (arrayResult == SevenZipFolderDecodeResult.Ok)
    {
      output.Write(decoded, 0, decoded.Length);
      bytesWritten = decoded.LongLength;
    }

    return arrayResult;
  }

  // Распознаёт простейший folder: ровно один LZMA2-coder (1 in / 1 out) над одним packed stream.
  // При успехе отдаёт срез packed-данных и байт свойств LZMA2 (размер словаря).
  private static bool TryGetSingleLzma2Coder(
      SevenZipStreamsInfo streamsInfo,
      ReadOnlySpan<byte> packedStreams,
      int folderIndex,
      out ReadOnlySpan<byte> packStream,
      out byte lzma2PropertiesByte)
  {
    packStream = default;
    lzma2PropertiesByte = 0;

    if (streamsInfo.PackInfo is not { } packInfo)
      return false;
    if (streamsInfo.UnpackInfo is not { } unpackInfo)
      return false;
    if ((uint)folderIndex >= (uint)unpackInfo.Folders.Length)
      return false;

    SevenZipFolder folder = unpackInfo.Folders[folderIndex];

    if (folder.Coders.Length != 1 || folder.PackedStreamIndices.Length != 1)
      return false;

    SevenZipCoderInfo coder = folder.Coders[0];

    if (!IsSingleByteMethodId(coder.MethodId, _methodIdLzma2))
      return false;
    if (coder.NumInStreams != 1 || coder.NumOutStreams != 1)
      return false;
    if (coder.Properties is null || coder.Properties.Length != 1)
      return false;

    ulong packStreamIndexU64 = 0;
    for (int i = 0; i < folderIndex; i++)
      packStreamIndexU64 += (ulong)unpackInfo.Folders[i].PackedStreamIndices.Length;

    if (packStreamIndexU64 > int.MaxValue)
      return false;

    uint packStreamIndex = (uint)packStreamIndexU64;
    if (packStreamIndex >= (uint)packInfo.PackSizes.Length)
      return false;

    if (!TryGetPackStream(packInfo, packedStreams, packStreamIndex, out packStream))
      return false;

    lzma2PropertiesByte = coder.Properties[0];
    return true;
  }

  /// <summary>
  /// Декодирует folder ПОТОКОВО из архива-<see cref="System.IO.Stream"/> (по смещению/размеру из
  /// метаданных), не загружая packed-данные в память — для извлечения архивов больше 2 ГиБ. Пишет
  /// выход в <paramref name="output"/>. <paramref name="packedBaseOffset"/> — начало packed-региона
  /// в файле (обычно 32). Одиночный LZMA2-folder декодируется чисто потоково (для файлов &gt; 2 ГиБ);
  /// прочие формы (PPMd/BCJ2/Copy/цепочки — их производит потоковый Auto) декодируются фолбэком:
  /// packed этого folder-а читается в память (folder = один файл, размер ограничен) и обрабатывается
  /// полноценным ин-мемори декодером.
  /// </summary>
  public static SevenZipFolderDecodeResult DecodeFolderStreamToStream(
      SevenZipStreamsInfo streamsInfo,
      System.IO.Stream archive,
      long packedBaseOffset,
      int folderIndex,
      SevenZipDecodeOptions options,
      System.IO.Stream output,
      out long bytesWritten,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    bytesWritten = 0;

    ArgumentNullException.ThrowIfNull(streamsInfo);
    ArgumentNullException.ThrowIfNull(archive);
    ArgumentNullException.ThrowIfNull(output);
    ArgumentNullException.ThrowIfNull(options);

    // Быстрый путь: одиночный LZMA2-folder — чистый потоковый декод (для больших файлов).
    if (TryGetSingleLzma2CoderLocation(streamsInfo, folderIndex,
            out ulong packStart, out ulong packSize, out byte lzma2PropertiesByte))
    {
      if (packSize > long.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      long fileOffset = packedBaseOffset + (long)packStart;
      if (fileOffset < 0 || fileOffset + (long)packSize > archive.Length)
        return SevenZipFolderDecodeResult.InvalidData;

      if (!Lzma2Properties.TryParse(lzma2PropertiesByte, out Lzma2Properties properties))
        return SevenZipFolderDecodeResult.InvalidData;

      archive.Position = fileOffset;

      Lzma2DecodeResult r = Lzma2Decoder.DecodeStreamToStream(
          archive, (long)packSize, properties, output, out bytesWritten, progress, token);

      return r switch
      {
        Lzma2DecodeResult.Finished => SevenZipFolderDecodeResult.Ok,
        Lzma2DecodeResult.NotSupported => SevenZipFolderDecodeResult.NotSupported,
        _ => SevenZipFolderDecodeResult.InvalidData,
      };
    }

    // Прочие folder-ы (PPMd/BCJ2/Copy/цепочки — их производит потоковый Auto): читаем packed этого
    // folder-а из потока в память (folder = один файл, размер ограничен) и декодируем ин-мемори
    // логикой DecodeFolderToArray через синтетический одно-folder StreamsInfo.
    return DecodeNonLzma2FolderFromStream(
        streamsInfo, archive, packedBaseOffset, folderIndex, options, output, out bytesWritten, progress, token);
  }

  // Фолбэк потокового декода для не-LZMA2 folder-ов: вычисляет байтовый packed-регион folder-а,
  // читает его в память и переиспользует полноценный ин-мемори декодер (BCJ2/PPMd/Copy/цепочки),
  // подсовывая ему синтетический StreamsInfo, где этот folder — единственный (folderIndex 0, PackPos 0).
  private static SevenZipFolderDecodeResult DecodeNonLzma2FolderFromStream(
      SevenZipStreamsInfo streamsInfo,
      System.IO.Stream archive,
      long packedBaseOffset,
      int folderIndex,
      SevenZipDecodeOptions options,
      System.IO.Stream output,
      out long bytesWritten,
      IProgress<LzmaProgress>? progress,
      System.Threading.CancellationToken token)
  {
    bytesWritten = 0;

    if (streamsInfo.PackInfo is not { } packInfo)
      return SevenZipFolderDecodeResult.InvalidData;
    if (streamsInfo.UnpackInfo is not { } unpackInfo)
      return SevenZipFolderDecodeResult.InvalidData;
    if ((uint)folderIndex >= (uint)unpackInfo.Folders.Length)
      return SevenZipFolderDecodeResult.InvalidData;
    if ((uint)folderIndex >= (uint)unpackInfo.FolderUnpackSizes.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    SevenZipFolder folder = unpackInfo.Folders[folderIndex];
    ulong[]? folderUnpackSizes = unpackInfo.FolderUnpackSizes[folderIndex];
    if (folderUnpackSizes is null)
      return SevenZipFolderDecodeResult.InvalidData;

    int packCount = folder.PackedStreamIndices.Length;

    // Глобальный индекс первого pack-стрима folder-а (pack-стримы folder-ов идут подряд).
    ulong startPackIndex = 0;
    for (int i = 0; i < folderIndex; i++)
      startPackIndex += (ulong)unpackInfo.Folders[i].PackedStreamIndices.Length;

    if (startPackIndex + (ulong)packCount > (ulong)packInfo.PackSizes.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    // Байтовое смещение и размер packed-региона folder-а + его pack-размеры (в порядке folder-а).
    ulong regionStart = packInfo.PackPos;
    for (ulong i = 0; i < startPackIndex; i++)
      regionStart += packInfo.PackSizes[(int)i];

    ulong regionSize = 0;
    var folderPackSizes = new ulong[packCount];
    for (int i = 0; i < packCount; i++)
    {
      ulong sz = packInfo.PackSizes[(int)startPackIndex + i];
      folderPackSizes[i] = sz;
      regionSize += sz;
    }

    if (regionSize > int.MaxValue) // ин-мемори декодер работает со span/int
      return SevenZipFolderDecodeResult.NotSupported;

    long fileOffset = packedBaseOffset + (long)regionStart;
    if (fileOffset < 0 || fileOffset + (long)regionSize > archive.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    byte[] localPacked = new byte[(int)regionSize];
    archive.Position = fileOffset;
    ReadExactly(archive, localPacked);

    // Синтетика: этот folder — единственный (folderIndex 0), packed-регион с нуля.
    var synthPack = new SevenZipPackInfo(0, folderPackSizes);
    var synthUnpack = new SevenZipUnpackInfo([folder], [folderUnpackSizes]);
    var synthStreams = new SevenZipStreamsInfo(synthPack, synthUnpack, null);

    SevenZipFolderDecodeResult result = DecodeFolderToArray(
        synthStreams, localPacked, folderIndex: 0, options, out byte[] decoded, progress, token);

    if (result == SevenZipFolderDecodeResult.Ok)
    {
      output.Write(decoded, 0, decoded.Length);
      bytesWritten = decoded.LongLength;
    }

    return result;
  }

  // Читает ровно buffer.Length байт из потока (или бросает при неожиданном конце).
  private static void ReadExactly(System.IO.Stream source, byte[] buffer)
  {
    int offset = 0;
    while (offset < buffer.Length)
    {
      int n = source.Read(buffer, offset, buffer.Length - offset);
      if (n <= 0)
        throw new System.IO.EndOfStreamException("Packed-регион folder-а короче заявленного.");
      offset += n;
    }
  }

  // Как TryGetSingleLzma2Coder, но вместо среза packed-данных отдаёт СМЕЩЕНИЕ и РАЗМЕР pack-стрима
  // folder-а в packed-регионе (ulong, поддержка > 2 ГиБ) — для потокового декода из архива-Stream.
  private static bool TryGetSingleLzma2CoderLocation(
      SevenZipStreamsInfo streamsInfo,
      int folderIndex,
      out ulong packStart,
      out ulong packSize,
      out byte lzma2PropertiesByte)
  {
    packStart = 0;
    packSize = 0;
    lzma2PropertiesByte = 0;

    if (streamsInfo.PackInfo is not { } packInfo)
      return false;
    if (streamsInfo.UnpackInfo is not { } unpackInfo)
      return false;
    if ((uint)folderIndex >= (uint)unpackInfo.Folders.Length)
      return false;

    SevenZipFolder folder = unpackInfo.Folders[folderIndex];

    if (folder.Coders.Length != 1 || folder.PackedStreamIndices.Length != 1)
      return false;

    SevenZipCoderInfo coder = folder.Coders[0];

    if (!IsSingleByteMethodId(coder.MethodId, _methodIdLzma2))
      return false;
    if (coder.NumInStreams != 1 || coder.NumOutStreams != 1)
      return false;
    if (coder.Properties is null || coder.Properties.Length != 1)
      return false;

    ulong packStreamIndex = 0;
    for (int i = 0; i < folderIndex; i++)
      packStreamIndex += (ulong)unpackInfo.Folders[i].PackedStreamIndices.Length;

    if (packStreamIndex >= (ulong)packInfo.PackSizes.Length)
      return false;

    ulong start = packInfo.PackPos;
    for (ulong i = 0; i < packStreamIndex; i++)
      start += packInfo.PackSizes[(int)i];

    packStart = start;
    packSize = packInfo.PackSizes[(int)packStreamIndex];
    lzma2PropertiesByte = coder.Properties[0];
    return true;
  }

  /// <summary>
  /// Декодирует Folder в массив байт.
  /// </summary>
  public static SevenZipFolderDecodeResult DecodeFolderToArray(
      SevenZipStreamsInfo streamsInfo,
      ReadOnlySpan<byte> packedStreams,
      int folderIndex,
      SevenZipDecodeOptions options,
      out byte[] output,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    output = [];

    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(streamsInfo);

    if (streamsInfo.PackInfo is not { } packInfo)
      return SevenZipFolderDecodeResult.InvalidData;

    if (streamsInfo.UnpackInfo is not { } unpackInfo)
      return SevenZipFolderDecodeResult.InvalidData;

    if ((uint)folderIndex >= (uint)unpackInfo.Folders.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    SevenZipFolder folder = unpackInfo.Folders[folderIndex];

    // Размеры распаковки в 7z лежат не в самом Folder, а отдельным массивом в UnpackInfo.
    if ((uint)folderIndex >= (uint)unpackInfo.FolderUnpackSizes.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    ulong[]? folderUnpackSizes = unpackInfo.FolderUnpackSizes[folderIndex];
    if (folderUnpackSizes is null || folderUnpackSizes.Length == 0)
      return SevenZipFolderDecodeResult.InvalidData;

    // BCJ2 — multi-stream coder, обрабатываем отдельной веткой (не линейный конвейер 1in/1out).
    bool hasBcj2 = false;
    for (int i = 0; i < folder.Coders.Length; i++)
    {
      if (IsBcj2MethodId(folder.Coders[i].MethodId))
      {
        hasBcj2 = true;
        break;
      }
    }

    if (hasBcj2)
    {
      SevenZipFolderDecodeResult rInputs = TryDecodeBcj2InputStreamsToArrays(
        streamsInfo,
        packedStreams,
        folderIndex,
        out byte[][] inputs);

      if (rInputs != SevenZipFolderDecodeResult.Ok)
        return rInputs;

      if (inputs.Length != 4)
        return SevenZipFolderDecodeResult.InvalidData;

      if (folder.NumOutStreams > int.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      int totalOut = (int)folder.NumOutStreams;

      if (folderUnpackSizes.Length != totalOut)
        return SevenZipFolderDecodeResult.InvalidData;

      // Находим финальный out stream folder'а: тот, который НЕ используется как producer (BindPairs.OutIndex).
      bool[] outUsed = new bool[totalOut];

      for (int i = 0; i < folder.BindPairs.Length; i++)
      {
        ulong outIndexU64 = folder.BindPairs[i].OutIndex;
        if (outIndexU64 > int.MaxValue)
          return SevenZipFolderDecodeResult.NotSupported;

        int outIndex = (int)outIndexU64;
        if ((uint)outIndex >= (uint)totalOut)
          return SevenZipFolderDecodeResult.InvalidData;

        outUsed[outIndex] = true;
      }

      int finalOutIndex = -1;
      for (int i = 0; i < totalOut; i++)
      {
        if (!outUsed[i])
        {
          if (finalOutIndex != -1)
            return SevenZipFolderDecodeResult.NotSupported; // не один финальный выход

          finalOutIndex = i;
        }
      }

      if (finalOutIndex < 0)
        return SevenZipFolderDecodeResult.NotSupported;

      ulong outSizeU64 = folderUnpackSizes[finalOutIndex];
      if (outSizeU64 > int.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      int outSize = (int)outSizeU64;

      return TryDecodeBcj2ToArray(
        buf0: inputs[0],
        buf1: inputs[1],
        buf2: inputs[2],
        buf3: inputs[3],
        outSize: outSize,
        output: out output);
    }

    // Линейный конвейер:
    // - ровно один packed stream;
    // - N coders (N >= 1);
    // - каждый coder: 1 in / 1 out;
    // - BindPairs образуют цепочку (N - 1 связей).
    if (folder.PackedStreamIndices.Length != 1)
      return SevenZipFolderDecodeResult.NotSupported;

    int coderCount = folder.Coders.Length;
    if (coderCount <= 0)
      return SevenZipFolderDecodeResult.InvalidData;

    if (folder.BindPairs.Length != coderCount - 1)
      return SevenZipFolderDecodeResult.NotSupported;

    if (folder.NumInStreams != (ulong)coderCount || folder.NumOutStreams != (ulong)coderCount)
      return SevenZipFolderDecodeResult.InvalidData;

    for (int i = 0; i < coderCount; i++)
    {
      SevenZipCoderInfo coder = folder.Coders[i];
      if (coder.NumInStreams != 1 || coder.NumOutStreams != 1)
        return SevenZipFolderDecodeResult.NotSupported;
    }

    ulong packStreamIndexU64 = 0;
    for (int i = 0; i < folderIndex; i++)
      packStreamIndexU64 += (ulong)unpackInfo.Folders[i].PackedStreamIndices.Length;

    if (packStreamIndexU64 > int.MaxValue)
      return SevenZipFolderDecodeResult.NotSupported;

    uint packStreamIndex = (uint)packStreamIndexU64;
    if (packStreamIndex >= (uint)packInfo.PackSizes.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    if (!TryGetPackStream(packInfo, packedStreams, packStreamIndex, out ReadOnlySpan<byte> packStream))
      return SevenZipFolderDecodeResult.InvalidData;

    static SevenZipFolderDecodeResult DecodeOneCoder(
    SevenZipCoderInfo coder,
    ReadOnlySpan<byte> input,
    int expectedUnpackSize,
    SevenZipDecodeOptions options,
    IProgress<LzmaProgress>? progress,
    System.Threading.CancellationToken token,
    out byte[] decoded)
    {
      decoded = [];

      if (SevenZipAesCoder.IsAesMethodId(coder.MethodId))
        return TryDecodeAesCoder(coder, input, expectedUnpackSize, options, out decoded);

      if (SevenZipGostCoder.IsGostMethodId(coder.MethodId))
        return TryDecodeGostCoder(coder, input, expectedUnpackSize, options, out decoded);

      if (IsSingleByteMethodId(coder.MethodId, _methodIdCopy))
        return TryDecodeCopyCoder(input, expectedUnpackSize, out decoded);

      if (IsSingleByteMethodId(coder.MethodId, _methodIdDelta))
        return TryDecodeDeltaCoder(coder, input, expectedUnpackSize, out decoded);

      if (IsSwap2MethodId(coder.MethodId))
        return TryDecodeSwap2Coder(coder, input, expectedUnpackSize, out decoded);

      if (IsSwap4MethodId(coder.MethodId))
        return TryDecodeSwap4Coder(coder, input, expectedUnpackSize, out decoded);

      if (IsBcjX86MethodId(coder.MethodId))
        return TryDecodeBcjCoder(coder, input, expectedUnpackSize, SevenZipBcjFilters.X86DecodeInPlace, out decoded);

      if (IsBcjArmMethodId(coder.MethodId))
        return TryDecodeBcjCoder(coder, input, expectedUnpackSize, SevenZipBcjFilters.ArmDecodeInPlace, out decoded);

      if (IsBcjArmtMethodId(coder.MethodId))
        return TryDecodeBcjCoder(coder, input, expectedUnpackSize, SevenZipBcjFilters.ArmtDecodeInPlace, out decoded);

      if (IsBcjPpcMethodId(coder.MethodId))
        return TryDecodeBcjCoder(coder, input, expectedUnpackSize, SevenZipBcjFilters.PpcDecodeInPlace, out decoded);

      if (IsBcjSparcMethodId(coder.MethodId))
        return TryDecodeBcjCoder(coder, input, expectedUnpackSize, SevenZipBcjFilters.SparcDecodeInPlace, out decoded);

      if (IsBcjIa64MethodId(coder.MethodId))
        return TryDecodeBcjCoder(coder, input, expectedUnpackSize, SevenZipBcjFilters.Ia64DecodeInPlace, out decoded);

      if (IsBcjArm64MethodId(coder.MethodId))
        return TryDecodeBcjCoder(coder, input, expectedUnpackSize, SevenZipBcjFilters.Arm64DecodeInPlace, out decoded);

      if (IsSingleByteMethodId(coder.MethodId, _methodIdLzma2))
        return TryDecodeLzma2Coder(coder, input, expectedUnpackSize, out decoded, progress, token);

      // LZMA (7z) method id = { 03 01 01 }.
      if (coder.MethodId.Length == 3 &&
          coder.MethodId[0] == 0x03 &&
          coder.MethodId[1] == 0x01 &&
          coder.MethodId[2] == 0x01)
        return TryDecodeLzmaCoder(coder, input, expectedUnpackSize, out decoded);

      if (IsBZip2MethodId(coder.MethodId))
        return TryDecodeBZip2Coder(input, expectedUnpackSize, out decoded);

      // PPMd (7z): MethodId = { 03 04 01 }.
      if (coder.MethodId.Length == 3 &&
          coder.MethodId[0] == 0x03 &&
          coder.MethodId[1] == 0x04 &&
          coder.MethodId[2] == 0x01)
        return TryDecodePpmdCoder(coder, input, expectedUnpackSize, out decoded);

      if (IsDeflateMethodId(coder.MethodId))
        return TryDecodeDeflateCoder(input, expectedUnpackSize, out decoded);

      if (IsDeflate64MethodId(coder.MethodId))
        return TryDecodeDeflate64Coder(input, expectedUnpackSize, out decoded);

      return SevenZipFolderDecodeResult.NotSupported;
    }

    if (folderUnpackSizes.Length != coderCount)
      return SevenZipFolderDecodeResult.NotSupported;

    // Строим линейный граф связей: producer(out) -> consumer(in).
    // В нашем ограниченном режиме (1in/1out на coder) индексы потоков совпадают с индексами coder'ов.
    int[] next = new int[coderCount];
    int[] prev = new int[coderCount];
    Array.Fill(next, -1);
    Array.Fill(prev, -1);

    for (int i = 0; i < folder.BindPairs.Length; i++)
    {
      SevenZipBindPair bp = folder.BindPairs[i];

      if (bp.InIndex >= (ulong)coderCount || bp.OutIndex >= (ulong)coderCount)
        return SevenZipFolderDecodeResult.InvalidData;

      int consumer = (int)bp.InIndex;
      int producer = (int)bp.OutIndex;

      if (consumer == producer)
        return SevenZipFolderDecodeResult.InvalidData;

      // Одна входная струя не может иметь двух источников.
      if (prev[consumer] != -1)
        return SevenZipFolderDecodeResult.InvalidData;

      // Один выход не может быть разветвлён на двух потребителей (в нашем режиме).
      if (next[producer] != -1)
        return SevenZipFolderDecodeResult.InvalidData;

      prev[consumer] = producer;
      next[producer] = consumer;
    }

    int startCoder = -1;
    for (int i = 0; i < coderCount; i++)
    {
      if (prev[i] == -1)
      {
        if (startCoder != -1)
          return SevenZipFolderDecodeResult.NotSupported; // не цепочка (несколько стартов)
        startCoder = i;
      }
    }

    if (startCoder == -1)
      return SevenZipFolderDecodeResult.NotSupported; // цикл без старта

    bool[] visited = new bool[coderCount];
    ReadOnlySpan<byte> currentInput = packStream;
    byte[] lastDecoded = [];

    int current = startCoder;
    for (int step = 0; step < coderCount; step++)
    {
      if ((uint)current >= (uint)coderCount)
        return SevenZipFolderDecodeResult.NotSupported;

      if (visited[current])
        return SevenZipFolderDecodeResult.NotSupported; // цикл

      visited[current] = true;

      ulong expectedU64 = folderUnpackSizes[current];
      if (expectedU64 > int.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      int expectedSize = (int)expectedU64;

      SevenZipFolderDecodeResult r = DecodeOneCoder(
        coder: folder.Coders[current],
        input: currentInput,
        expectedUnpackSize: expectedSize,
        options: options,
        progress: coderCount == 1 ? progress : null,
        token: token,
        decoded: out byte[] decoded);

      if (r != SevenZipFolderDecodeResult.Ok)
      {
        output = [];
        return r;
      }

      lastDecoded = decoded;
      currentInput = decoded;
      current = next[current]; // -1 после конца
    }

    if (current != -1)
      return SevenZipFolderDecodeResult.NotSupported;

    output = lastDecoded;
    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>BZip2</c> (method id { 04 02 02 }) собственным BZip2-декодером.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeBZip2Coder(
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded)
  {
    decoded = [];

    BZip2DecodeResult result = BZip2Decoder.Decode(input, out byte[] output);

    if (result == BZip2DecodeResult.NotSupported)
      return SevenZipFolderDecodeResult.NotSupported;

    if (result != BZip2DecodeResult.Ok || output.Length != expectedUnpackSize)
      return SevenZipFolderDecodeResult.InvalidData;

    decoded = output;
    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>PPMd</c> (method id { 03 04 01 }).
  /// Properties = 5 байт: [0] = order, [1..4] = memSize (UInt32 LE).
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodePpmdCoder(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded)
  {
    decoded = [];

    if (coder.Properties is null || coder.Properties.Length != 5)
      return SevenZipFolderDecodeResult.InvalidData;

    int order = coder.Properties[0];
    uint memSize = BinaryPrimitives.ReadUInt32LittleEndian(coder.Properties.AsSpan(1, 4));

    byte[] output = new byte[expectedUnpackSize];

    Ppmd7DecodeResult result = Ppmd7Decoder.Decode(input, order, memSize, output);

    if (result == Ppmd7DecodeResult.NotSupported)
      return SevenZipFolderDecodeResult.NotSupported;

    if (result != Ppmd7DecodeResult.Ok)
      return SevenZipFolderDecodeResult.InvalidData;

    decoded = output;
    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>Deflate</c> (method id { 04 01 08 }) собственным DEFLATE-декодером.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeDeflateCoder(
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded)
  {
    decoded = new byte[expectedUnpackSize];

    DeflateDecodeResult result = DeflateDecoder.Decode(
        input,
        decoded,
        out int bytesConsumed,
        out int bytesWritten);

    if (result != DeflateDecodeResult.Ok || bytesWritten != expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    // Хвост в packed stream обычно недопустим, но нулевое выравнивание допускаем
    // (как в LZMA2/BZip2 ветках).
    if (bytesConsumed < input.Length && !IsZeroTail(input, bytesConsumed))
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>Deflate64</c> (method id { 04 01 09 }) собственным
  /// DEFLATE-декодером в режиме Deflate64.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeDeflate64Coder(
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded)
  {
    decoded = new byte[expectedUnpackSize];

    DeflateDecodeResult result = DeflateDecoder.Decode(
        input,
        decoded,
        deflate64: true,
        out int bytesConsumed,
        out int bytesWritten);

    if (result != DeflateDecodeResult.Ok || bytesWritten != expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (bytesConsumed < input.Length && !IsZeroTail(input, bytesConsumed))
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Проверяет, что хвост packed stream начиная с <paramref name="start"/> состоит только из нулей.
  /// </summary>
  private static bool IsZeroTail(ReadOnlySpan<byte> packed, int start)
  {
    for (int i = start; i < packed.Length; i++)
      if (packed[i] != 0)
        return false;

    return true;
  }

  /// <summary>
  /// Расшифровывает одиночный AES-coder (7zAES). Пароль берётся из <paramref name="options"/>.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeAesCoder(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      SevenZipDecodeOptions options,
      out byte[] decoded)
  {
    decoded = [];

    ReadOnlySpan<byte> aesProperties = coder.Properties ?? [];

    if (!SevenZipAesCoder.TryParseProperties(aesProperties, out SevenZipAesProperties? parsedAesProperties))
      return SevenZipFolderDecodeResult.InvalidData;

    if (!SevenZipAesCoder.IsSupportedNumCyclesPower(parsedAesProperties!.NumCyclesPower))
      return SevenZipFolderDecodeResult.NotSupported;

    if (options.Password is null)
      return SevenZipFolderDecodeResult.NotSupported;

    SevenZipAesDecryptResult decryptResult = SevenZipAesPackedStreamDecryptor.TryDecrypt(
        properties: parsedAesProperties,
        password: options.Password,
        ciphertext: input,
        plaintext: out decoded);

    if (decryptResult == SevenZipAesDecryptResult.NotSupported)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.NotSupported;
    }

    if (decryptResult == SevenZipAesDecryptResult.InvalidData)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (decoded.Length < expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (decoded.Length > expectedUnpackSize)
    {
      ReadOnlySpan<byte> tail = decoded.AsSpan(expectedUnpackSize);

      for (int i = 0; i < tail.Length; i++)
      {
        if (tail[i] != 0)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }
      }

      Array.Resize(ref decoded, expectedUnpackSize);
    }

    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Расшифровывает одиночный экспериментальный GOST-coder. Пароль берётся из <paramref name="options"/>.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeGostCoder(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      SevenZipDecodeOptions options,
      out byte[] decoded)
  {
    decoded = [];

    ReadOnlySpan<byte> gostProperties = coder.Properties ?? [];

    if (!SevenZipGostCoder.TryParseProperties(gostProperties, out SevenZipGostProperties? parsedGostProperties))
      return SevenZipFolderDecodeResult.InvalidData;

    if (options.Password is null)
      return SevenZipFolderDecodeResult.NotSupported;

    SevenZipGostDecryptResult decryptResult = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: coder.MethodId,
        properties: parsedGostProperties!,
        password: options.Password,
        ciphertext: input,
        plaintext: out decoded);

    if (decryptResult == SevenZipGostDecryptResult.NotSupported)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.NotSupported;
    }

    if (decryptResult == SevenZipGostDecryptResult.InvalidData)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (decoded.Length != expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>LZMA2</c>: properties — 1 байт размера словаря.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeLzma2Coder(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded,
      IProgress<LzmaProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    decoded = [];

    if (coder.Properties is null || coder.Properties.Length != 1)
      return SevenZipFolderDecodeResult.InvalidData;

    byte lzma2PropertiesByte = coder.Properties[0];

    // В 7z LZMA2 properties — это 1 байт, допустимый диапазон: 0..40.
    if (!SevenZipLzma2Coder.TryDecodeDictionarySize(lzma2PropertiesByte, out uint dictionarySize))
      return SevenZipFolderDecodeResult.InvalidData;

    if (dictionarySize > int.MaxValue)
      return SevenZipFolderDecodeResult.NotSupported;

    Lzma2DecodeResult lzma2Result = Lzma2Decoder.DecodeToArray(
      input: input,
      dictionaryProp: lzma2PropertiesByte,
      output: out decoded,
      bytesConsumed: out int lzma2BytesConsumed,
      progress: progress,
      token: token);

    if (lzma2Result == Lzma2DecodeResult.NotSupported)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.NotSupported;
    }

    if (lzma2Result == Lzma2DecodeResult.InvalidData)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (decoded.Length != expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if ((uint)lzma2BytesConsumed > (uint)input.Length)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    // Допускаем хвост из нулей.
    if (lzma2BytesConsumed != input.Length)
    {
      ReadOnlySpan<byte> tail = input[lzma2BytesConsumed..];
      for (int i = 0; i < tail.Length; i++)
      {
        if (tail[i] != 0)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }
      }
    }

    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>LZMA</c> (method id { 03 01 01 }).
  /// Properties — 5 байт: [0] = байт lc/lp/pb, [1..4] = размер словаря (UInt32 LE).
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeLzmaCoder(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded)
  {
    decoded = [];

    if (coder.Properties is null || coder.Properties.Length != 5)
      return SevenZipFolderDecodeResult.InvalidData;

    byte lzmaPropsByte = coder.Properties[0];
    if (!LzmaProperties.TryParse(lzmaPropsByte, out LzmaProperties lzmaProps))
      return SevenZipFolderDecodeResult.InvalidData;

    uint dictU32 = BinaryPrimitives.ReadUInt32LittleEndian(coder.Properties.AsSpan(1, 4));
    if (dictU32 == 0)
      return SevenZipFolderDecodeResult.InvalidData;

    if (dictU32 > int.MaxValue)
      return SevenZipFolderDecodeResult.NotSupported;

    int dictSize = (int)dictU32;

    decoded = new byte[expectedUnpackSize];
    var decoder = new LzmaDecoder(lzmaProps, dictSize);

    LzmaDecodeResult lzmaResult = decoder.Decode(
      src: input,
      bytesConsumed: out int lzmaBytesConsumed,
      dst: decoded,
      bytesWritten: out int lzmaBytesWritten,
      progress: out _);

    if (lzmaResult == LzmaDecodeResult.NotImplemented)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.NotSupported;
    }

    if (lzmaResult == LzmaDecodeResult.InvalidData)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (lzmaResult == LzmaDecodeResult.NeedsMoreInput)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (lzmaBytesWritten != expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if ((uint)lzmaBytesConsumed > (uint)input.Length)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    // Для raw LZMA хвост не валидируем.
    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>BCJ branch-фильтр: decode-преобразование на месте.</summary>
  private delegate void BcjFilter(Span<byte> data, uint startOffset);

  /// <summary>
  /// Декодирует одиночный BCJ-coder: общий путь для всех ветвлений (x86/ARM/ARMT/PPC/SPARC/IA64/ARM64).
  /// Опциональный startOffset берётся из properties (4 байта LE), фильтр не меняет размер данных.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeBcjCoder(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      BcjFilter filter,
      out byte[] decoded)
  {
    uint startOffset = 0;

    if (coder.Properties is not null && coder.Properties.Length != 0)
    {
      if (coder.Properties.Length != 4)
      {
        decoded = [];
        return SevenZipFolderDecodeResult.InvalidData;
      }

      startOffset = BinaryPrimitives.ReadUInt32LittleEndian(coder.Properties);
    }

    if (input.Length != expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    decoded = input.ToArray();
    filter(decoded, startOffset);
    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>Copy</c> (0x00): данные копируются как есть.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeCopyCoder(
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded)
  {
    decoded = input.ToArray();
    return decoded.Length == expectedUnpackSize
      ? SevenZipFolderDecodeResult.Ok
      : SevenZipFolderDecodeResult.InvalidData;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>Delta</c> (0x03).
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeDeltaCoder(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded)
  {
    // Properties: 1 byte, prop = delta - 1 => delta = prop + 1, диапазон 1..256.
    int delta;

    if (coder.Properties is null || coder.Properties.Length == 0) // На всякий случай: если props отсутствуют, считаем delta=1.
      delta = 1;
    else if (coder.Properties.Length == 1)
      delta = coder.Properties[0] + 1;
    else
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if ((uint)(delta - 1) > 255u) // delta must be 1..256
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    // Delta не меняет размер.
    if (input.Length != expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    decoded = input.ToArray();

    // Decode: out[i] = in[i] + out[i-delta] (mod 256), i>=delta.
    // Первые delta байт остаются как есть (state=0).
    Span<byte> dst = decoded;
    for (int i = delta; i < dst.Length; i++)
      dst[i] = unchecked((byte)(dst[i] + dst[i - delta]));

    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>Swap2</c>: обмен байтов в каждом 2-байтном слове.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeSwap2Coder(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded)
  {
    // В 7-Zip фильтр обрабатывает только полные блоки; хвост < 2 байт остаётся как есть.
    if (coder.Properties is not null && coder.Properties.Length != 0)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (input.Length != expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    decoded = input.ToArray();

    for (int i = 0; i + 2 <= decoded.Length; i += 2)
      (decoded[i + 1], decoded[i]) = (decoded[i], decoded[i + 1]);

    return SevenZipFolderDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует одиночный coder <c>Swap4</c>: реверс байтов в каждом 4-байтном слове.
  /// </summary>
  private static SevenZipFolderDecodeResult TryDecodeSwap4Coder(
      SevenZipCoderInfo coder,
      ReadOnlySpan<byte> input,
      int expectedUnpackSize,
      out byte[] decoded)
  {
    // Хвост < 4 байт остаётся как есть.
    if (coder.Properties is not null && coder.Properties.Length != 0)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (input.Length != expectedUnpackSize)
    {
      decoded = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    decoded = input.ToArray();

    for (int i = 0; i + 4 <= decoded.Length; i += 4)
    {
      (decoded[i], decoded[i + 3]) = (decoded[i + 3], decoded[i]);
      (decoded[i + 1], decoded[i + 2]) = (decoded[i + 2], decoded[i + 1]);
    }

    return SevenZipFolderDecodeResult.Ok;
  }

  public static SevenZipFolderDecodeResult TryGetFolderPackedStreamRanges(
  SevenZipStreamsInfo streamsInfo,
  ReadOnlySpan<byte> packedStreams,
  int folderIndex,
  out SevenZipFolderPackedStreamRange[] ranges)
  {
    ranges = [];

    ArgumentNullException.ThrowIfNull(streamsInfo);

    if (streamsInfo.PackInfo is not { } packInfo)
      return SevenZipFolderDecodeResult.InvalidData;

    if (streamsInfo.UnpackInfo is not { } unpackInfo)
      return SevenZipFolderDecodeResult.InvalidData;

    if ((uint)folderIndex >= (uint)unpackInfo.Folders.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    SevenZipFolder folder = unpackInfo.Folders[folderIndex];

    if (folder.PackedStreamIndices.Length == 0)
      return SevenZipFolderDecodeResult.InvalidData;

    int folderPackedStreamCount = folder.PackedStreamIndices.Length;

    // Глобальный индекс первого pack stream'а folder'а в PackInfo.
    // На этапе 1 предполагаем стандартный порядок: pack streams идут подряд по folder'ам.
    ulong basePackStreamIndexU64 = 0;
    for (int i = 0; i < folderIndex; i++)
      basePackStreamIndexU64 += (ulong)unpackInfo.Folders[i].PackedStreamIndices.Length;

    if (basePackStreamIndexU64 > int.MaxValue)
      return SevenZipFolderDecodeResult.NotSupported;

    if (basePackStreamIndexU64 + (ulong)folderPackedStreamCount > (ulong)packInfo.PackSizes.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    int basePackStreamIndex = (int)basePackStreamIndexU64;

    // Вычисляем стартовый offset внутри packedStreams: PackPos + sum(PackSizes[0..base-1]).
    ulong startU64 = packInfo.PackPos;
    for (int i = 0; i < basePackStreamIndex; i++)
      startU64 += packInfo.PackSizes[i];

    if (startU64 > (ulong)packedStreams.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    if (startU64 > int.MaxValue)
      return SevenZipFolderDecodeResult.NotSupported;

    var tmp = new SevenZipFolderPackedStreamRange[folderPackedStreamCount];

    ulong curStart = startU64;

    for (int i = 0; i < folderPackedStreamCount; i++)
    {
      int globalIndex = basePackStreamIndex + i;
      ulong sizeU64 = packInfo.PackSizes[globalIndex];

      if (curStart > (ulong)packedStreams.Length)
        return SevenZipFolderDecodeResult.InvalidData;

      if (sizeU64 > (ulong)packedStreams.Length - curStart)
        return SevenZipFolderDecodeResult.InvalidData;

      if (curStart > int.MaxValue || sizeU64 > int.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      tmp[i] = new SevenZipFolderPackedStreamRange(
        folderInIndex: folder.PackedStreamIndices[i],
        packStreamIndex: (uint)globalIndex,
        offset: (int)curStart,
        length: (int)sizeU64);

      curStart += sizeU64;
    }

    ranges = tmp;
    return SevenZipFolderDecodeResult.Ok;
  }

  public static SevenZipFolderDecodeResult TryDecodeBcj2ToArray(
  ReadOnlySpan<byte> buf0,
  ReadOnlySpan<byte> buf1,
  ReadOnlySpan<byte> buf2,
  ReadOnlySpan<byte> buf3,
  int outSize,
  out byte[] output)
      => SevenZipBcj2Decoder.DecodeToArray(buf0, buf1, buf2, buf3, outSize, out output);

  public static SevenZipFolderDecodeResult TryDecodeBcj2InputStreamsToArrays(
  SevenZipStreamsInfo streamsInfo,
  ReadOnlySpan<byte> packedStreams,
  int folderIndex,
  out byte[][] decodedInputStreams)
  {
    decodedInputStreams = [];

    ArgumentNullException.ThrowIfNull(streamsInfo);

    if (streamsInfo.PackInfo is not { })
      return SevenZipFolderDecodeResult.InvalidData;

    if (streamsInfo.UnpackInfo is not { } unpackInfo)
      return SevenZipFolderDecodeResult.InvalidData;

    if ((uint)folderIndex >= (uint)unpackInfo.Folders.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    if ((uint)folderIndex >= (uint)unpackInfo.FolderUnpackSizes.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    ulong[]? folderUnpackSizes = unpackInfo.FolderUnpackSizes[folderIndex];
    if (folderUnpackSizes is null || folderUnpackSizes.Length == 0)
      return SevenZipFolderDecodeResult.InvalidData;

    SevenZipFolder folder = unpackInfo.Folders[folderIndex];

    // Для BCJ2 ожидаем 4 входных packed stream'а.
    if (folder.PackedStreamIndices.Length != 4)
      return SevenZipFolderDecodeResult.NotSupported;

    // Получаем диапазоны 4 packed stream'ов.
    SevenZipFolderDecodeResult rr = TryGetFolderPackedStreamRanges(
      streamsInfo,
      packedStreams,
      folderIndex,
      out SevenZipFolderPackedStreamRange[] ranges);

    if (rr != SevenZipFolderDecodeResult.Ok)
      return rr;

    if (ranges.Length != 4)
      return SevenZipFolderDecodeResult.InvalidData;

    int coderCount = folder.Coders.Length;
    if (coderCount == 0)
      return SevenZipFolderDecodeResult.InvalidData;

    // Строим offsets входных/выходных потоков для каждого coder.
    // И заполняем owner-таблицы: глобальный in/out индекс -> coderIndex.
    if (folder.NumInStreams > int.MaxValue || folder.NumOutStreams > int.MaxValue)
      return SevenZipFolderDecodeResult.NotSupported;

    int totalIn = (int)folder.NumInStreams;
    int totalOut = (int)folder.NumOutStreams;

    var coderInStart = new int[coderCount];
    var coderOutStart = new int[coderCount];

    var inOwner = new int[totalIn];
    var outOwner = new int[totalOut];

    int inCursor = 0;
    int outCursor = 0;

    int bcj2CoderIndex = -1;

    for (int ci = 0; ci < coderCount; ci++)
    {
      SevenZipCoderInfo c = folder.Coders[ci];

      if (c.NumInStreams > int.MaxValue || c.NumOutStreams > int.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      int nin = (int)c.NumInStreams;
      int nout = (int)c.NumOutStreams;

      if (inCursor > totalIn - nin || outCursor > totalOut - nout)
        return SevenZipFolderDecodeResult.InvalidData;

      coderInStart[ci] = inCursor;
      coderOutStart[ci] = outCursor;

      for (int k = 0; k < nin; k++)
        inOwner[inCursor + k] = ci;

      for (int k = 0; k < nout; k++)
        outOwner[outCursor + k] = ci;

      if (IsBcj2MethodId(c.MethodId))
      {
        if (bcj2CoderIndex != -1)
          return SevenZipFolderDecodeResult.NotSupported; // больше одного BCJ2 на этапе 1 не поддерживаем
        bcj2CoderIndex = ci;
      }

      inCursor += nin;
      outCursor += nout;
    }

    if (inCursor != totalIn || outCursor != totalOut)
      return SevenZipFolderDecodeResult.InvalidData;

    if (bcj2CoderIndex == -1)
      return SevenZipFolderDecodeResult.NotSupported;

    SevenZipCoderInfo bcj2Coder = folder.Coders[bcj2CoderIndex];

    if (bcj2Coder.NumInStreams != 4 || bcj2Coder.NumOutStreams != 1)
      return SevenZipFolderDecodeResult.NotSupported;

    int bcj2InStart = coderInStart[bcj2CoderIndex];

    if (folderUnpackSizes.Length != totalOut)
      return SevenZipFolderDecodeResult.InvalidData;

    // Результат: 4 входных потока BCJ2 в порядке slot'ов 0..3.
    var result = new byte[4][];
    var filled = new bool[4];

    // Для каждого входа BCJ2:
    // 1) по BindPairs находим producer OutIndex,
    // 2) по outOwner узнаём producer coder (ожидаем LZMA2 1in/1out),
    // 3) его единственный InIndex должен быть одним из PackedStreamIndices -> берём соответствующий range,
    // 4) распаковываем LZMA2 и кладём в result[slot].
    for (int slot = 0; slot < 4; slot++)
    {
      ulong consumerIn = (ulong)(bcj2InStart + slot);

      bool found = false;
      ulong producerOut = 0;

      for (int i = 0; i < folder.BindPairs.Length; i++)
      {
        SevenZipBindPair bp = folder.BindPairs[i];
        if (bp.InIndex == consumerIn)
        {
          producerOut = bp.OutIndex;
          found = true;
          break;
        }
      }

      if (!found)
      {
        // Для BCJ2 один из входных потоков может быть unbound и лежать в packed stream напрямую
        // (без producer coder'а). В этом случае просто берём байты packed stream как есть.
        int localPackOrdinal = -1;
        for (int i = 0; i < folder.PackedStreamIndices.Length; i++)
        {
          if (folder.PackedStreamIndices[i] == consumerIn)
          {
            localPackOrdinal = i;
            break;
          }
        }

        if (localPackOrdinal < 0)
          return SevenZipFolderDecodeResult.InvalidData;

        ReadOnlySpan<byte> raw = packedStreams.Slice(ranges[localPackOrdinal].Offset, ranges[localPackOrdinal].Length);

        if (filled[slot])
          return SevenZipFolderDecodeResult.InvalidData;

        result[slot] = raw.ToArray();
        filled[slot] = true;
        continue;
      }

      if (producerOut > int.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      int producerOutIndex = (int)producerOut;
      if ((uint)producerOutIndex >= (uint)totalOut)
        return SevenZipFolderDecodeResult.InvalidData;

      int producerCoderIndex = outOwner[producerOutIndex];
      SevenZipCoderInfo producerCoder = folder.Coders[producerCoderIndex];

      // Producer coder для входа BCJ2 ожидаем 1in/1out (Copy / LZMA2 / LZMA).
      if (producerCoder.NumInStreams != 1 || producerCoder.NumOutStreams != 1)
        return SevenZipFolderDecodeResult.NotSupported;

      int producerInIndex = coderInStart[producerCoderIndex];

      int packOrdinal = -1;
      for (int i = 0; i < folder.PackedStreamIndices.Length; i++)
      {
        if (folder.PackedStreamIndices[i] == (ulong)producerInIndex)
        {
          packOrdinal = i;
          break;
        }
      }

      if (packOrdinal < 0)
        return SevenZipFolderDecodeResult.InvalidData;

      if (ranges[packOrdinal].FolderInIndex != folder.PackedStreamIndices[packOrdinal])
        return SevenZipFolderDecodeResult.InvalidData;

      ReadOnlySpan<byte> src = packedStreams.Slice(ranges[packOrdinal].Offset, ranges[packOrdinal].Length);

      if ((uint)producerOutIndex >= (uint)folderUnpackSizes.Length)
        return SevenZipFolderDecodeResult.InvalidData;

      ulong expectedU64 = folderUnpackSizes[producerOutIndex];
      if (expectedU64 > int.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      int expectedSize = (int)expectedU64;

      byte[] decoded;

      if (IsSingleByteMethodId(producerCoder.MethodId, _methodIdCopy))
      {
        decoded = src.ToArray();
        if (decoded.Length != expectedSize)
          return SevenZipFolderDecodeResult.InvalidData;
      }
      else if (IsSingleByteMethodId(producerCoder.MethodId, _methodIdLzma2))
      {
        if (producerCoder.Properties is null || producerCoder.Properties.Length != 1)
          return SevenZipFolderDecodeResult.InvalidData;

        byte lzma2PropertiesByte = producerCoder.Properties[0];

        if (!SevenZipLzma2Coder.TryDecodeDictionarySize(lzma2PropertiesByte, out uint dictionarySize))
          return SevenZipFolderDecodeResult.InvalidData;

        if (dictionarySize > int.MaxValue)
          return SevenZipFolderDecodeResult.NotSupported;

        Lzma2DecodeResult lzma2Result = Lzma2Decoder.DecodeToArray(
          input: src,
          dictionaryProp: lzma2PropertiesByte,
          output: out decoded,
          bytesConsumed: out int bytesConsumed);

        if (lzma2Result == Lzma2DecodeResult.NotSupported)
          return SevenZipFolderDecodeResult.NotSupported;

        if (lzma2Result == Lzma2DecodeResult.InvalidData)
          return SevenZipFolderDecodeResult.InvalidData;

        if (decoded.Length != expectedSize)
          return SevenZipFolderDecodeResult.InvalidData;

        if ((uint)bytesConsumed > (uint)src.Length)
          return SevenZipFolderDecodeResult.InvalidData;

        // Допускаем хвост из нулей.
        if (bytesConsumed != src.Length)
        {
          ReadOnlySpan<byte> tail = src[bytesConsumed..];
          for (int i = 0; i < tail.Length; i++)
          {
            if (tail[i] != 0)
              return SevenZipFolderDecodeResult.InvalidData;
          }
        }
      }
      else if (producerCoder.MethodId.Length == 3 &&
               producerCoder.MethodId[0] == 0x03 &&
               producerCoder.MethodId[1] == 0x01 &&
               producerCoder.MethodId[2] == 0x01)
      {
        // LZMA (7z): properties = 5 байт: [0]=propsByte, [1..4]=dictSize LE
        if (producerCoder.Properties is null || producerCoder.Properties.Length != 5)
          return SevenZipFolderDecodeResult.InvalidData;

        byte lzmaPropsByte = producerCoder.Properties[0];
        if (!LzmaProperties.TryParse(lzmaPropsByte, out LzmaProperties lzmaProps))
          return SevenZipFolderDecodeResult.InvalidData;

        uint dictU32 = BinaryPrimitives.ReadUInt32LittleEndian(producerCoder.Properties.AsSpan(1, 4));
        if (dictU32 == 0)
          return SevenZipFolderDecodeResult.InvalidData;

        if (dictU32 > int.MaxValue)
          return SevenZipFolderDecodeResult.NotSupported;

        decoded = new byte[expectedSize];
        var decoder = new LzmaDecoder(lzmaProps, (int)dictU32);

        LzmaDecodeResult lzmaResult = decoder.Decode(
          src: src,
          bytesConsumed: out int lzmaBytesConsumed,
          dst: decoded,
          bytesWritten: out int lzmaBytesWritten,
          progress: out _);

        if (lzmaResult == LzmaDecodeResult.NotImplemented)
          return SevenZipFolderDecodeResult.NotSupported;

        if (lzmaResult == LzmaDecodeResult.InvalidData || lzmaResult == LzmaDecodeResult.NeedsMoreInput)
          return SevenZipFolderDecodeResult.InvalidData;

        if (lzmaBytesWritten != expectedSize)
          return SevenZipFolderDecodeResult.InvalidData;

        if ((uint)lzmaBytesConsumed > (uint)src.Length)
          return SevenZipFolderDecodeResult.InvalidData;

        // Для raw LZMA хвост не валидируем.
      }
      else
      {
        return SevenZipFolderDecodeResult.NotSupported;
      }

      if (filled[slot])
        return SevenZipFolderDecodeResult.InvalidData;

      result[slot] = decoded;
      filled[slot] = true;
    }

    decodedInputStreams = result;
    return SevenZipFolderDecodeResult.Ok;
  }

  private static bool TryGetPackStream(
      SevenZipPackInfo packInfo,
      ReadOnlySpan<byte> packedStreams,
      uint packStreamIndex,
      out ReadOnlySpan<byte> packStream)
  {
    packStream = default;

    // Ограничим поддержку: индекс должен помещаться в int,
    // иначе Slice всё равно не сможет адресовать такие значения.
    if (packStreamIndex > int.MaxValue)
      return false;

    ulong start = packInfo.PackPos;

    // При packStreamIndex == 0 цикл не выполнится.
    for (int i = 0; i < (int)packStreamIndex; i++)
      start += packInfo.PackSizes[i];

    ulong size = packInfo.PackSizes[(int)packStreamIndex];

    if (start > (ulong)packedStreams.Length)
      return false;

    if (size > (ulong)packedStreams.Length - start)
      return false;

    if (start > int.MaxValue || size > int.MaxValue)
      return false;

    packStream = packedStreams.Slice((int)start, (int)size);
    return true;
  }
}
