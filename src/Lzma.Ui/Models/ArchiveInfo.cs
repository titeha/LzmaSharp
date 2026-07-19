using System;

namespace Lzma.Ui.Models;

/// <summary>
/// Сводка по открытому архиву для окна «Информация»: имена/счётчики/размеры + производные метрики
/// (коэффициент сжатия, экономия, доля от оригинала) с готовыми к показу строками.
/// </summary>
public sealed record ArchiveInfo(
    string Name,
    int FileCount,
    int FolderCount,
    long UncompressedSize,
    long CompressedSize)
{
  private bool HasRatio => UncompressedSize > 0 && CompressedSize > 0;

  /// <summary>Коэффициент сжатия (исходный / архив).</summary>
  public double Ratio => HasRatio ? (double)UncompressedSize / CompressedSize : 0;

  /// <summary>Доля размера архива от оригинала, % (меньше — лучше сжатие). 0..100.</summary>
  public double PercentOfOriginal => HasRatio ? Math.Clamp(100.0 * CompressedSize / UncompressedSize, 0, 100) : 0;

  public string RatioDisplay => HasRatio ? $"{Ratio:0.0}×" : "—";

  public string SavedDisplay
  {
    get
    {
      if (!HasRatio)
        return "—";

      double saved = 100.0 * (1 - (double)CompressedSize / UncompressedSize);
      return $"{saved:0.#}%"; // отрицательно, если архив крупнее оригинала (несжимаемое/мелочь)
    }
  }

  public string UncompressedDisplay => ByteSizeFormat.Format(UncompressedSize);
  public string CompressedDisplay => ByteSizeFormat.Format(CompressedSize);
  public string FileCountDisplay => FileCount.ToString();
  public string FolderCountDisplay => FolderCount.ToString();

  /// <summary>«Архив занимает N% от оригинала» — подпись к полосе.</summary>
  public string PercentOfOriginalDisplay => HasRatio ? $"{PercentOfOriginal:0.#}%" : "—";
}
