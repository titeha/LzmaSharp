using System.Reflection;

using BenchmarkDotNet.Running;

using Lzma.Core.Benchmarks;
using Lzma.Core.Deflate;

// Режим отчёта о степени сжатия (вне BenchmarkDotNet):
//   dotnet run -c Release --project bench/Lzma.Core.Benchmarks -- ratio [sizeMiB]
if (args.Length >= 1 && args[0] == "ratio")
{
  int sizeMiB = args.Length >= 2 && int.TryParse(args[1], out int s) ? s : 8;
  RatioReport.Run(sizeMiB);
  return;
}

// Проверка round-trip Deflate на больших данных:
//   dotnet run -c Release --project bench/Lzma.Core.Benchmarks -- verify-deflate [sizeMiB]
if (args.Length >= 1 && args[0] == "verify-deflate")
{
  int sizeMiB = args.Length >= 2 && int.TryParse(args[1], out int s) ? s : 16;
  byte[] raw = BenchData.MakeTextLike(sizeMiB * 1024 * 1024);
  byte[] encoded = DeflateEncoder.Encode(raw);
  byte[] output = new byte[raw.Length];
  DeflateDecodeResult result = DeflateDecoder.Decode(encoded, output, out int consumed, out int written);
  bool equal = written == raw.Length && output.AsSpan().SequenceEqual(raw);
  Console.WriteLine($"verify-deflate {sizeMiB} MiB: result={result}, encoded={encoded.Length:N0}, " +
      $"consumed={consumed:N0}/{encoded.Length:N0}, written={written:N0}/{raw.Length:N0}, equal={equal}");
  return;
}

// Иначе — запуск бенчмарков через BenchmarkSwitcher (фильтр --filter *Lzma2* и т.п.).
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
