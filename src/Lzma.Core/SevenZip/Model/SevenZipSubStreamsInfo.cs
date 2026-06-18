namespace Lzma.Core.SevenZip;

/// <summary>
/// <para>SubStreamsInfo из заголовка 7z.</para>
/// <para>
/// По смыслу это разбиение распакованных данных "папки" (Folder)
/// на отдельные "unpack streams". В типичных архивах это соответствует файлам,
/// которые были упакованы одной папкой.
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

  /// <summary>
  /// Если в SubStreamsInfo присутствует kCRC, здесь лежит флаг "CRC определён" для каждого unpack-stream.
  /// Индекс: [folderIndex][streamIndex].
  /// </summary>
  public bool[][]? UnpackCrcDefinedPerFolder { get; }

  /// <summary>
  /// CRC32 для unpack-stream'ов (если присутствует kCRC в SubStreamsInfo).
  /// Индекс: [folderIndex][streamIndex].
  ///
  /// Если CRC не определён (UnpackCrcDefinedPerFolder=false), значение может быть 0.
  /// </summary>
  public uint[][]? UnpackCrcPerFolder { get; }

  public bool HasUnpackCrc => UnpackCrcDefinedPerFolder is not null;

  public SevenZipSubStreamsInfo(
    ulong[] numUnpackStreamsPerFolder,
    ulong[][] unpackSizesPerFolder,
    bool[][]? unpackCrcDefinedPerFolder = null,
    uint[][]? unpackCrcPerFolder = null)
  {
    NumUnpackStreamsPerFolder = numUnpackStreamsPerFolder ?? [];
    UnpackSizesPerFolder = unpackSizesPerFolder ?? [];

    UnpackCrcDefinedPerFolder = unpackCrcDefinedPerFolder;
    UnpackCrcPerFolder = unpackCrcPerFolder;

    if (NumUnpackStreamsPerFolder.Length != UnpackSizesPerFolder.Length)
      throw new ArgumentException("Размеры массивов не совпадают.", nameof(unpackSizesPerFolder));

    if (UnpackCrcDefinedPerFolder is null != UnpackCrcPerFolder is null)
      throw new ArgumentException("UnpackCrcDefinedPerFolder и UnpackCrcPerFolder должны быть оба null, либо оба не null.");

    if (UnpackCrcDefinedPerFolder is not null)
      if (UnpackCrcDefinedPerFolder.Length != NumUnpackStreamsPerFolder.Length || UnpackCrcPerFolder!.Length != NumUnpackStreamsPerFolder.Length)
        throw new ArgumentException("Размеры массивов CRC не совпадают.", nameof(unpackCrcPerFolder));

    for (int i = 0; i < NumUnpackStreamsPerFolder.Length; i++)
    {
      ulong n = NumUnpackStreamsPerFolder[i];
      if (n == 0)
        throw new ArgumentOutOfRangeException(nameof(numUnpackStreamsPerFolder), "Количество потоков не может быть 0.");

      ulong[] sizes = UnpackSizesPerFolder[i] ?? [];
      if ((ulong)sizes.Length != n)
        throw new ArgumentException("Количество распакованных потоков не совпадает с количеством размеров.", nameof(unpackSizesPerFolder));

      if (UnpackCrcDefinedPerFolder is not null)
      {
        bool[] def = UnpackCrcDefinedPerFolder[i] ?? [];
        uint[] crc = UnpackCrcPerFolder![i] ?? [];

        if ((ulong)def.Length != n || (ulong)crc.Length != n)
          throw new ArgumentException("Количество CRC не совпадает с количеством unpack-stream.", nameof(unpackCrcPerFolder));
      }
    }
  }
}
