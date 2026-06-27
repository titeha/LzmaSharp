namespace Lzma.Core.SevenZip;

/// <summary>
/// Прогресс извлечения 7z-архива: сколько распакованных байт уже готово и сколько ожидается всего.
/// </summary>
/// <param name="BytesProcessed">Сумма распакованных байт по уже обработанным folder-ам.</param>
/// <param name="TotalBytes">Ожидаемый суммарный размер распаковки всех folder-ов.</param>
/// <remarks>
/// Гранулярность — по folder-ам: для многофайловых (несолидных) архивов это фактически
/// по файлам; для одного большого/солидного folder-а отчёт приходит по его завершении.
/// Проценты/ETA вычисляет UI: <c>BytesProcessed / TotalBytes</c> (при <c>TotalBytes &gt; 0</c>).
/// </remarks>
public readonly record struct SevenZipExtractionProgress(long BytesProcessed, long TotalBytes);
