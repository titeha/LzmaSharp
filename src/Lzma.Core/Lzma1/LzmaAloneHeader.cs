using System.Buffers.Binary;

namespace Lzma.Core.Lzma1;

/// <summary>
/// Заголовок формата "LZMA-Alone" (файлы *.lzma).
/// </summary>
/// <remarks>
/// <para>
/// Это ОТДЕЛЬНЫЙ контейнерный формат, который часто используется для хранения "сырого" потока LZMA.
/// Он состоит из 13 байт заголовка, после которых идут байты range coder'а и сжатые данные.
/// </para>
/// <para>
/// Структура заголовка (13 байт, little-endian):
/// <list type="bullet">
/// <item><description>0:  1 байт properties (lc/lp/pb), как в 7-Zip</description></item>
/// <item><description>1..4: 4 байта dictionary size (UInt32)</description></item>
/// <item><description>5..12: 8 байт uncompressed size (UInt64), либо все 0xFF для "неизвестно"</description></item>
/// </list>
/// </para>
/// <para>
/// На этом шаге мы делаем только чистый парсер/писатель заголовка.
/// Обёртки "прочитал заголовок и распаковал" добавим отдельным маленьким шагом.
/// </para>
/// </remarks>
public readonly struct LzmaAloneHeader
{
  /// <summary>
  /// Размер заголовка LZMA-Alone в байтах.
  /// </summary>
  public const int HeaderSize = 13;

  /// <summary>
  /// properties (lc/lp/pb).
  /// </summary>
  public LzmaProperties Properties { get; }

  /// <summary>
  /// Размер словаря в байтах.
  /// </summary>
  public int DictionarySize { get; }

  /// <summary>
  /// Размер распакованных данных.
  /// Null = размер неизвестен (в заголовке хранится UInt64.MaxValue).
  /// </summary>
  public ulong? UncompressedSize { get; }

  /// <summary>
  /// Создаёт экземпляр заголовка.
  /// </summary>
  public LzmaAloneHeader(LzmaProperties properties, int dictionarySize, ulong? uncompressedSize)
  {
    if (dictionarySize <= 0)
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Размер словаря должен быть > 0.");

    Properties = properties;
    DictionarySize = dictionarySize;
    UncompressedSize = uncompressedSize;
  }

  /// <summary>
  /// Результат попытки чтения заголовка.
  /// </summary>
  public enum ReadResult
  {
    Ok,
    NeedMoreInput,
    InvalidData,
  }

  /// <summary>
  /// Пытается прочитать заголовок LZMA-Alone.
  /// </summary>
  /// <param name="input">Входные данные.</param>
  /// <param name="header">Распарсенный заголовок.</param>
  /// <param name="bytesConsumed">Сколько байт потребили из input (только при Ok).</param>
  public static ReadResult TryRead(ReadOnlySpan<byte> input, out LzmaAloneHeader header, out int bytesConsumed)
  {
    header = default;
    bytesConsumed = 0;

    if (input.Length < HeaderSize)
      return ReadResult.NeedMoreInput;

    // 1) properties byte
    byte propsByte = input[0];
    if (!LzmaProperties.TryParse(propsByte, out var props))
      return ReadResult.InvalidData;

    // 2) dictionary size
    uint dict = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(1, 4));
    if (dict == 0 || dict > int.MaxValue)
      return ReadResult.InvalidData;

    // 3) uncompressed size
    ulong unpack = BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(5, 8));
    ulong? unpackSize = unpack == ulong.MaxValue ? null : unpack;

    header = new LzmaAloneHeader(props, (int)dict, unpackSize);
    bytesConsumed = HeaderSize;
    return ReadResult.Ok;
  }

  /// <summary>
  /// Пытается записать заголовок в <paramref name="output"/>.
  /// </summary>
  /// <param name="output">Буфер для записи.</param>
  /// <param name="bytesWritten">Сколько байт записали (только при true).</param>
  public bool TryWrite(Span<byte> output, out int bytesWritten)
  {
    bytesWritten = 0;

    if (output.Length < HeaderSize)
      return false;

    if (!Properties.TryToByte(out byte propsByte))
      return false;

    output[0] = propsByte;
    BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(1, 4), (uint)DictionarySize);

    ulong unpack = UncompressedSize ?? ulong.MaxValue;
    BinaryPrimitives.WriteUInt64LittleEndian(output.Slice(5, 8), unpack);

    bytesWritten = HeaderSize;
    return true;
  }
}
