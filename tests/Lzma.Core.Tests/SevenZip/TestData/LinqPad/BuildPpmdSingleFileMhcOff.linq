<Query Kind="Program" />

// LINQPad 9
// Язык: C# Program

using System.Diagnostics;
using System.Text;

void Main()
{
	// Поправь путь при необходимости.
	string repoRoot = @"G:\Projects\Windows\LzmaSharp";

	string sevenZipExe = Find7ZipExe();

	string outputDir = Path.Combine(
		repoRoot,
		"tests",
		"Lzma.Core.Tests",
		"SevenZip",
		"TestData",
		"Real");

	Directory.CreateDirectory(outputDir);

	string archivePath = Path.Combine(outputDir, "ppmd_singlefile_mhc_off.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_PPMd_Single_MhcOff_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		byte[] fileBytes = CreatePpmdTextBytes();
		File.WriteAllBytes(Path.Combine(workDir, "ppmd.txt"), fileBytes);

		if (File.Exists(archivePath))
			File.Delete(archivePath);

		var psi = new ProcessStartInfo
		{
			FileName = sevenZipExe,
			WorkingDirectory = workDir,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		psi.ArgumentList.Add("a");
		psi.ArgumentList.Add(archivePath);
		psi.ArgumentList.Add("ppmd.txt");
		psi.ArgumentList.Add("-t7z");

		// Фиксируем параметры, чтобы архив был воспроизводимым.
		psi.ArgumentList.Add("-m0=PPMd:o=6:mem=16m");

		// ВАЖНО: NextHeader НЕ сжимаем => NextHeaderKind == Header.
		psi.ArgumentList.Add("-mhc=off");

		using var process = Process.Start(psi)!;

		string stdout = process.StandardOutput.ReadToEnd();
		string stderr = process.StandardError.ReadToEnd();

		process.WaitForExit();

		new
		{
			sevenZipExe,
			workDir,
			archivePath,
			process.ExitCode,
			stdout,
			stderr,
		}.Dump("Результат запуска 7z");

		if (process.ExitCode != 0)
			throw new InvalidOperationException("7z завершился с ошибкой.");

		byte[] archiveBytes = File.ReadAllBytes(archivePath);

		new
		{
			ArchivePath = archivePath,
			ArchiveSize = archiveBytes.Length,
			InputSize = fileBytes.Length,
		}.Dump("Архив собран");
	}
	finally
	{
		try
		{
			if (Directory.Exists(workDir))
				Directory.Delete(workDir, recursive: true);
		}
		catch
		{
			// Если ОС удерживает хэндл, папку можно удалить вручную позже.
		}
	}
}

static byte[] CreatePpmdTextBytes()
{
	const string line1 = "PPMd real test line 01: alpha beta gamma delta epsilon zeta.\n";
	const string line2 = "PPMd real test line 02: the quick brown fox jumps over the lazy dog.\n";
	const string line3 = "PPMd real test line 03: 0123456789 repeated text for compression.\n";

	var sb = new StringBuilder(capacity: 32 * 1024);
	for (int i = 0; i < 180; i++)
	{
		sb.Append(line1);
		sb.Append(line2);
		sb.Append(line3);
	}

	return Encoding.ASCII.GetBytes(sb.ToString());
}

static string Find7ZipExe()
{
	string? envValue = Environment.GetEnvironmentVariable("SEVEN_ZIP_EXE");
	if (!string.IsNullOrWhiteSpace(envValue) && File.Exists(envValue))
		return envValue;

	string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
	string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

	string[] candidates =
	[
		Path.Combine(programFiles, "7-Zip", "7z.exe"),
		Path.Combine(programFilesX86, "7-Zip", "7z.exe"),
		"7z.exe",
	];

	foreach (string candidate in candidates)
	{
		if (Path.IsPathRooted(candidate))
		{
			if (File.Exists(candidate))
				return candidate;

			continue;
		}

		// Если это просто "7z.exe", пробуем через PATH.
		return candidate;
	}

	throw new FileNotFoundException(
		"Не найден 7z.exe. Укажи путь через переменную окружения SEVEN_ZIP_EXE " +
		"или установи 7-Zip в стандартную папку.");
}