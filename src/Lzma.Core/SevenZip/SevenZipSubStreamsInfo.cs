namespace Lzma.Core.SevenZip;

/// <summary>
/// <para>SubStreamsInfo из заголовка 7z.</para>
/// <para>
/// По смыслу это разбиение распакованных данных "папки" (Folder) на отдельные "unpack streams".
/// В типичных архивах это соответствует файлам, которые были упакованы одной папкой.
/// </para>
/// </summary>
public sealed class SevenZipSubStreamsInfo
{
  public ulong[] NumUnpackStreamsPerFolder { get; }

  /// <summary>
  /// Размеры распакованных потоков для каждой папки.
  /// Индекс: [folderIndex][streamIndex].
  /// </summary>
  public ulong[][] UnpackSizesPerFolder { get; }

  public SevenZipSubStreamsInfo(ulong[] numUnpackStreamsPerFolder, ulong[][] unpackSizesPerFolder)
  {
    NumUnpackStreamsPerFolder = numUnpackStreamsPerFolder ?? [];
    UnpackSizesPerFolder = unpackSizesPerFolder ?? [];

    if (NumUnpackStreamsPerFolder.Length != UnpackSizesPerFolder.Length)
      throw new ArgumentException("Размеры массивов не совпадают.", nameof(unpackSizesPerFolder));

    for (int i = 0; i < NumUnpackStreamsPerFolder.Length; i++)
    {
      ulong n = NumUnpackStreamsPerFolder[i];
      ulong[] sizes = UnpackSizesPerFolder[i] ?? [];

      if ((ulong)sizes.Length != n)
        throw new ArgumentException("Количество распакованных потоков не совпадает с количеством размеров.", nameof(unpackSizesPerFolder));

      if (n == 0)
        throw new ArgumentOutOfRangeException(nameof(numUnpackStreamsPerFolder), "Количество потоков не может быть 0.");
    }
  }
}
