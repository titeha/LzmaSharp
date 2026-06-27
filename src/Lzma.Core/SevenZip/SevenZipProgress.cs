namespace Lzma.Core.SevenZip;

/// <summary>
/// Прогресс операции над 7z-архивом (извлечение или создание): сколько байт уже обработано
/// и сколько ожидается всего.
/// </summary>
/// <param name="BytesProcessed">
/// Для извлечения — сумма распакованных байт по обработанным folder-ам; для создания — сумма
/// размеров уже упакованных исходных файлов.
/// </param>
/// <param name="TotalBytes">Ожидаемый суммарный размер (распаковки либо исходных данных).</param>
/// <remarks>
/// Гранулярность — по folder-ам/файлам: для многофайловых архивов это фактически по файлам;
/// для одного большого/солидного folder-а отчёт приходит по его завершении. Проценты/ETA
/// вычисляет UI: <c>BytesProcessed / TotalBytes</c> (при <c>TotalBytes &gt; 0</c>).
/// </remarks>
public readonly record struct SevenZipProgress(long BytesProcessed, long TotalBytes);
