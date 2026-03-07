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
	 "tests",
	 "Lzma.Core.Tests",
	 "SevenZip",
	 "TestData",
	 "Real");

	Directory.CreateDirectory(outputDir);

	string archivePath = Path.Combine(outputDir, "two_folders_deflate_and_bzip2_mhc_off.7z");

	string workDir = Path.Combine(
	 Path.GetTempPath(),
	 "LzmaSharp_TwoFolders_Deflate_BZip2_NoHeader_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		// Два файла, чтобы 7-Zip сделал два folder'а (методы разные).
		WriteFilledFile(Path.Combine(workDir, "a_deflate.bin"), 24 * 1024, 0x44); // 'D'
		WriteFilledFile(Path.Combine(workDir, "b_bzip2.bin"), 32 * 1024, 0x42);  // 'B'

		if (File.Exists(archivePath))
			File.Delete(archivePath);

		// 1) Создаём архив, добавляя первый файл с Deflate.
		Run7z(sevenZipExe, workDir, new[]
		{
   "a",
   archivePath,
   "a_deflate.bin",
   "-t7z",
   "-m0=Deflate",
   "-ms=off",
   "-mhc=off",
  });

		// 2) Добавляем второй файл уже с BZip2 (другой folder + другой pack stream).
		Run7z(sevenZipExe, workDir, new[]
		{
   "a",
   archivePath,
   "b_bzip2.bin",
   "-t7z",
   "-m0=BZip2",
   "-ms=off",
   "-mhc=off",
  });

		byte[] archiveBytes = File.ReadAllBytes(archivePath);

		new
		{
			sevenZipExe,
			workDir,
			archivePath,
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

static void Run7z(string sevenZipExe, string workingDir, string[] args)
{
	var psi = new ProcessStartInfo
	{
		FileName = sevenZipExe,
		WorkingDirectory = workingDir,
		RedirectStandardOutput = true,
		RedirectStandardError = true,
		UseShellExecute = false,
		CreateNoWindow = true,
	};

	foreach (string a in args)
		psi.ArgumentList.Add(a);

	using var process = Process.Start(psi)!;

	string stdout = process.StandardOutput.ReadToEnd();
	string stderr = process.StandardError.ReadToEnd();

	process.WaitForExit();

	new
	{
		Args = string.Join(' ', args),
		process.ExitCode,
		stdout,
		stderr,
	}.Dump("Результат запуска 7z");

	if (process.ExitCode != 0)
		throw new InvalidOperationException("7z завершился с ошибкой.");
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
  "7z.exe", // последний шанс: если 7z в PATH
 ];

	foreach (string c in candidates)
	{
		if (File.Exists(c))
			return c;

		if (string.Equals(c, "7z.exe", StringComparison.OrdinalIgnoreCase))
			return c;
	}

	throw new FileNotFoundException("Не удалось найти 7z.exe. Укажи путь в SEVEN_ZIP_EXE или поправь Find7ZipExe().");
}