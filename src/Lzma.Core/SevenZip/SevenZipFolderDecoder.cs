using System.Buffers.Binary;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

public enum SevenZipFolderDecodeResult
{
  Ok,
  InvalidData,
  NotSupported,
}

/// <summary>
/// <para>Декодирование данных папки (Folder) из 7z.</para>
/// <para>
/// На текущем этапе поддерживаем только простейший вариант:
/// - один coder
/// - один входной поток
/// - один выходной поток
/// - без BindPairs
/// - coder: Copy (0x00) или LZMA2 (0x21)
/// </para>
/// </summary>
public static class SevenZipFolderDecoder
{
  private const byte _methodIdCopy = 0x00;
  private const byte _methodIdLzma2 = 0x21;
  private const byte _methodIdDelta = 0x03;

  public static SevenZipFolderDecodeResult DecodeFolderToArray(
      SevenZipStreamsInfo streamsInfo,
      ReadOnlySpan<byte> packedStreams,
      int folderIndex,
      out byte[] output)
  {
    output = [];

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

    // На этапе 1 поддерживаем только "линейный конвейер":
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
      out byte[] decoded)
    {
      decoded = [];

      if (IsSingleByteMethodId(coder.MethodId, _methodIdCopy))
      {
        decoded = input.ToArray();
        return decoded.Length == expectedUnpackSize
          ? SevenZipFolderDecodeResult.Ok
          : SevenZipFolderDecodeResult.InvalidData;
      }

      if (IsSingleByteMethodId(coder.MethodId, _methodIdDelta))
      {
        // Delta filter (0x03):
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

      if (IsSwap2MethodId(coder.MethodId))
      {
        // Swap2: меняем местами байты в каждом 2-байтном слове.
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

      if (IsSwap4MethodId(coder.MethodId))
      {
        // Swap4: реверс байтов в каждом 4-байтном слове.
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

      if (IsBcjX86MethodId(coder.MethodId))
      {
        // BCJ x86: props обычно нет. Иногда можно встретить startOffset (4 байта LE).
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

        // Фильтр не меняет размер.
        if (input.Length != expectedUnpackSize)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        decoded = input.ToArray();
        BcjX86DecodeInPlace(decoded.AsSpan(), startOffset);
        return SevenZipFolderDecodeResult.Ok;
      }

      if (IsBcjArmMethodId(coder.MethodId))
      {
        // BCJ ARM: props обычно нет, но допускаем startOffset (4 байта LE), как и для других BCJ.
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

        // Фильтр не меняет размер.
        if (input.Length != expectedUnpackSize)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        decoded = input.ToArray();
        BcjArmDecodeInPlace(decoded.AsSpan(), startOffset);
        return SevenZipFolderDecodeResult.Ok;
      }

      if (IsBcjArmtMethodId(coder.MethodId))
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
        BcjArmtDecodeInPlace(decoded.AsSpan(), startOffset);
        return SevenZipFolderDecodeResult.Ok;
      }

      if (IsBcjPpcMethodId(coder.MethodId))
      {
        // PPC BCJ: props обычно нет, но допускаем startOffset (4 байта LE) как и в других BCJ.
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

        // Фильтр не меняет размер.
        if (input.Length != expectedUnpackSize)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        decoded = input.ToArray();
        BcjPpcDecodeInPlace(decoded.AsSpan(), startOffset);
        return SevenZipFolderDecodeResult.Ok;
      }

      if (IsBcjSparcMethodId(coder.MethodId))
      {
        // BCJ SPARC: props обычно нет. Иногда может встретиться startOffset (4 байта LE).
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

        // Фильтр не меняет размер.
        if (input.Length != expectedUnpackSize)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        decoded = input.ToArray();
        BcjSparcDecodeInPlace(decoded.AsSpan(), startOffset);
        return SevenZipFolderDecodeResult.Ok;
      }

      if (IsBcjIa64MethodId(coder.MethodId))
      {
        // BCJ IA64: props обычно нет. Иногда может встретиться startOffset (4 байта LE).
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

        // Фильтр не меняет размер.
        if (input.Length != expectedUnpackSize)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        decoded = input.ToArray();
        BcjIa64DecodeInPlace(decoded.AsSpan(), startOffset);
        return SevenZipFolderDecodeResult.Ok;
      }

      if (IsBcjArm64MethodId(coder.MethodId))
      {
        // BCJ ARM64: props обычно нет, но допускаем startOffset (4 байта LE), как и для других BCJ.
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

        // Фильтр не меняет размер.
        if (input.Length != expectedUnpackSize)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        decoded = input.ToArray();
        BcjArm64DecodeInPlace(decoded.AsSpan(), startOffset);
        return SevenZipFolderDecodeResult.Ok;
      }

      if (IsSingleByteMethodId(coder.MethodId, _methodIdLzma2))
      {
        if (coder.Properties is null || coder.Properties.Length != 1)
          return SevenZipFolderDecodeResult.InvalidData;

        byte lzma2PropertiesByte = coder.Properties[0];

        // В 7z LZMA2 properties — это 1 байт, допустимый диапазон: 0..40.
        if (!SevenZipLzma2Coder.TryDecodeDictionarySize(lzma2PropertiesByte, out uint dictionarySize))
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        if (dictionarySize > int.MaxValue)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.NotSupported;
        }

        Lzma2DecodeResult lzma2Result = Lzma2Decoder.DecodeToArray(
          input: input,
          dictionaryProp: lzma2PropertiesByte,
          output: out decoded,
          bytesConsumed: out int lzma2BytesConsumed);

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

      // LZMA (7z) method id = { 03 01 01 }, properties = 5 bytes:
      // [0] = LZMA property byte (lc/lp/pb)
      // [1..4] = dictionary size (UInt32 LE).
      if (coder.MethodId.Length == 3 &&
          coder.MethodId[0] == 0x03 &&
          coder.MethodId[1] == 0x01 &&
          coder.MethodId[2] == 0x01)
      {
        if (coder.Properties is null || coder.Properties.Length != 5)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        byte lzmaPropsByte = coder.Properties[0];
        if (!LzmaProperties.TryParse(lzmaPropsByte, out LzmaProperties lzmaProps))
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        uint dictU32 = BinaryPrimitives.ReadUInt32LittleEndian(coder.Properties.AsSpan(1, 4));
        if (dictU32 == 0)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.InvalidData;
        }

        if (dictU32 > int.MaxValue)
        {
          decoded = [];
          return SevenZipFolderDecodeResult.NotSupported;
        }

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
  {
    output = [];

    if (outSize < 0)
      return SevenZipFolderDecodeResult.InvalidData;

    if (outSize == 0)
    {
      output = [];
      return SevenZipFolderDecodeResult.Ok;
    }

    // Порт из LZMA SDK (Bcj2.c). Комментарии по-русски оставляем, код максимально “в лоб”.
    // buf0 - основной поток кода (с удалёнными disp32 для части переходов)
    // buf1 - поток адресов для E8
    // buf2 - поток адресов для E9 и Jcc
    // buf3 - range-coded поток флагов (какие переходы “вынесены” в buf1/buf2)

    const int kNumTopBits = 24;
    const uint kTopValue = 1u << kNumTopBits;

    const int kNumBitModelTotalBits = 11;
    const uint kBitModelTotal = 1u << kNumBitModelTotalBits; // 2048

    const int kNumMoveBits = 5;

    static bool IsJcc(byte b0, byte b1) => b0 == 0x0F && (b1 & 0xF0) == 0x80;
    static bool IsJ(byte b0, byte b1) => (b1 & 0xFE) == 0xE8 || IsJcc(b0, b1);

    output = new byte[outSize];

    // prob модели: 256 для E8 (индекс по prevByte) + 2 общих (E9 и Jcc/прочее)
    uint[] p = new uint[256 + 2];
    for (int i = 0; i < p.Length; i++)
      p[i] = kBitModelTotal >> 1;

    int inPos = 0;
    int outPos = 0;

    int pos1 = 0;
    int pos2 = 0;
    int pos3 = 0;

    byte prevByte = 0;

    // Инициализация range decoder: 5 байт из buf3
    if (buf3.Length < 5)
    {
      output = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    uint code = 0;
    for (int i = 0; i < 5; i++)
      code = (code << 8) | buf3[pos3++];

    uint range = 0xFFFF_FFFFu;

    try
    {
      for (; ; )
      {
        int limit = buf0.Length - inPos;
        int outRemain = outSize - outPos;
        if (outRemain < limit)
          limit = outRemain;

        while (limit != 0)
        {
          byte b = buf0[inPos];
          output[outPos++] = b;

          if (IsJ(prevByte, b))
            break;

          inPos++;
          prevByte = b;
          limit--;
        }

        if (limit == 0 || outPos == outSize)
          break;

        // b = байт перехода/второй байт Jcc (он уже записан в output ранее)
        byte b2 = buf0[inPos++];

        int probIndex;
        if (b2 == 0xE8)
          probIndex = prevByte; // 0..255
        else if (b2 == 0xE9)
          probIndex = 256;
        else
          probIndex = 257;

        uint ttt = p[probIndex];
        uint bound = (range >> kNumBitModelTotalBits) * ttt;

        if (code < bound)
        {
          // BIT = 0: disp32 остаётся в buf0 (ничего не подмешиваем)
          range = bound;
          p[probIndex] = ttt + ((kBitModelTotal - ttt) >> kNumMoveBits);
          if (!Bcj2Normalize(buf3, ref pos3, ref range, ref code))
          {
            output = [];
            return SevenZipFolderDecodeResult.InvalidData;
          }
          prevByte = b2;
        }
        else
        {
          // BIT = 1: disp32 берём из buf1/buf2 и конвертируем ABS->REL
          range -= bound;
          code -= bound;
          p[probIndex] = ttt - (ttt >> kNumMoveBits);
          if (!Bcj2Normalize(buf3, ref pos3, ref range, ref code))
          {
            output = [];
            return SevenZipFolderDecodeResult.InvalidData;
          }

          uint abs;
          if (b2 == 0xE8)
          {
            if (pos1 + 4 > buf1.Length)
            {
              output = [];
              return SevenZipFolderDecodeResult.InvalidData;
            }

            abs = ((uint)buf1[pos1] << 24) |
                  ((uint)buf1[pos1 + 1] << 16) |
                  ((uint)buf1[pos1 + 2] << 8) |
                  ((uint)buf1[pos1 + 3]);
            pos1 += 4;
          }
          else
          {
            if (pos2 + 4 > buf2.Length)
            {
              output = [];
              return SevenZipFolderDecodeResult.InvalidData;
            }

            abs = ((uint)buf2[pos2] << 24) |
                  ((uint)buf2[pos2 + 1] << 16) |
                  ((uint)buf2[pos2 + 2] << 8) |
                  ((uint)buf2[pos2 + 3]);
            pos2 += 4;
          }

          uint dest = unchecked(abs - (uint)(outPos + 4));

          output[outPos++] = (byte)dest;
          if (outPos == outSize)
            break;

          output[outPos++] = (byte)(dest >> 8);
          if (outPos == outSize)
            break;

          output[outPos++] = (byte)(dest >> 16);
          if (outPos == outSize)
            break;

          output[outPos++] = prevByte = (byte)(dest >> 24);
        }
      }
    }
    catch (InvalidOperationException)
    {
      output = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (outPos != outSize)
    {
      output = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    return SevenZipFolderDecodeResult.Ok;
  }

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

  private static bool Bcj2Normalize(ReadOnlySpan<byte> rangeStream, ref int pos, ref uint range, ref uint code)
  {
    const uint kTopValue = 1u << 24;

    if (range >= kTopValue)
      return true;

    if ((uint)pos >= (uint)rangeStream.Length)
      return false;

    range <<= 8;
    code = (code << 8) | rangeStream[pos++];
    return true;
  }

  private static bool IsBcj2MethodId(byte[] methodId)
  {
    // BCJ2 обычно 03 03 01 1B, иногда короткий 1B.
    return
      methodId.Length == 1 && methodId[0] == 0x1B ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x01 &&
      methodId[3] == 0x1B;
  }

  private static bool IsSingleByteMethodId(byte[] methodId, byte expected)
      => methodId.Length == 1 && methodId[0] == expected;

  private static bool IsSwap2MethodId(byte[] methodId)
  {
    return methodId.Length == 3
      && methodId[0] == 0x02
      && methodId[1] == 0x03
      && methodId[2] == 0x02;
  }

  private static bool IsSwap4MethodId(byte[] methodId)
  {
    return methodId.Length == 3
      && methodId[0] == 0x02
      && methodId[1] == 0x03
      && methodId[2] == 0x04;
  }

  private static bool IsBcjArmMethodId(byte[] methodId)
  {
    // Methods.txt:
    // 07 - ARM (little-endian)
    // 03 03 05 01 - 7z Branch Codecs / ARM (little-endian)
    return
      methodId.Length == 1 && methodId[0] == 0x07 ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x05 &&
      methodId[3] == 0x01;
  }

  private static bool IsBcjX86MethodId(byte[] methodId)
  {
    // В 7z часто используется "длинный" ID для BCJ: { 03 03 01 03 }.
    // Иногда может встретиться и короткий ID: { 04 }.
    return
      methodId.Length == 1 && methodId[0] == 0x04 ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x01 &&
      methodId[3] == 0x03;
  }

  private static bool IsBcjArmtMethodId(byte[] methodId)
  {
    // Methods.txt:
    // 08 - ARMT (little-endian)
    // 03 03 07 01 - 7z Branch Codecs / ARMT (little-endian)
    return
      (methodId.Length == 1 && methodId[0] == 0x08) ||
      (methodId.Length == 4 &&
       methodId[0] == 0x03 &&
       methodId[1] == 0x03 &&
       methodId[2] == 0x07 &&
       methodId[3] == 0x01);
  }

  private static bool IsBcjPpcMethodId(byte[] methodId)
  {
    // Methods.txt:
    // 05 - PPC (big-endian)
    // 03 03 02 05 - 7z Branch Codecs / PPC (big-endian)
    return
      methodId.Length == 1 && methodId[0] == 0x05 ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x02 &&
      methodId[3] == 0x05;
  }

  private static bool IsBcjSparcMethodId(byte[] methodId)
  {
    // Methods.txt:
    // 09 - SPARC
    // 03 03 08 05 - 7z Branch Codecs / SPARC
    return
      methodId.Length == 1 && methodId[0] == 0x09 ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x08 &&
      methodId[3] == 0x05;
  }

  private static bool IsBcjIa64MethodId(byte[] methodId)
  {
    // Methods.txt:
    // 06 - IA64
    // 03 03 04 01 - 7z Branch Codecs / IA64
    return
      (methodId.Length == 1 && methodId[0] == 0x06) ||
      (methodId.Length == 4 &&
       methodId[0] == 0x03 &&
       methodId[1] == 0x03 &&
       methodId[2] == 0x04 &&
       methodId[3] == 0x01);
  }

  private static bool IsBcjArm64MethodId(byte[] methodId)
  {
    // Methods.txt:
    // 0A - ARM64
    return methodId.Length == 1 && methodId[0] == 0x0A;
  }

  private static void BcjArmDecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Port из LZMA SDK (Bra.c): ARM_Convert(data, size, ip, encoding=0).
    // Обрабатывает только выровненные по 4 байтам инструкции.
    // Патчит BL-инструкции (последний байт == 0xEB), переводя абсолют -> относительный.
    //
    // startOffset — виртуальный offset для ip (обычно 0). Если фильтр вызывается кусками,
    // ip надо накапливать, но у нас сейчас decode целого буфера.

    int size = data.Length & ~3;            // size &= ~(size_t)3
    uint ip = unchecked(startOffset + 4u);  // ip += 4

    for (int i = 0; i + 4 <= size; i += 4)
    {
      if (data[i + 3] != 0xEB)
        continue;

      uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i, 4));

      v <<= 2;
      // В оригинале используется (p - data), где p уже сдвинут на +4.
      // То есть добавляется (i + 4).
      v = unchecked(v - (ip + (uint)(i + 4)));
      v >>= 2;

      v &= 0x00FFFFFF;
      v |= 0xEB000000;

      BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(i, 4), v);
    }
  }

  private static void BcjArmtDecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Порт из LZMA SDK (Bra.c): ARMT_Convert(data, size, ip, encoding=0).
    // Обрабатывает Thumb-2 BL-последовательности.
    if (data.Length < 4)
      return;

    int limit = data.Length - 4;            // size -= 4
    uint ip = unchecked(startOffset + 4u);  // ip += 4

    for (int i = 0; i <= limit; i += 2)
      if ((data[i + 1] & 0xF8) == 0xF0 &&
                 (data[i + 3] & 0xF8) == 0xF8)
      {
        uint src =
          ((data[i + 1] & 0x7u) << 19) |
          ((uint)data[i + 0] << 11) |
          ((data[i + 3] & 0x7u) << 8) |
          data[i + 2];

        src <<= 1;

        uint dest = unchecked(src - (ip + (uint)i));
        dest >>= 1;

        data[i + 1] = (byte)(0xF0 | ((dest >> 19) & 0x7));
        data[i + 0] = (byte)(dest >> 11);
        data[i + 3] = (byte)(0xF8 | ((dest >> 8) & 0x7));
        data[i + 2] = (byte)dest;

        i += 2; // как в оригинале: внутри for делается i += 2
      }
  }

  private static void BcjPpcDecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Port из LZMA SDK (Bra.c): PPC_Convert(data, size, ip, encoding=0).
    // Работает по 4-байтным инструкциям, big-endian.
    if (data.Length < 4)
      return;

    int limit = data.Length - 4;

    for (int i = 0; i <= limit; i += 4)
    {
      // Условие из Bra.c:
      // (data[i] >> 2) == 0x12  => 0x48..0x4B
      // (data[i + 3] & 3) == 1
      if ((data[i] >> 2) != 0x12 || (data[i + 3] & 3) != 1)
        continue;

      uint src =
        ((uint)(data[i + 0] & 3) << 24) |
        ((uint)data[i + 1] << 16) |
        ((uint)data[i + 2] << 8) |
        ((uint)data[i + 3] & 0xFFFFFFFCu);

      uint dest = unchecked(src - (startOffset + (uint)i));

      data[i + 0] = (byte)(0x48 | ((dest >> 24) & 0x3));
      data[i + 1] = (byte)(dest >> 16);
      data[i + 2] = (byte)(dest >> 8);

      byte b3 = data[i + 3];
      b3 &= 0x3;
      b3 |= (byte)dest;
      data[i + 3] = b3;
    }
  }

