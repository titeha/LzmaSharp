using Lzma.Ui.Services;

// Test-only проба для шага A2 (F3 / AUD-005).
//
// Назначение: воспроизвести validate/use inconsistency относительных staged-путей в
// ИЗОЛИРОВАННОМ процессе. Валидация выполняется при одном Environment.CurrentDirectory,
// а файловые операции Commit — при другом. Глобальный CWD xUnit-раннера не затрагивается.
//
// Использование:
//   Lzma.Ui.CwdProbe <initialCwd> <switchCwd> <destinationBasePath> <stagedPath>...
//
// Коды возврата:
//   0 — SetVolumes и Commit завершились без исключения (инварианты проверяет вызывающий тест
//       по фактическому состоянию файловой системы);
//   1 — Commit/SetVolumes бросили исключение (тип и сообщение — в stderr);
//   2 — неверные аргументы.
//
// В stdout проба печатает наблюдаемые факты (CWD до/после переключения), чтобы вызывающий
// тест мог приложить их к сообщению об отказе.

if (args.Length < 4)
{
  Console.Error.WriteLine(
      "usage: Lzma.Ui.CwdProbe <initialCwd> <switchCwd> <destinationBasePath> <stagedPath>...");
  return 2;
}

string initialCwd = args[0];
string switchCwd = args[1];
string destinationBasePath = args[2];
string[] stagedPaths = args[3..];

try
{
  Directory.SetCurrentDirectory(initialCwd);
  Console.WriteLine($"cwd-before-validation={Directory.GetCurrentDirectory()}");

  using StagedVolumeSet volumes = new(destinationBasePath);
  volumes.SetVolumes(stagedPaths);

  // Ключевой момент сценария: множество путей проверено при initialCwd,
  // а I/O будет выполняться при switchCwd.
  Directory.SetCurrentDirectory(switchCwd);
  Console.WriteLine($"cwd-before-commit={Directory.GetCurrentDirectory()}");

  volumes.Commit();

  Console.WriteLine("commit=ok");
  return 0;
}
catch (Exception ex)
{
  Console.Error.WriteLine($"{ex.GetType().FullName}: {ex.Message}");
  return 1;
}
