using System.Reflection;

using BenchmarkDotNet.Running;

using Lzma.Core.Benchmarks;

// Режим отчёта о степени сжатия (вне BenchmarkDotNet):
//   dotnet run -c Release --project bench/Lzma.Core.Benchmarks -- ratio [sizeMiB]
if (args.Length >= 1 && args[0] == "ratio")
{
  int sizeMiB = args.Length >= 2 && int.TryParse(args[1], out int s) ? s : 8;
  RatioReport.Run(sizeMiB);
  return;
}

// Иначе — запуск бенчмарков через BenchmarkSwitcher (фильтр --filter *Lzma2* и т.п.).
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
