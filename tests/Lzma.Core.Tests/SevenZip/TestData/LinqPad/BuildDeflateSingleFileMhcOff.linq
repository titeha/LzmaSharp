<Query Kind="Program" />

// LINQPad 9
// Язык: C# Program

using System.Diagnostics;

void Main()
{
	// Поправь путь при необходимости.
	string repoRoot = @"G:\Projects\Windows\LzmaSharp";

	string sevenZipExe = Find7ZipExe();

	string outputDir = Path.Combine(
	  repoRoot,
	  "tests", "Lzma.Core.Tests", "SevenZip", "TestData", "Real");

	Directory.CreateDirectory(outputDir);

	string archivePath = Path.Combine(outputDir, "deflate_singlefile_mhc_off.7z");

	string workDir = Path.Combine(
	  Path.GetTempPath(),
	  "LzmaSharp_Deflate_Single_NoHeader_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		// Делаем входной файл: 16 KiB байт 'A' (как в существующем deflate real-тесте).
		WriteFilledFile(Path.Combine(workDir, "deflate.bin"), 16 * 1024, 0x41);

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
		psi.ArgumentList.Add("deflate.bin");

		psi.ArgumentList.Add("-t7z");
		psi.ArgumentList.Add("-m0=Deflate");
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
			// При необходимости временную папку можно удалить вручную.
		}
	}
}

static void WriteFilledFile(string path, int length, byte value)
{
	byte[] data = GC.AllocateUninitializedArray<byte>(length);
	data.AsSpan().Fill(value);
	File.WriteAllBytes(path, data);
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

		// "7z.exe" пусть резолвится через PATH.
		return candidate;
	}

	throw new FileNotFoundException(
	  "Не найден 7z.exe. " +
	  "Укажи путь через переменную окружения SEVEN_ZIP_EXE " +
	  "или установи 7-Zip в стандартную папку.");
}