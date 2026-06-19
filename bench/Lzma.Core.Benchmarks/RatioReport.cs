using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Быстрый отчёт о степени сжатия LZMA2-энкодера на тех же данных, что и бенчмарки.
/// Нужен для работы над парсингом (lazy/optimal): степень сжатия — главная метрика качества.
/// Запуск: <c>dotnet run -c Release --project bench/Lzma.Core.Benchmarks -- ratio [sizeMiB]</c>.
/// </summary>
internal static class RatioReport
{
  private const int Dict = 1 << 16;

  public static void Run(int sizeMiB)
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    byte[] raw = BenchData.MakeTextLike(sizeMiB * 1024 * 1024);

    byte[] encoded = Lzma2LzmaEncoder.Encode(raw, props, Dict);

    double ratio = (double)raw.Length / encoded.Length;
    double percent = 100.0 * encoded.Length / raw.Length;

    Console.WriteLine($"LZMA2 ratio report (text-like, {sizeMiB} MiB):");
    Console.WriteLine($"  Исходный:  {raw.Length:N0} байт");
    Console.WriteLine($"  Сжатый:    {encoded.Length:N0} байт");
    Console.WriteLine($"  Коэффициент: {ratio:F3}x  ({percent:F2}% от исходного)");
  }
}
