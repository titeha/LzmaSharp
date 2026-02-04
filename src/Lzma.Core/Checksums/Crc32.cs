namespace Lzma.Core.Checksums;

/// <summary>
/// CRC32 (полином 0xEDB88320, стандартный "ZIP/PKZIP" вариант).
///
/// <para>
/// В 7z CRC используется много где:
/// - StartHeaderCRC в сигнатурном заголовке;
/// - CRC NextHeader;
/// - CRC для потоков/файлов внутри архива.
/// </para>
/// </summary>
internal static class Crc32
{
  // Реверсивный полином CRC-32/ISO-HDLC (Zip, Ethernet, PNG и т.п.).
  private const uint _polynomial = 0xEDB88320u;

  /// <summary>
  /// Начальное состояние (стандартное для CRC32/PKZIP).
  /// </summary>
  public const uint InitialState = 0xFFFF_FFFFu;

  private static readonly uint[] _table = CreateTable();

  /// <summary>
  /// Обновляет CRC-состояние на очередной порции данных.
  ///
  /// <para>
  /// Важно: это "сырое" состояние (ещё не финализированное).
  /// Чтобы получить итоговую CRC, вызови <see cref="Finalize"/>.
  /// </para>
  /// </summary>
  public static uint Update(uint state, ReadOnlySpan<byte> data)
  {
    uint crc = state;

    // Табличный алгоритм (быстрый и достаточно простой).
    foreach (byte b in data)
    {
      uint idx = (crc ^ b) & 0xFFu;
      crc = _table[idx] ^ (crc >> 8);
    }

    return crc;
  }

  /// <summary>
  /// Финализирует CRC-состояние (стандартный xor с 0xFFFFFFFF).
  /// </summary>
  public static uint Finalize(uint state) => state ^ 0xFFFF_FFFFu;

  /// <summary>
  /// Вычисляет CRC32 для блока данных.
  /// </summary>
  public static uint Compute(ReadOnlySpan<byte> data)
  {
    uint state = Update(InitialState, data);
    return Finalize(state);
  }

  private static uint[] CreateTable()
  {
    var table = new uint[256];

    for (uint i = 0; i < 256; i++)
    {
      uint crc = i;

      for (int bit = 0; bit < 8; bit++)
        crc = (crc & 1u) != 0 ? (crc >> 1) ^ _polynomial : (crc >> 1);

      table[i] = crc;
    }

    return table;
  }
}
