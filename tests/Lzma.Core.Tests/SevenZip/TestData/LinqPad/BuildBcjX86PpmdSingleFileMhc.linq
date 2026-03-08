<Query Kind="Program" />

// LINQPad 9
// Язык: C# Program

using System.Buffers.Binary;
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

	string archivePath = Path.Combine(outputDir, "bcj_x86_ppmd_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_BCJx86_PPMd_Single_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		// Входной файл такой же, как ожидаемый в тесте.
		byte[] fileBytes = BuildExpectedX86LikeBytes(4096);
		File.WriteAllBytes(Path.Combine(workDir, "x86_ppmd.bin"), fileBytes);

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
		psi.ArgumentList.Add("x86_ppmd.bin");

		psi.ArgumentList.Add("-t7z");

		// Явно задаём цепочку: BCJ(x86) -> PPMd
		psi.ArgumentList.Add("-m0=BCJ");
		psi.ArgumentList.Add("-m1=PPMd:o=6:mem=16m");

		// Header compressed (EncodedHeader) — как в других bcj_*_mhc.7z тестах.
		psi.ArgumentList.Add("-mhc=on");

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

static byte[] BuildExpectedX86LikeBytes(int length)
{
	var data = new byte[length];
	for (int i = 0; i < data.Length; i++)
		data[i] = 0x90; // NOP

	WriteRel32(data, pos: 0x00, opcode: 0xE8, target: 0x200); // CALL rel32
	WriteRel32(data, pos: 0x40, opcode: 0xE9, target: 0x300); // JMP rel32
	WriteRel32(data, pos: 0x80, opcode: 0xE8, target: 0x180); // CALL rel32

	return data;
}

static void WriteRel32(byte[] data, int pos, byte opcode, int target)
{
	data[pos] = opcode;
	int rel = target - (pos + 5);
	BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(pos + 1, 4), rel);
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