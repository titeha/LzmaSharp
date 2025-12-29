// Copyright (c) LzmaSharp project.
// Лицензия: см. корень репозитория.

namespace Lzma.Core;

/// <summary>
/// <para>Универсальная структура для отчёта о прогрессе.</para>
/// <para>
/// Мы сознательно держим её максимально простой:
/// - <see cref="BytesRead"/>: сколько байт было потреблено из входного потока (сжатые данные).
/// - <see cref="BytesWritten"/>: сколько байт было произведено в выход (распакованные данные).
/// </para>
/// <para>
/// Проценты / ETA / скорость — это ответственность уровня UI, потому что там известны:
/// - общий размер файла (для BytesRead),
/// - ожидаемый размер распаковки (для BytesWritten),
/// - модель обновления (частота),
/// - синхронизация с UI-потоком.
/// </para>
/// </summary>
public readonly record struct LzmaProgress(long BytesRead, long BytesWritten);
