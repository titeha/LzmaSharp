using System;
using System.Collections.Generic;
using System.IO;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Поток записи, разбивающий вывод на тома фиксированного размера <c>base.001</c>, <c>base.002</c>, …
/// (как многотомный 7z: это чистая побайтовая нарезка единого архива, без пер-томовых заголовков).
/// Передаётся как <c>output</c> в существующие потоковые writer-ы без их изменений: поддерживает
/// последовательную запись сквозь границы томов и <see cref="Seek"/> назад (для патча сигнатуры в
/// первом томе). Открытым держит только ОДИН том за раз — не течёт хэндлами на больших архивах.
/// </summary>
public sealed class VolumeSpanningWriteStream : Stream
{
  private readonly string _basePath;
  private readonly long _volumeSize;
  private readonly List<long> _volumeLengths = [];

  private long _position;
  private long _length;
  private int _currentIndex = -1;
  private int _maxCreatedIndex = -1;
  private FileStream? _current;

  /// <summary>Создаёт многотомный поток: тома пишутся как <paramref name="basePath"/>.001, .002, …</summary>
  /// <param name="basePath">Базовый путь (напр. <c>C:\out.7z</c>); тома получают суффикс <c>.NNN</c>.</param>
  /// <param name="volumeSize">Размер тома в байтах (&gt; 0). Последний том может быть меньше.</param>
  public VolumeSpanningWriteStream(string basePath, long volumeSize)
  {
    ArgumentNullException.ThrowIfNull(basePath);
    if (volumeSize <= 0)
      throw new ArgumentOutOfRangeException(nameof(volumeSize));

    _basePath = basePath;
    _volumeSize = volumeSize;
    OpenVolume(0);
  }

  /// <summary>Имя тома по индексу (0 → <c>.001</c>). Минимум 3 цифры, при переполнении — больше.</summary>
  public static string VolumePath(string basePath, int index) => $"{basePath}.{index + 1:D3}";

  /// <summary>Число созданных томов.</summary>
  public int VolumeCount => _maxCreatedIndex + 1;

  /// <summary>Пути созданных томов в порядке .001, .002, …</summary>
  public IReadOnlyList<string> VolumePaths()
  {
    var paths = new string[VolumeCount];
    for (int i = 0; i < paths.Length; i++)
      paths[i] = VolumePath(_basePath, i);
    return paths;
  }

  public override bool CanRead => false;
  public override bool CanSeek => true;
  public override bool CanWrite => true;
  public override long Length => _length;

  public override long Position
  {
    get => _position;
    set => Seek(value, SeekOrigin.Begin);
  }

  public override void Write(byte[] buffer, int offset, int count)
  {
    ArgumentNullException.ThrowIfNull(buffer);

    while (count > 0)
    {
      int volIndex = (int)(_position / _volumeSize);
      long offsetInVolume = _position - (long)volIndex * _volumeSize;

      if (volIndex != _currentIndex)
        OpenVolume(volIndex);

      int room = (int)Math.Min(count, _volumeSize - offsetInVolume);

      _current!.Position = offsetInVolume;
      _current.Write(buffer, offset, room);

      offset += room;
      count -= room;
      _position += room;

      long writtenEnd = offsetInVolume + room;
      if (writtenEnd > _volumeLengths[volIndex])
        _volumeLengths[volIndex] = writtenEnd;

      if (_position > _length)
        _length = _position;
    }
  }

  public override long Seek(long offset, SeekOrigin origin)
  {
    long target = origin switch
    {
      SeekOrigin.Begin => offset,
      SeekOrigin.Current => _position + offset,
      SeekOrigin.End => _length + offset,
      _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };

    if (target < 0)
      throw new IOException("Отрицательная позиция в многотомном потоке.");

    // Открытие нужного тома — лениво при следующей записи (Seek сам файл не трогает).
    _position = target;
    return _position;
  }

  public override void Flush() => _current?.Flush();

  public override int Read(byte[] buffer, int offset, int count)
      => throw new NotSupportedException("Многотомный поток записи не поддерживает чтение.");

  public override void SetLength(long value) => throw new NotSupportedException();

  // Открывает том index текущим (закрыв предыдущий). Новый (ещё не создававшийся) индекс — Create
  // (обрезает возможный устаревший файл), ранее созданный — Open (для патча/дозаписи).
  private void OpenVolume(int index)
  {
    _current?.Flush();
    _current?.Dispose();

    bool isNew = index > _maxCreatedIndex;
    string path = VolumePath(_basePath, index);
    _current = new FileStream(path, isNew ? FileMode.Create : FileMode.Open, FileAccess.ReadWrite);
    _currentIndex = index;

    if (isNew)
    {
      _maxCreatedIndex = index;
      while (_volumeLengths.Count <= index)
        _volumeLengths.Add(0);
    }
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _current?.Flush();
      _current?.Dispose();
      _current = null;
    }

    base.Dispose(disposing);
  }
}
