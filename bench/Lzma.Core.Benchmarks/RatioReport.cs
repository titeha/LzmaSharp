using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.Ppmd;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Быстрый отчёт о степени сжатия: сравнивает наши LZMA2- и PPMd-энкодеры на одних данных.
/// Степень сжатия — главная метрика качества. Запуск:
/// <c>dotnet run -c Release --project bench/Lzma.Core.Benchmarks -- ratio [sizeMiB]</c>.
/// </summary>
internal static class RatioReport
{
  private const int Dict = 1 << 16;
  private const int PpmdOrder = 6;
  private const uint PpmdMem = 16u << 20; // 16 МБ — как по умолчанию у 7-Zip

  public static void Run(int sizeMiB)
  {
    int size = sizeMiB * 1024 * 1024;
    Report("text-like", BenchData.MakeTextLike(size));
    Report("periodic", BenchData.MakePeriodic(size));
  }

  private static void Report(string name, byte[] raw)
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    byte[] lzma2 = Lzma2LzmaEncoder.Encode(raw, props, Dict);

    Ppmd7Encoder.Encode(raw, PpmdOrder, PpmdMem, out byte[] ppmd);

    Console.WriteLine($"{name}, {raw.Length / (1024 * 1024)} MiB (исходный {raw.Length:N0} байт):");
    Print("LZMA2", raw.Length, lzma2.Length);
    Print("PPMd ", raw.Length, ppmd.Length);
    Console.WriteLine();
  }

  private static void Print(string codec, int rawLen, int encLen)
  {
    double ratio = (double)rawLen / encLen;
    double percent = 100.0 * encLen / rawLen;
    Console.WriteLine($"  {codec}: {encLen,12:N0} байт  {ratio,8:F3}x  ({percent:F2}%)");
  }
}
