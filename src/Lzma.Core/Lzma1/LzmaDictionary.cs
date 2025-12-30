// Copyright (c) LzmaSharp
// Licensed under the MIT license.

namespace Lzma.Core.Lzma1;

/// <summary>
/// Результат операций записи в словарь LZMA.
/// </summary>
public enum LzmaDictionaryResult
{
  /// <summary>Операция выполнена успешно.</summary>
  Ok = 0,

  /// <summary>В переданный буфер вывода не помещается требуемое количество байт.</summary>
  OutputTooSmall = 1,

  /// <summary>Запрошена недопустимая дистанция (например, больше уже записанных байт или больше размера словаря).</summary>
  InvalidDistance = 2,
}

/// <summary>
/// <para>Словарь LZMA (скользящее окно / ring-buffer).</para>
/// <para>
/// Зачем он нужен:
/// - LZMA кодирует "матчи" — ссылки на уже распакованные байты: (distance, length)
/// - Чтобы распаковать такой матч, декодер должен уметь быстро читать байты "назад"
///   и одновременно записывать новые байты (копирование может перекрываться).
/// </para>
/// <para>
/// Важные свойства реализации:
/// - Без unsafe и без указателей.
/// - Предсказуемое поведение: все граничные случаи возвращаются как коды результата.
/// - Поддержка перекрывающихся копирований (distance=1, length>1 и т.п.).
/// </para>
/// </summary>
public sealed class LzmaDictionary
{
  private readonly byte[] _buffer;

  // Текущая позиция записи в ring-buffer (0..Size-1).
  private int _pos;

  // Сколько байт всего "выдано" наружу с момента последнего Reset().
  // Это нужно, чтобы валидировать дистанции на самых первых байтах потока.
  private long _totalWritten;

  /// <summary>Размер словаря (в байтах).</summary>
  public int Size => _buffer.Length;

  /// <summary>Текущая позиция записи в ring-buffer (0..Size-1).</summary>
  public int Position => _pos;

  /// <summary>Всего байт записано с момента последнего Reset().</summary>
  public long TotalWritten => _totalWritten;

  public LzmaDictionary(int size)
  {
    if (size <= 0)
      throw new ArgumentOutOfRangeException(nameof(size), "Размер словаря должен быть положительным.");

    _buffer = new byte[size];
    Reset(clearBuffer: true);
  }

  /// <summary>
  /// Сбрасывает состояние словаря.
  /// </summary>
  /// <param name="clearBuffer">
  /// Если true — обнуляет весь буфер словаря.
  /// Для корректности это не обязательно (мы всё равно не позволяем читать "дальше, чем TotalWritten"),
  /// но для отладки и предсказуемости на ранних шагах удобно.
  /// </param>
  public void Reset(bool clearBuffer = true)
  {
    _pos = 0;
    _totalWritten = 0;

    if (clearBuffer)
      Array.Clear(_buffer, 0, _buffer.Length);
  }

  /// <summary>
  /// Пишет один байт в выход и в словарь.
  /// </summary>
  public LzmaDictionaryResult TryPutByte(byte value, Span<byte> output, ref int outputPos)
  {
    if ((uint)outputPos > (uint)output.Length)
      throw new ArgumentOutOfRangeException(nameof(outputPos), "outputPos не может быть больше длины выходного буфера.");

    if (outputPos == output.Length)
      return LzmaDictionaryResult.OutputTooSmall;

    output[outputPos++] = value;

    _buffer[_pos++] = value;
    if (_pos == _buffer.Length)
      _pos = 0;

    _totalWritten++;
    return LzmaDictionaryResult.Ok;
  }

  /// <summary>
  /// <para>Копирует "матч" (distance, length): берёт байты из словаря на distance назад и пишет length байт в выход.</para>
  /// <para>Важно: копирование может перекрываться, поэтому копируем по одному байту.</para>
  /// </summary>
  public LzmaDictionaryResult TryCopyMatch(int distance, int length, Span<byte> output, ref int outputPos)
  {
    if ((uint)outputPos > (uint)output.Length)
      throw new ArgumentOutOfRangeException(nameof(outputPos), "outputPos не может быть больше длины выходного буфера.");
    if (length < 0)
      throw new ArgumentOutOfRangeException(nameof(length), "Длина матча не может быть отрицательной.");

    if (length == 0)
      return LzmaDictionaryResult.Ok;

    // distance в LZMA считается от 1.
    if (distance <= 0 || distance > _buffer.Length)
      return LzmaDictionaryResult.InvalidDistance;

    // Нельзя ссылаться на байты, которые ещё не были записаны в словарь.
    if (_totalWritten < distance)
      return LzmaDictionaryResult.InvalidDistance;

    if (output.Length - outputPos < length)
      return LzmaDictionaryResult.OutputTooSmall;

    // Индекс источника: позиция записи минус distance.
    int src = _pos - distance;
    if (src < 0)
      src += _buffer.Length;

    for (int i = 0; i < length; i++)
    {
      byte b = _buffer[src];

      output[outputPos++] = b;

      _buffer[_pos] = b;
      _pos++;
      if (_pos == _buffer.Length)
        _pos = 0;

      src++;
      if (src == _buffer.Length)
        src = 0;

      _totalWritten++;
    }

    return LzmaDictionaryResult.Ok;
  }

  /// <summary>
  /// Пытается получить байт "назад" на distance (distance=1 — предыдущий байт).
  /// </summary>
  public bool TryGetByteBack(int distance, out byte value)
  {
    value = 0;

    if (distance <= 0 || distance > _buffer.Length)
      return false;

    if (_totalWritten < distance)
      return false;

    int idx = _pos - distance;
    if (idx < 0)
      idx += _buffer.Length;

    value = _buffer[idx];
    return true;
  }
}
