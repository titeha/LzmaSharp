using BenchmarkDotNet.Attributes;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Базовые замеры ядра LZMA2: реальное сжатие (match finder + range coder) и
/// распаковка на 1 и 64 МиБ. Это отправная точка перед оптимизацией — без цифр
/// оптимизация превращается в угадайку.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class Lzma2Benchmarks
{
  // Словарь = размеру чанка (64 КБ): MVP-энкодер независимо сжимает чанки ≤ 64 КБ
  // со сбросом словаря, поэтому больший словарь сжатию не помогает.
  private const int Dict = 1 << 16;

  private static readonly LzmaProperties Props = new(Lc: 3, Lp: 0, Pb: 2);

  [Params(1, 64)]
  public int SizeMiB;

  private byte[] _raw = [];
  private byte[] _encoded = [];

  [GlobalSetup]
  public void Setup()
  {
    _raw = BenchData.MakeTextLike(SizeMiB * 1024 * 1024);
    _encoded = Lzma2LzmaEncoder.Encode(_raw, Props, Dict);
  }

  [Benchmark]
  public byte[] Encode() => Lzma2LzmaEncoder.Encode(_raw, Props, Dict);

  [Benchmark]
  public byte[] Decode()
  {
    var decoder = new Lzma2IncrementalDecoder(progress: null, dictionarySize: Dict);
    byte[] output = new byte[_raw.Length];

    int inPos = 0;
    int outPos = 0;

    while (true)
    {
      Lzma2DecodeResult result = decoder.Decode(
          _encoded.AsSpan(inPos),
          output.AsSpan(outPos),
          out int consumed,
          out int written);

      inPos += consumed;
      outPos += written;

      if (result == Lzma2DecodeResult.Finished)
        return output;

      if (result is Lzma2DecodeResult.InvalidData or Lzma2DecodeResult.NotSupported)
        throw new InvalidOperationException($"Неожиданный результат: {result}.");

      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся.");
    }
  }
}
