using System;

namespace Lzma.Ui.Services;

/// <summary>
/// Синхронный <see cref="IProgress{T}"/> из делегата: вызывает его на месте, в отличие от
/// <see cref="Progress{T}"/>, который постит отчёты асинхронно через SynchronizationContext.
/// Нужен там, где отчёты уже приходят на UI-поток и важен точный момент вызова (счётчик
/// сканирования: иначе поздний отчёт мог бы «мигнуть» индикатором после завершения).
/// </summary>
public sealed class DelegateProgress<T>(Action<T> report) : IProgress<T>
{
  public void Report(T value) => report(value);
}
