// Copyright (c) ...
// Этот файл — часть учебного/постепенного переписывания LZMA2-декодера.
// Шаг 1: только разбор заголовков чанков LZMA2. Декодирования здесь НЕТ.
//
// LZMA2 поток = последовательность чанков (chunks).
// Каждый чанк начинается с 1 байта control.
//
// Типы control:
//   0x00               — конец потока (End marker)
//   0x01 или 0x02      — "copy" (несжатый) чанк: далее 2 байта UnpackSize-1 (BE), затем UnpackSize байт данных
//   0x80..0xFF         — "lzma" (сжатый) чанк: далее 2 байта (нижние 16 бит UnpackSize-1, BE),
//                        далее 2 байта PackSize-1 (BE),
//                        и опционально 1 байт свойств (properties) если control >= 0xE0.
//                        Сами свойства — это стандартный LZMA property byte.
//
// Важно: тут мы НЕ пытаемся распаковать данные. Мы только читаем заголовок,
// чтобы позже (шагами) собрать потоковый декодер.

namespace Lzma.Core.Lzma2;

/// <summary>
/// Тип чанка LZMA2.
/// </summary>
public enum Lzma2ChunkKind
{
  /// <summary>Конец потока (control == 0x00).</summary>
  End = 0,

  /// <summary>Несжатый блок ("copy"), данные идут как есть.</summary>
  Copy = 1,

  /// <summary>Сжатый блок ("lzma"), payload — LZMA-поток указанной длины.</summary>
  Lzma = 2,
}

/// <summary>
/// Результат чтения заголовка чанка.
/// </summary>
public enum Lzma2ReadHeaderResult
{
  /// <summary>Заголовок успешно прочитан.</summary>
  Ok = 0,

  /// <summary>Нужно больше входных данных (не хватает байт на заголовок).</summary>
  NeedMoreInput = 1,

  /// <summary>Входные данные не похожи на корректный поток LZMA2.</summary>
  InvalidData = 2,
}

/// <summary>
/// Заголовок одного чанка LZMA2 (только метаданные, без payload).
/// </summary>
/// <remarks>
/// Размеры в LZMA2 хранятся как (value - 1), поэтому мы сразу возвращаем "реальный" размер (value).
/// </remarks>
public readonly record struct Lzma2ChunkHeader(
    Lzma2ChunkKind Kind,
    byte Control,
    bool ResetDictionary,
    bool ResetState,
    bool HasProperties,
    int UnpackSize,
    int PackSize,
    byte? Properties)
{
  /// <summary>Минимальный размер заголовка (в байтах) для данного вида чанка.</summary>
  public int HeaderSize => Kind switch
  {
    Lzma2ChunkKind.End => 1,
    Lzma2ChunkKind.Copy => 3,
    Lzma2ChunkKind.Lzma => HasProperties ? 6 : 5,
    _ => throw new ArgumentOutOfRangeException(nameof(Kind))
  };

  /// <summary>Длина payload (данных после заголовка) в байтах.</summary>
  public int PayloadSize => Kind switch
  {
    Lzma2ChunkKind.End => 0,
    Lzma2ChunkKind.Copy => UnpackSize,
    Lzma2ChunkKind.Lzma => PackSize,
    _ => throw new ArgumentOutOfRangeException(nameof(Kind))
  };

  /// <summary>Полная длина чанка (заголовок + payload) в байтах.</summary>
  public int TotalSize => HeaderSize + PayloadSize;

  /// <summary>
  /// Пытается прочитать заголовок LZMA2 чанка из начала <paramref name="input"/>.
  /// </summary>
  /// <param name="input">Входные байты, начиная с control.</param>
  /// <param name="header">Прочитанный заголовок (если Ok).</param>
  /// <param name="bytesConsumed">Сколько байт заголовка было потреблено (если Ok).</param>
  public static Lzma2ReadHeaderResult TryRead(
      ReadOnlySpan<byte> input,
      out Lzma2ChunkHeader header,
      out int bytesConsumed)
  {
    header = default;
    bytesConsumed = 0;

    if (input.Length < 1)
      return Lzma2ReadHeaderResult.NeedMoreInput;

    byte control = input[0];

    // 1) End marker
    if (control == 0x00)
    {
      header = new Lzma2ChunkHeader(
          Kind: Lzma2ChunkKind.End,
          Control: control,
          ResetDictionary: false,
          ResetState: false,
          HasProperties: false,
          UnpackSize: 0,
          PackSize: 0,
          Properties: null);

      bytesConsumed = 1;
      return Lzma2ReadHeaderResult.Ok;
    }

    // 2) Copy (несжатый) блок
    if (control is 0x01 or 0x02)
    {
      if (input.Length < 3)
        return Lzma2ReadHeaderResult.NeedMoreInput;

      // UnpackSize хранится как big-endian (size - 1)
      int unpackSizeMinus1 = (input[1] << 8) | input[2];
      int unpackSize = unpackSizeMinus1 + 1;

      header = new Lzma2ChunkHeader(
          Kind: Lzma2ChunkKind.Copy,
          Control: control,
          ResetDictionary: control == 0x01,
          ResetState: false,
          HasProperties: false,
          UnpackSize: unpackSize,
          PackSize: 0,
          Properties: null);

      bytesConsumed = 3;
      return Lzma2ReadHeaderResult.Ok;
    }

    // 3) Значения 0x03..0x7F в LZMA2 не используются.
    if (control < 0x80)
      return Lzma2ReadHeaderResult.InvalidData;

    // 4) LZMA (сжатый) блок
    bool resetDictionary = control < 0xA0;  // 0x80..0x9F
    bool resetState = control >= 0xC0;      // 0xC0..0xFF
    bool hasProperties = control >= 0xE0;   // 0xE0..0xFF

    int headerSize = hasProperties ? 6 : 5;
    if (input.Length < headerSize)
      return Lzma2ReadHeaderResult.NeedMoreInput;

    // UnpackSize-1: 21 бит = (control & 0x1F) как старшие 5 бит + ещё 16 бит из 2 байт
    int unpackSizeMinus1_21 =
        ((control & 0x1F) << 16) |
        (input[1] << 8) |
        input[2];

    int unpackSizeLzma = unpackSizeMinus1_21 + 1;

    // PackSize-1: 16 бит
    int packSizeMinus1 = (input[3] << 8) | input[4];
    int packSize = packSizeMinus1 + 1;

    byte? props = hasProperties ? input[5] : null;

    header = new Lzma2ChunkHeader(
        Kind: Lzma2ChunkKind.Lzma,
        Control: control,
        ResetDictionary: resetDictionary,
        ResetState: resetState,
        HasProperties: hasProperties,
        UnpackSize: unpackSizeLzma,
        PackSize: packSize,
        Properties: props);

    bytesConsumed = headerSize;
    return Lzma2ReadHeaderResult.Ok;
  }
}
