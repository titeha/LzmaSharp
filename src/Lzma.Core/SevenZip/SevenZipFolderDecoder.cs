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

    // На этапе 1 поддерживаем:
    // - ровно один packed stream;
    // - либо 1 coder (без BindPairs),
    // - либо 2 coders (цепочка 1->1, один BindPair).
    if (folder.PackedStreamIndices.Length != 1)
      return SevenZipFolderDecodeResult.NotSupported;

    if (folder.Coders.Length == 1)
    {
      if (folder.BindPairs.Length != 0)
        return SevenZipFolderDecodeResult.NotSupported;
    }
    else if (folder.Coders.Length == 2)
    {
      if (folder.BindPairs.Length != 1)
        return SevenZipFolderDecodeResult.NotSupported;
    }
    else
    {
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

    SevenZipFolderDecodeResult DecodeOneCoder(
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

    if (folder.Coders.Length == 1)
    {
      if (folderUnpackSizes.Length != 1)
        return SevenZipFolderDecodeResult.NotSupported;

      ulong unpackSizeU64 = folderUnpackSizes[0];
      if (unpackSizeU64 > int.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      int expectedUnpackSize = (int)unpackSizeU64;
      return DecodeOneCoder(folder.Coders[0], packStream, expectedUnpackSize, out output);
    }

    if (folder.Coders.Length == 2)
    {
      if (folderUnpackSizes.Length != 2)
        return SevenZipFolderDecodeResult.NotSupported;

      SevenZipCoderInfo c0 = folder.Coders[0];
      SevenZipCoderInfo c1 = folder.Coders[1];

      if (c0.NumInStreams != 1 || c0.NumOutStreams != 1 ||
          c1.NumInStreams != 1 || c1.NumOutStreams != 1)
        return SevenZipFolderDecodeResult.NotSupported;

      if (folder.NumInStreams != 2 || folder.NumOutStreams != 2)
        return SevenZipFolderDecodeResult.InvalidData;

      SevenZipBindPair bp = folder.BindPairs[0];

      if (bp.InIndex > 1 || bp.OutIndex > 1)
        return SevenZipFolderDecodeResult.InvalidData;

      if (bp.InIndex == bp.OutIndex)
        return SevenZipFolderDecodeResult.InvalidData;

      int consumerIndex = (int)bp.InIndex;
      int producerIndex = (int)bp.OutIndex;

      ulong producerSizeU64 = folderUnpackSizes[producerIndex];
      ulong consumerSizeU64 = folderUnpackSizes[consumerIndex];

      if (producerSizeU64 > int.MaxValue || consumerSizeU64 > int.MaxValue)
        return SevenZipFolderDecodeResult.NotSupported;

      int producerExpectedSize = (int)producerSizeU64;
      int consumerExpectedSize = (int)consumerSizeU64;

      SevenZipFolderDecodeResult r0 = DecodeOneCoder(
        coder: folder.Coders[producerIndex],
        input: packStream,
        expectedUnpackSize: producerExpectedSize,
        decoded: out byte[] intermediate);

      if (r0 != SevenZipFolderDecodeResult.Ok)
      {
        output = [];
        return r0;
      }

      SevenZipFolderDecodeResult r1 = DecodeOneCoder(
        coder: folder.Coders[consumerIndex],
        input: intermediate,
        expectedUnpackSize: consumerExpectedSize,
        decoded: out output);

      if (r1 != SevenZipFolderDecodeResult.Ok)
      {
        output = [];
        return r1;
      }

      return SevenZipFolderDecodeResult.Ok;
    }

    return SevenZipFolderDecodeResult.NotSupported;
  }

  private static bool IsSingleByteMethodId(byte[] methodId, byte expected)
      => methodId.Length == 1 && methodId[0] == expected;

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
