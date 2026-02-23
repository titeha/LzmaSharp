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

    // Размеры распаковки в 7z лежат не в самом Folder, а отдельным массивом в UnpackInfo.
    if ((uint)folderIndex >= (uint)unpackInfo.FolderUnpackSizes.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    ulong[]? folderUnpackSizes = unpackInfo.FolderUnpackSizes[folderIndex];
    if (folderUnpackSizes is null || folderUnpackSizes.Length == 0)
      return SevenZipFolderDecodeResult.InvalidData;

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
      for (m = m + 1; m <= 4; m++)
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

        raw &= ~((0x8FFFFFu) << m);
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
