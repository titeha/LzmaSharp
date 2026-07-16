using System;
using System.Collections.Generic;
using System.IO;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Поток чтения, склеивающий тома <c>base.001</c>, <c>base.002</c>, … в один логический поток
/// (многотомный 7z — чистая побайтовая нарезка). Передаётся как <c>archive</c> в существующие
/// потоковые reader-ы (<see cref="SevenZipArchiveStreamReader"/>, извлечение) БЕЗ их изменений:
/// поддерживает произвольный <see cref="Seek"/> (reader сикает в конец за next-header) и чтение
/// сквозь границы томов. Открытым держит один том за раз.
/// </summary>
public sealed class VolumeSpanningReadStream : Stream
{
  private readonly string[] _paths;
  private readonly long[] _starts; // кумулятивное смещение начала каждого тома
  private readonly long[] _lengths;
  private readonly long _length;

  private long _position;
  private int _currentIndex = -1;
  private FileStream? _current;

  /// <summary>
  /// Открывает набор томов <paramref name="basePath"/>.001, .002, … (подряд, пока файлы существуют).
  /// </summary>
  /// <exception cref="FileNotFoundException">Если нет даже первого тома <c>.001</c>.</exception>
  public VolumeSpanningReadStream(string basePath)
  {
    ArgumentNullException.ThrowIfNull(basePath);

    var paths = new List<string>();
    var lengths = new List<long>();
    for (int i = 0; ; i++)
    {
      string p = VolumeSpanningWriteStream.VolumePath(basePath, i);
      if (!File.Exists(p))
        break;
      paths.Add(p);
      lengths.Add(new FileInfo(p).Length);
    }

    if (paths.Count == 0)
      throw new FileNotFoundException($"Не найден первый том: {VolumeSpanningWriteStream.VolumePath(basePath, 0)}");

    _paths = [.. paths];
    _lengths = [.. lengths];
    _starts = new long[_paths.Length];

    long acc = 0;
    for (int i = 0; i < _paths.Length; i++)
    {
      _starts[i] = acc;
      acc += _lengths[i];
    }

    _length = acc;
  }

  /// <summary>Число томов набора.</summary>
  public int VolumeCount => _paths.Length;

  /// <summary>
  /// Если <paramref name="path"/> — первый том (оканчивается на <c>.NNN</c> и рядом есть <c>.001</c>),
  /// возвращает базовый путь (без суффикса <c>.NNN</c>) и <see langword="true"/>. Иначе — <see langword="false"/>.
  /// </summary>
  public static bool TryGetVolumeBasePath(string path, out string basePath)
  {
    basePath = path ?? string.Empty;
    if (string.IsNullOrEmpty(path))
      return false;

    int dot = path.LastIndexOf('.');
    if (dot <= 0 || dot == path.Length - 1)
      return false;

    string suffix = path[(dot + 1)..];
    if (suffix.Length < 3)
      return false;
    foreach (char c in suffix)
      if (c is < '0' or > '9')
        return false;

    string candidate = path[..dot];
    if (!File.Exists(VolumeSpanningWriteStream.VolumePath(candidate, 0)))
      return false;

    basePath = candidate;
    return true;
  }

  public override bool CanRead => true;
  public override bool CanSeek => true;
  public override bool CanWrite => false;
  public override long Length => _length;

  public override long Position
  {
    get => _position;
    set => Seek(value, SeekOrigin.Begin);
  }

  public override int Read(byte[] buffer, int offset, int count)
  {
    ArgumentNullException.ThrowIfNull(buffer);

    int total = 0;
    while (count > 0 && _position < _length)
    {
      int volIndex = VolumeAt(_position);
      if (volIndex != _currentIndex)
        OpenVolume(volIndex);

      long offsetInVolume = _position - _starts[volIndex];
      int room = (int)Math.Min(count, _lengths[volIndex] - offsetInVolume);

      _current!.Position = offsetInVolume;
      int read = _current.Read(buffer, offset, room);
      if (read <= 0)
        break;

      total += read;
      offset += read;
      count -= read;
      _position += read;
    }

    return total;
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

    _position = target;
    return _position;
  }

  public override void Flush() { }
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();

  // Индекс тома, содержащего позицию pos (последний i с _starts[i] <= pos).
  private int VolumeAt(long pos)
  {
    int lo = 0, hi = _paths.Length - 1, res = 0;
    while (lo <= hi)
    {
      int mid = (lo + hi) / 2;
      if (_starts[mid] <= pos)
      {
        res = mid;
        lo = mid + 1;
      }
      else
      {
        hi = mid - 1;
      }
    }

    return res;
  }

  private void OpenVolume(int index)
  {
    _current?.Dispose();
    _current = new FileStream(_paths[index], FileMode.Open, FileAccess.Read);
    _currentIndex = index;
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _current?.Dispose();
      _current = null;
    }

    base.Dispose(disposing);
  }
}
