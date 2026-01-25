namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Минимальный LZMA-энкодер.</para>
/// <para>
/// На данном шаге реализован ТОЛЬКО путь <c>isMatch == 0</c> (литералы).
/// Это сделано осознанно: мы двигаемся маленькими шагами и каждую часть покрываем тестами.
/// </para>
/// <para>
/// Важно:
/// - это «сырой» LZMA битстрим (range coded), без контейнерного заголовка;
/// - мы пока не пишем end-of-stream маркер, поэтому «конец потока» определяется внешним контрактом
///   (например, известным размером распакованных данных);
/// - мы пока не ищем совпадения в словаре и не делаем оптимальный выбор.
/// </para>
/// </summary>
public sealed class LzmaEncoder
{
  private readonly LzmaProperties _properties;
  private LzmaRangeEncoder _range = new();
  private readonly LzmaLiteralEncoder _literal;

  // isMatch[state][posState]
  private readonly ushort[] _isMatch;
  private readonly int _numPosStates;
  private readonly int _posStateMask;

  private LzmaState _state;
  private byte _previousByte;
  private long _position;

  /// <summary>
  /// Создаёт энкодер с указанными LZMA properties (lc/lp/pb).
  /// </summary>
  public LzmaEncoder(LzmaProperties properties)
  {
    _properties = properties;

    _literal = new LzmaLiteralEncoder(properties.Lc, properties.Lp);

    _numPosStates = 1 << properties.Pb;
    _posStateMask = _numPosStates - 1;

    _isMatch = new ushort[LzmaConstants.NumStates * _numPosStates];

    Reset();
  }

  /// <summary>
  /// Сбрасывает состояние и вероятностные модели. Используй перед кодированием нового потока.
  /// </summary>
  public void Reset()
  {
    _range.Reset();

    LzmaProbability.Reset(_isMatch);
    _literal.Reset();

    _state.Reset();

    _previousByte = 0;
    _position = 0;
  }

  /// <summary>
  /// Кодирует весь <paramref name="input"/> как последовательность литералов (isMatch==0).
  /// </summary>
  public byte[] EncodeLiteralOnly(ReadOnlySpan<byte> input)
  {
    // Чтобы поведение было максимально предсказуемым на раннем этапе,
    // этот метод всегда кодирует НОВЫЙ поток.
    Reset();

    for (int i = 0; i < input.Length; i++)
    {
      byte b = input[i];

      // 1) isMatch[state][posState] = 0
      int posState = (int)_position & _posStateMask;
      int idx = _state.Value * _numPosStates + posState;
      _range.EncodeBit(ref _isMatch[idx], 0u);

      // 2) Кодируем сам литерал.
      _literal.EncodeNormal(ref _range, _position, _previousByte, b);

      // 3) Обновляем состояние.
      _previousByte = b;
      _state.UpdateLiteral();
      _position++;
    }

    // Дописываем хвост range encoder'а, чтобы поток был завершён корректно.
    // Эти байты же являются теми самыми «init bytes», которые читает range decoder (первые 5 байт Code).
    _range.Flush();

    return _range.ToArray();
  }
}
