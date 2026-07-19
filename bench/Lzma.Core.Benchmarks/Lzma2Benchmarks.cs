using System.IO;

using BenchmarkDotNet.Attributes;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Замеры ядра LZMA2: одноразовое сжатие (match finder + range coder), распаковка, а также ПОТОКОВЫЕ
/// пути (для файлов &gt; 2 ГиБ): однопоточный кольцевой (EncodeStreaming) и блочно-ПАРАЛЛЕЛЬНЫЙ
/// (EncodeParallelToStream, все ядра). Параллельный показывает ускорение на много-ядре ценой сброса
/// словаря на границе блока (чуть хуже сжатие). Потоковые с реалистичным словарём 4 МиБ.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class Lzma2Benchmarks
{
  // Словарь = размеру чанка (64 КБ) для одноразового: MVP-энкодер независимо сжимает чанки ≤ 64 КБ.
  private const int Dict = 1 << 16;

  // Реалистичный словарь для потоковых путей (как StreamingDictionarySize в UI).
  private const int StreamDict = 1 << 22;

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

  [Benchmark(Baseline = true)]
  public byte[] Encode() => Lzma2LzmaEncoder.Encode(_raw, Props, Dict);

  [Benchmark(Description = "Encode streaming (single-thread ring)")]
  public long EncodeStreaming()
  {
    using var input = new MemoryStream(_raw);
    using var output = new MemoryStream(_raw.Length / 2 + 16);
    return Lzma2LzmaEncoder.EncodeStreaming(input, _raw.Length, Props, StreamDict, output);
  }

  [Benchmark(Description = "Encode parallel (all cores)")]
  public long EncodeParallel()
  {
    using var input = new MemoryStream(_raw);
    using var output = new MemoryStream(_raw.Length / 2 + 16);
    return Lzma2LzmaEncoder.EncodeParallelToStream(input, _raw.Length, Props, StreamDict, output, out _);
  }

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
