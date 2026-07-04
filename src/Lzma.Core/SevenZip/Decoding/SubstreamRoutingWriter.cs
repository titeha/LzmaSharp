using System.IO;

using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Потоковый маршрутизатор: распределяет непрерывный поток декодированных байт одного folder-а
/// по его substream-файлам в заданном порядке, считая CRC32 каждого на лету, и пишет в целевые
/// потоки (обычно <see cref="FileStream"/>). Позволяет потоковому извлечению не буферизировать
/// весь folder/файл в памяти. Только запись.
/// </summary>
/// <remarks>
/// После завершения записи вызывающий проверяет <see cref="IsComplete"/> (все сегменты заполнены
/// ровно), <see cref="CrcMismatch"/> (несовпадение CRC какого-либо сегмента) и
/// <see cref="SizeOverflow"/> (пришло больше байт, чем суммарный размер сегментов).
/// </remarks>
internal sealed class SubstreamRoutingWriter : Stream
{
  /// <summary>Один целевой сегмент: поток-приёмник, ожидаемый размер и (опц.) ожидаемый CRC32.</summary>
  internal readonly struct Segment(Stream target, long length, bool hasCrc, uint expectedCrc)
  {
    public Stream Target { get; } = target;
    public long Length { get; } = length;
    public bool HasCrc { get; } = hasCrc;
    public uint ExpectedCrc { get; } = expectedCrc;
  }

  private readonly Segment[] _segments;
  private int _index;
  private long _remaining;
  private uint _crcState;

  /// <summary>Обнаружено несовпадение CRC хотя бы одного заполненного сегмента.</summary>
  public bool CrcMismatch { get; private set; }

  /// <summary>Пришло больше байт, чем суммарный размер всех сегментов.</summary>
  public bool SizeOverflow { get; private set; }

  public SubstreamRoutingWriter(Segment[] segments)
  {
    ArgumentNullException.ThrowIfNull(segments);

    _segments = segments;
    _index = 0;
    _crcState = Crc32.InitialState;
    _remaining = segments.Length > 0 ? segments[0].Length : 0;

    // Ведущие сегменты нулевой длины закрываем сразу (валидируя их CRC как CRC пустого).
    AdvanceCompletedSegments();
  }

  /// <summary>Все ли сегменты полностью заполнены (после записи всего выхода folder-а).</summary>
  public bool IsComplete => _index >= _segments.Length;

  public override bool CanWrite => true;
  public override bool CanRead => false;
  public override bool CanSeek => false;
  public override long Length => throw new NotSupportedException();

  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override void Write(ReadOnlySpan<byte> buffer)
  {
    while (!buffer.IsEmpty)
    {
      if (_index >= _segments.Length)
      {
        SizeOverflow = true; // лишние байты — писать больше некуда
        return;
      }

      int take = (int)Math.Min(buffer.Length, _remaining);
      ReadOnlySpan<byte> chunk = buffer[..take];

      _segments[_index].Target.Write(chunk);
      _crcState = Crc32.Update(_crcState, chunk);
      _remaining -= take;
      buffer = buffer[take..];

      AdvanceCompletedSegments();
    }
  }

  public override void Write(byte[] buffer, int offset, int count)
      => Write(buffer.AsSpan(offset, count));

  public override void WriteByte(byte value)
  {
    Span<byte> one = [value];
    Write(one);
  }

  // Пока текущий сегмент заполнен (_remaining == 0), валидирует его CRC и переходит к следующему.
  // Итеративно (без рекурсии) обрабатывает и сегменты нулевой длины.
  private void AdvanceCompletedSegments()
  {
    while (_index < _segments.Length && _remaining == 0)
    {
      Segment seg = _segments[_index];

      if (seg.HasCrc && Crc32.Finalize(_crcState) != seg.ExpectedCrc)
        CrcMismatch = true;

      _index++;
      _crcState = Crc32.InitialState;
      _remaining = _index < _segments.Length ? _segments[_index].Length : 0;
    }
  }

  public override void Flush()
  {
  }

  public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  public override void SetLength(long value) => throw new NotSupportedException();
}
