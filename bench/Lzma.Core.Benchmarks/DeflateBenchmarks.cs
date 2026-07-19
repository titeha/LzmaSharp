using System.IO;

using BenchmarkDotNet.Attributes;

using Lzma.Core.Deflate;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Замеры собственного Deflate (RFC 1951): encode/decode одноразовые (весь буфер в памяти) и ПОТОКОВЫЕ
/// (вход/выход через Stream — путь для членов &gt; 2 ГиБ). Сравнение показывает накладные потокового
/// пути (StreamBitWriter / StreamInflater) относительно одноразового.
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

  [Benchmark(Baseline = true)]
  public byte[] Encode() => DeflateEncoder.Encode(_raw);

  [Benchmark]
  public long EncodeStream()
  {
    using var input = new MemoryStream(_raw);
    using var output = new MemoryStream(_raw.Length / 2 + 16);
    DeflateEncoder.Encode(input, _raw.Length, output);
    return output.Length;
  }

  [Benchmark]
  public int Decode()
  {
    byte[] output = new byte[_raw.Length];
    DeflateDecoder.Decode(_encoded, output, out _, out int written);
    return written;
  }

  [Benchmark]
  public long DecodeStream()
  {
    using var input = new MemoryStream(_encoded);
    using var output = new MemoryStream(_raw.Length);
    DeflateDecoder.Decode(input, _encoded.Length, output, deflate64: false, out long written);
    return written;
  }
}
