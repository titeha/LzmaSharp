<Query Kind="Program" />

// LINQPad 9
// Язык: C# Program

using System.Diagnostics;

void Main()
{
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

	string archivePath = Path.Combine(outputDir, "solid_a_empty_b_ppmd_mhc_off.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_PPMd_Solid_NoHeader_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		WriteFilledFile(Path.Combine(workDir, "a.bin"), 4096, 0x41);
		File.WriteAllBytes(Path.Combine(workDir, "empty.bin"), Array.Empty<byte>());
		WriteFilledFile(Path.Combine(workDir, "b.bin"), 6000, 0x42);

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
		psi.ArgumentList.Add("a.bin");
		psi.ArgumentList.Add("empty.bin");
		psi.ArgumentList.Add("b.bin");

		psi.ArgumentList.Add("-t7z");
		psi.ArgumentList.Add("-m0=PPMd:o=6:mem=16m");
		psi.ArgumentList.Add("-ms=on");
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

	string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
	string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

	string[] candidates =
	[
		Path.Combine(pf, "7-Zip", "7z.exe"),
		Path.Combine(pf86, "7-Zip", "7z.exe"),
		"7z.exe",
	];

	foreach (string c in candidates)
	{
		if (Path.IsPathRooted(c))
		{
			if (File.Exists(c))
				return c;

			continue;
		}

		return c;
	}

	throw new FileNotFoundException(
		"Не найден 7z.exe. " +
		"Укажи путь через переменную окружения SEVEN_ZIP_EXE " +
		"или установи 7-Zip в стандартную папку.");
}