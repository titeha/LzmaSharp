using BenchmarkDotNet.Attributes;

using Lzma.Core.Ppmd;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Базовые замеры собственного PPMd (var.H / PPMd7): encode и decode.
/// Параметры как по умолчанию у 7-Zip: order 6, память 16 МБ.
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

  [Benchmark]
  public byte[] Encode()
  {
    Ppmd7Encoder.Encode(_raw, Order, Mem, out byte[] output);
    return output;
  }

  [Benchmark]
  public int Decode()
  {
    byte[] output = new byte[_raw.Length];
    Ppmd7Decoder.Decode(_encoded, Order, Mem, output);
    return output.Length;
  }
}
