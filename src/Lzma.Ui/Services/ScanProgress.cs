namespace Lzma.Ui.Services;

/// <summary>
/// Отчёт о ходе сканирования/чтения исходных файлов перед сжатием: сколько файлов уже
/// прочитано в память и суммарный объём их байт. Общий размер заранее неизвестен (отчёт
/// идёт по мере чтения), поэтому это «живой счётчик», а не проценты.
/// </summary>
public readonly record struct ScanProgress(int FilesRead, long BytesRead);
