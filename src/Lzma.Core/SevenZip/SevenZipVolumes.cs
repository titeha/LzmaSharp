using System.Globalization;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Разбиение готового 7z-архива на тома (<c>archive.7z.001</c>, <c>.002</c>, …) и обратная
/// сборка. Это побайтовая нарезка одного потока — формат .7z не затрагивается, тома склеиваются
/// в исходный архив без какой-либо обработки содержимого.
/// </summary>
public static class SevenZipVolumes
{
  /// <summary>Минимальное число цифр в суффиксе тома (как у 7-Zip: <c>.001</c>).</summary>
  public const int MinVolumeNameDigits = 3;

  /// <summary>
  /// Разбивает архив на тома по <paramref name="volumeSize"/> байт. Последний том может быть
  /// меньше. Пустой вход даёт пустой набор томов.
  /// </summary>
  /// <exception cref="ArgumentNullException">Если <paramref name="archive"/> равен null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="volumeSize"/> не положителен.</exception>
  public static byte[][] Split(byte[] archive, long volumeSize)
  {
    ArgumentNullException.ThrowIfNull(archive);

    if (volumeSize <= 0)
      throw new ArgumentOutOfRangeException(nameof(volumeSize), volumeSize, "Размер тома должен быть положительным.");

    if (archive.Length == 0)
      return [];

    int count = (int)((archive.Length + volumeSize - 1) / volumeSize);
    var volumes = new byte[count][];

    int offset = 0;

    for (int i = 0; i < count; i++)
    {
      int size = (int)Math.Min(volumeSize, archive.Length - offset);
      volumes[i] = archive.AsSpan(offset, size).ToArray();
      offset += size;
    }

    return volumes;
  }

  /// <summary>
  /// Склеивает тома (в заданном порядке) обратно в один архив.
  /// </summary>
  /// <exception cref="ArgumentNullException">Если <paramref name="volumes"/> или любой том равен null.</exception>
  /// <exception cref="ArgumentOutOfRangeException">Если суммарный размер превышает <see cref="int.MaxValue"/>.</exception>
  public static byte[] Join(IReadOnlyList<byte[]> volumes)
  {
    ArgumentNullException.ThrowIfNull(volumes);

    long total = 0;

    for (int i = 0; i < volumes.Count; i++)
    {
      ArgumentNullException.ThrowIfNull(volumes[i]);
      total += volumes[i].Length;
    }

    if (total > int.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(volumes), total, "Суммарный размер томов превышает 2 ГиБ.");

    byte[] result = new byte[total];
    int offset = 0;

    for (int i = 0; i < volumes.Count; i++)
    {
      volumes[i].CopyTo(result, offset);
      offset += volumes[i].Length;
    }

    return result;
  }

  /// <summary>
  /// Имя файла тома: <paramref name="baseName"/> + точка + порядковый номер (1-based),
  /// дополненный нулями. Ширина суффикса — не меньше <see cref="MinVolumeNameDigits"/> и
  /// достаточная для <paramref name="volumeCount"/>.
  /// </summary>
  /// <example><c>VolumeFileName("archive.7z", 0, 5)</c> → <c>archive.7z.001</c>.</example>
  /// <exception cref="ArgumentException">Если <paramref name="baseName"/> пуст.</exception>
  /// <exception cref="ArgumentOutOfRangeException">Если индекс вне диапазона или количество не положительно.</exception>
  public static string VolumeFileName(string baseName, int index, int volumeCount)
  {
    if (string.IsNullOrEmpty(baseName))
      throw new ArgumentException("Базовое имя не должно быть пустым.", nameof(baseName));

    if (volumeCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(volumeCount), volumeCount, "Количество томов должно быть положительным.");

    if (index < 0 || index >= volumeCount)
      throw new ArgumentOutOfRangeException(nameof(index), index, "Индекс тома вне диапазона.");

    int width = Math.Max(MinVolumeNameDigits, volumeCount.ToString(CultureInfo.InvariantCulture).Length);
    string number = (index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

    return baseName + "." + number;
  }

  /// <summary>
  /// Разбирает имя файла тома: при успехе возвращает базовое имя и 0-based индекс. Том — это
  /// имя, оканчивающееся на точку и не менее <see cref="MinVolumeNameDigits"/> цифр (номер ≥ 1).
  /// </summary>
  public static bool TryParseVolumeName(string fileName, out string baseName, out int index)
  {
    baseName = string.Empty;
    index = -1;

    if (string.IsNullOrEmpty(fileName))
      return false;

    int dot = fileName.LastIndexOf('.');

    if (dot <= 0 || dot == fileName.Length - 1)
      return false;

    string suffix = fileName[(dot + 1)..];

    if (suffix.Length < MinVolumeNameDigits)
      return false;

    for (int i = 0; i < suffix.Length; i++)
      if (suffix[i] is < '0' or > '9')
        return false;

    if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1)
      return false;

    baseName = fileName[..dot];
    index = number - 1;
    return true;
  }
}
