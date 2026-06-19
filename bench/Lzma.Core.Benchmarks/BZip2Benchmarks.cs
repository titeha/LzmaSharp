using BenchmarkDotNet.Attributes;

using Lzma.Core.BZip2;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Базовые замеры собственного BZip2: encode (RLE1 → BWT → MTF+RLE2 → Huffman) и decode.
/// Подозреваемое узкое место encode — BWT (prefix-doubling сортировка ротаций).
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class BZip2Benchmarks
{
  [Params(1, 8)]
  public int SizeMiB;

  private byte[] _raw = [];
  private byte[] _encoded = [];

  [GlobalSetup]
  public void Setup()
  {
    _raw = BenchData.MakeTextLike(SizeMiB * 1024 * 1024);
    _encoded = BZip2Encoder.Encode(_raw);
  }

  [Benchmark]
  public byte[] Encode() => BZip2Encoder.Encode(_raw);

  [Benchmark]
  public byte[] Decode()
  {
    BZip2Decoder.Decode(_encoded, out byte[] output);
    return output;
  }
}
