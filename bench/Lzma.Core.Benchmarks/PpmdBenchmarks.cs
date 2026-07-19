using System.IO;

using BenchmarkDotNet.Attributes;

using Lzma.Core.Ppmd;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Замеры собственного PPMd (var.H / PPMd7): encode одноразовый и ПОТОКОВЫЙ (вход/выход через Stream —
/// путь для членов &gt; 2 ГиБ) + decode. Параметры как по умолчанию у 7-Zip: order 6, память 16 МБ.
/// Потоковый выход отличается только приёмником (EmitByte-буфер) — сравнение показывает его накладные.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class PpmdBenchmarks
{
  private const int Order = 6;
  private const uint Mem = 16u << 20;

  [Params(1, 8)]
  public int SizeMiB;

  private byte[] _raw = [];
  private byte[] _encoded = [];

  [GlobalSetup]
  public void Setup()
  {
    _raw = BenchData.MakeTextLike(SizeMiB * 1024 * 1024);
    Ppmd7Encoder.Encode(_raw, Order, Mem, out _encoded);
  }

  [Benchmark(Baseline = true)]
  public byte[] Encode()
  {
    Ppmd7Encoder.Encode(_raw, Order, Mem, out byte[] output);
    return output;
  }

  [Benchmark]
  public long EncodeStream()
  {
    using var input = new MemoryStream(_raw);
    using var output = new MemoryStream(_raw.Length / 2 + 16);
    Ppmd7Encoder.Encode(input, _raw.Length, Order, Mem, output, out long written);
    return written;
  }

  [Benchmark]
  public int Decode()
  {
    byte[] output = new byte[_raw.Length];
    Ppmd7Decoder.Decode(_encoded, Order, Mem, output);
    return output.Length;
  }
}
