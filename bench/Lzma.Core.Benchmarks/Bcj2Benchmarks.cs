using BenchmarkDotNet.Attributes;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Замеры BCJ2-кодека (x86): encode (разбивка на 4 потока, детект ветвлений + range-кодер)
/// и decode (слияние 4 потоков обратно). Раньше BCJ/BCJ2 в бенчмарках не мерили.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class Bcj2Benchmarks
{
  [Params(1, 16)]
  public int SizeMiB;

  private byte[] _raw = [];
  private SevenZipBcj2Streams _streams;

  [GlobalSetup]
  public void Setup()
  {
    _raw = BenchData.MakeX86Like(SizeMiB * 1024 * 1024);
    _streams = SevenZipBcj2Encoder.Encode(_raw);
  }

  [Benchmark]
  public SevenZipBcj2Streams Encode() => SevenZipBcj2Encoder.Encode(_raw);

  [Benchmark]
  public int Decode()
  {
    SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        _streams.Main, _streams.Call, _streams.Jump, _streams.Control, _raw.Length, out byte[] output);
    return output.Length;
  }
}
