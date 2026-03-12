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

	string archiveBasePath = Path.Combine(outputDir, "hello_copy_split_v10k_mhc_off.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_Hello_Copy_Split_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		WriteFilledFile(Path.Combine(workDir, "hello.bin"), 16 * 1024, 0x41);

		foreach (string path in Directory.GetFiles(outputDir, "hello_copy_split_v10k_mhc_off.7z*"))
			File.Delete(path);

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
		psi.ArgumentList.Add(archiveBasePath);
		psi.ArgumentList.Add("hello.bin");
		psi.ArgumentList.Add("-t7z");
		psi.ArgumentList.Add("-m0=Copy");
		psi.ArgumentList.Add("-mhc=off");
		psi.ArgumentList.Add("-v10k");

		using var process = Process.Start(psi)!;

		string stdout = process.StandardOutput.ReadToEnd();
		string stderr = process.StandardError.ReadToEnd();

		process.WaitForExit();

		new
		{
			sevenZipExe,
			workDir,
			archiveBasePath,
			process.ExitCode,
			stdout,
			stderr,
		}.Dump("Результат запуска 7z");

		if (process.ExitCode != 0)
			throw new InvalidOperationException("7z завершился с ошибкой.");

		string[] parts = Directory.GetFiles(outputDir, "hello_copy_split_v10k_mhc_off.7z*");
		Array.Sort(parts, StringComparer.Ordinal);

		var info = parts
			.Select(p => new
			{
				FileName = Path.GetFileName(p),
				Size = new FileInfo(p).Length,
			})
			.ToArray();

		info.Dump("Собранные тома");
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

		return candidate;
	}

	throw new FileNotFoundException(
		"Не найден 7z.exe. " +
		"Укажи путь через переменную окружения SEVEN_ZIP_EXE " +
		"или установи 7-Zip в стандартную папку.");
}