  private static void BcjX86DecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Port из LZMA SDK (Bra86.c): x86 BCJ decoder.
    // Декодирование: абсолютные адреса -> относительные смещения (для E8/E9).
    //
    // startOffset — виртуальный стартовый оффсет (обычно 0). В 7z чаще всего props нет.
    // В формуле используется ip = startOffset + 5, чтобы соответствовать (pos + 5).

    static bool Test86MSByte(byte b) => b == 0 || b == 0xFF;

    ReadOnlySpan<byte> kMaskToAllowedStatus = [1, 1, 1, 0, 1, 0, 0, 0];
    ReadOnlySpan<byte> kMaskToBitNumber = [0, 1, 2, 2, 3, 3, 3, 3];

    if (data.Length < 5)
      return;

    uint ip = unchecked(startOffset + 5u);

    int bufferPos = 0;
    int prevPos = -1;
    uint prevMask = 0;

    while (true)
    {
      int limit = data.Length - 4;
      int p = bufferPos;

      // Ищем следующий E8/E9 (CALL/JMP near).
      for (; p < limit; p++)
        if ((data[p] & 0xFE) == 0xE8)
          break;

      bufferPos = p;
      if (p >= limit)
        break;

      int distance = bufferPos - prevPos;
      if (distance > 3)
        prevMask = 0;
      else
      {
        prevMask = (prevMask << (distance - 1)) & 0x7;

        if (prevMask != 0)
        {
          byte b = data[bufferPos + 4 - kMaskToBitNumber[(int)prevMask]];

          if (kMaskToAllowedStatus[(int)prevMask] == 0 || Test86MSByte(b))
          {
            prevPos = bufferPos;
            prevMask = ((prevMask << 1) & 0x7) | 1u;
            bufferPos++;
            continue;
          }
        }
      }

      prevPos = bufferPos;

      if (Test86MSByte(data[bufferPos + 4]))
      {
        uint src =
          ((uint)data[bufferPos + 4] << 24) |
          ((uint)data[bufferPos + 3] << 16) |
          ((uint)data[bufferPos + 2] << 8) |
          data[bufferPos + 1];

        uint dest;

        while (true)
        {
          // decode: rel = abs - (ip + pos)
          dest = unchecked(src - (ip + (uint)bufferPos));

          if (prevMask == 0)
            break;

          int bIndex = kMaskToBitNumber[(int)prevMask] * 8;
          byte b = (byte)(dest >> (24 - bIndex));

          if (!Test86MSByte(b))
            break;

          src = dest ^ ((1u << (32 - bIndex)) - 1u);
        }

        data[bufferPos + 4] = (byte)(~(((dest >> 24) & 1) - 1));
        data[bufferPos + 3] = (byte)(dest >> 16);
        data[bufferPos + 2] = (byte)(dest >> 8);
        data[bufferPos + 1] = (byte)dest;

        bufferPos += 5;
      }
      else
      {
        prevMask = ((prevMask << 1) & 0x7) | 1u;
        bufferPos++;
      }
    }
  }

  private static void BcjSparcDecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Порт из LZMA SDK (Bra.c): SPARC_Convert(data, size, ip, encoding=0).
    // Big-endian, обрабатывает только выровненные по 4 байта инструкции.
    int size = data.Length & ~3;

    for (int i = 0; i + 4 <= size; i += 4)
    {
      byte b0 = data[i];
      byte b1 = data[i + 1];

      // Условие из Bra.c (упрощённая проверка ветвления).
      if (!((b0 == 0x40 && (b1 & 0xC0) == 0) || (b0 == 0x7F && b1 >= 0xC0)))
        continue;

      uint v = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i, 4));

      v <<= 2;

      // В Bra.c: ip -= 4; используется ip + (p - data), где p уже сдвинут на +4.
      // Это эквивалентно (startOffset + i).
      v = unchecked(v - (startOffset + (uint)i));

      v &= 0x01FFFFFFu;
      v = unchecked(v - (1u << 24));
      v ^= 0xFF000000u;
      v >>= 2;
      v |= 0x40000000u;

      BinaryPrimitives.WriteUInt32BigEndian(data.Slice(i, 4), v);
    }
  }

  private static void BcjIa64DecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Порт из LZMA SDK (BraIA64.c): IA64_Convert(data, size, ip, encoding=0).
    // Обрабатываются только полные 16-байтные bundle’ы; хвост < 16 остаётся как есть.

    if (data.Length < 16)
      return;

    int lastBundleStart = data.Length - 16;

    for (int i = 0; i <= lastBundleStart; i += 16)
    {
      int m = (int)((0x334B0000u >> (data[i] & 0x1E)) & 3u);
      if (m == 0)
        continue;

      // В оригинале: m++; do { ... } while (++m <= 4);
      for (++m; m <= 4; m++)
      {
        int p = i + m * 5 - 8;

        // if (((p[3] >> m) & 15) == 5 ...
        if (((data[p + 3] >> m) & 0xF) != 5)
          continue;

        // && (((p[-1] | ((UInt32)p[0] << 8)) >> m) & 0x70) == 0)
        uint t = (uint)(data[p - 1] | (data[p] << 8));
        if (((t >> m) & 0x70u) != 0)
          continue;

        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p, 4));

        uint v = raw >> m;
        v = (v & 0xFFFFFu) | ((v & (1u << 23)) >> 3);
        v <<= 4;

        uint add = unchecked(startOffset + (uint)i);
        v = unchecked(v - add);

        v >>= 4;
        v &= 0x1FFFFFu;
        v = unchecked(v + 0x700000u);
        v &= 0x8FFFFFu;

        raw &= ~(0x8FFFFFu << m);
        raw |= (v << m);

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(p, 4), raw);
      }
    }
  }

  private static void BcjArm64DecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Port из LZMA SDK (Bra.c): z7_BranchConv_ARM64_Dec.
    // Обрабатывает только выровненные по 4 байтам инструкции.
    // startOffset — виртуальный offset (обычно 0). Если фильтр вызван кусками, его нужно накапливать.

    int size = data.Length & ~3;

    const uint flag = 1u << (24 - 4);            // 1 << 20
    const uint mask = (1u << 24) - (flag << 1);  // 0x00E00000

    for (int i = 0; i + 4 <= size; i += 4)
    {
      uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i, 4));
      uint pc = unchecked(startOffset + (uint)i);

      // BL imm26 (0x94xxxxxx)
      if (((v - 0x94000000u) & 0xFC000000u) == 0u)
      {
        uint c = pc >> 2;
        v = unchecked(v - c);
        v &= 0x03FFFFFFu;
        v |= 0x94000000u;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(i, 4), v);
        continue;
      }

      // ADRP-подобный паттерн (см. Bra.c)
      v = unchecked(v - 0x90000000u);
      if ((v & 0x9F000000u) != 0u)
        continue;

      v = unchecked(v + flag);
      if ((v & mask) != 0u)
        continue;

      uint z = (v & 0xFFFFFFE0u) | (v >> 26);

      uint c2 = (pc >> (12 - 3)) & ~7u; // (pc >> 9) & ~7
      z = unchecked(z - c2);

      uint outV = v & 0x1Fu;
      outV |= 0x90000000u;
      outV |= z << 26;
      outV |= 0x00FFFFE0u & unchecked((z & ((flag << 1) - 1)) - flag);

      BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(i, 4), outV);
    }
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
