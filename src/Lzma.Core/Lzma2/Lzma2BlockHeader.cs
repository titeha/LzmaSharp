namespace Lzma.Core.Lzma2;

/// <summary>
/// Парсер заголовков блоков LZMA2.
/// Следует спецификации из официального 7-Zip (Lzma2Dec.c).
/// Определяет тип блока по трём старшим битам (маска 0xE0).
/// </summary>
public readonly struct Lzma2BlockHeader
{
  public enum BlockType
  {
    EndOfStream,
    UncompressedResetDic,      // control = 0x01 — несжатые данные + сброс словаря
    UncompressedNoReset,       // control = 0x02 — несжатые данные без сброса
    LzmaNoReset,               // control 0x80..0x9F — LZMA без сброса состояния
    LzmaResetState,            // control 0xA0..0xBF — LZMA + сброс состояния
    LzmaResetStateAndProps,    // control 0xC0..0xDF — LZMA + сброс состояния + новые свойства
    LzmaFullReset,             // control 0xE0..0xFF — полный сброс (словарь + состояние + свойства)
    Invalid
  }

  public BlockType Type { get; }

  public uint UnpackSize { get; } // Размер распакованных данных (+1 согласно спецификации)

  public uint PackSize { get; }   // Размер сжатых данных (+1 согласно спецификации)

  public byte? Props { get; }     // Свойства LZMA (только для блоков 0xC0..0xFF)

  private Lzma2BlockHeader(BlockType type, uint unpackSize, uint packSize, byte? props = null)
  {
    Type = type;
    UnpackSize = unpackSize;
    PackSize = packSize;
    Props = props;
  }

  /// <summary>
  /// Парсит заголовок блока из буфера.
  /// Возвращает количество прочитанных байт или 0 при ошибке структуры.
  /// Тип блока определяется по трём старшим битам (маска 0xE0).
  /// </summary>
  public static int TryParse(ReadOnlySpan<byte> buffer, out Lzma2BlockHeader header)
  {
    header = default;

    if (buffer.Length == 0)
      return 0;

    byte control = buffer[0];
    int offset = 1;

    byte? props = null;

    // Случай 1: Конец потока (0x00)
    if (control == 0x00)
    {
      header = new Lzma2BlockHeader(BlockType.EndOfStream, 0, 0);
      return offset;
    }

    // Объявляем все переменные в начале метода, чтобы избежать конфликта имён
    BlockType blockType;

    uint unpackSize;

    uint packSize;
    // Случай 2: Несжатые данные (бит 7 = 0)
    if ((control & 0x80) == 0)
    {
      if (buffer.Length < 3)
        return 0;

      // Размер распакованных данных: 2 байта + 1 (согласно спецификации 7-Zip)
      unpackSize = ((uint)buffer[1] << 8) | buffer[2];
      unpackSize++;

      blockType = control switch
      {
        0x01 => BlockType.UncompressedResetDic,
        0x02 => BlockType.UncompressedNoReset,
        _ => BlockType.Invalid
      };

      if (blockType == BlockType.Invalid)
        return 0;

      packSize = unpackSize; // Для несжатых данных размеры совпадают
      header = new Lzma2BlockHeader(blockType, unpackSize, packSize);
      return offset + 2;
    }

    // Случай 3: LZMA-блоки (бит 7 = 1)
    // Определяем тип по трём старшим битам (маска 0xE0)
    byte prefix = (byte)(control & 0xE0);

    bool needsProps;
    switch (prefix)
    {
      case 0x80: // 100xxxxx — LZMA без сброса
        blockType = BlockType.LzmaNoReset;
        needsProps = false;
        break;

      case 0xA0: // 101xxxxx — LZMA + сброс состояния
        blockType = BlockType.LzmaResetState;
        needsProps = false;
        break;

      case 0xC0: // 110xxxxx — LZMA + сброс состояния + новые свойства
        blockType = BlockType.LzmaResetStateAndProps;
        needsProps = true;
        break;

      case 0xE0: // 111xxxxx — LZMA + полный сброс
        blockType = BlockType.LzmaFullReset;
        needsProps = true;
        break;

      default:
        return 0; // Недопустимый префикс
    }

    // Проверяем достаточность данных
    if (buffer.Length < (needsProps ? 6 : 5))
      return 0;

    // Размер распакованных данных: младшие 5 бит control + 2 байта + 1
    unpackSize = ((uint)(control & 0x1F) << 16) |
                ((uint)buffer[1] << 8) |
                buffer[2];
    unpackSize++;

    // Размер сжатых данных: 2 байта + 1
    packSize = ((uint)buffer[3] << 8) | buffer[4];
    packSize++;

    offset += 4;

    if (needsProps)
    {
      props = buffer[5];
      offset++;
    }

    header = new Lzma2BlockHeader(blockType, unpackSize, packSize, props);
    return offset;
  }
}
