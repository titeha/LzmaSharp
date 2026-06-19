using BenchmarkDotNet.Attributes;

using Lzma.Core.Deflate;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Базовые замеры собственного Deflate (RFC 1951): encode (LZ77 хеш-цепочки + Huffman)
/// и decode (inflate). Отправная точка перед оптимизацией.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class DeflateBenchmarks
{
  [Params(1, 16)]
  public int SizeMiB;

  private byte[] _raw = [];
  private byte[] _encoded = [];

  [GlobalSetup]
  public void Setup()
  {
    _raw = BenchData.MakeTextLike(SizeMiB * 1024 * 1024);
    _encoded = DeflateEncoder.Encode(_raw);
  }

  [Benchmark]
  public byte[] Encode() => DeflateEncoder.Encode(_raw);

  [Benchmark]
  public int Decode()
  {
    byte[] output = new byte[_raw.Length];
    DeflateDecoder.Decode(_encoded, output, out _, out int written);
    return written;
  }
}
