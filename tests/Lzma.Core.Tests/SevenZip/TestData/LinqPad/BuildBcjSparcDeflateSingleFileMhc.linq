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

	string archivePath = Path.Combine(outputDir, "bcj_sparc_deflate_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_BCJSparc_Deflate_Single_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		byte[] fileBytes = BuildExpectedSparcLikeBytes(4096);
		File.WriteAllBytes(Path.Combine(workDir, "sparc.bin"), fileBytes);

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
		psi.ArgumentList.Add("sparc.bin");

		psi.ArgumentList.Add("-t7z");

		// Явно задаём цепочку: SPARC -> Deflate
		psi.ArgumentList.Add("-m0=SPARC");
		psi.ArgumentList.Add("-m1=Deflate");

		psi.ArgumentList.Add("-ms=off");
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

static byte[] BuildExpectedSparcLikeBytes(int length)
{
	if ((length & 3) != 0)
		throw new ArgumentOutOfRangeException(nameof(length), "Для SPARC нужен размер, кратный 4.");

	if (length < 0x400)
		throw new ArgumentOutOfRangeException(nameof(length), "Для теста нужен буфер побольше.");

	var data = new byte[length];

	// SPARC NOP = sethi 0, %g0 = 0x01000000 (big-endian).
	for (int i = 0; i + 4 <= data.Length; i += 4)
		BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i, 4), 0x01000000u);

	// Несколько CALL, чтобы SPARC-фильтру было что нормализовать.
	WriteSparcCall(data, pos: 0x00, target: 0x200);
	WriteSparcCall(data, pos: 0x40, target: 0x300);
	WriteSparcCall(data, pos: 0x80, target: 0x180);

	return data;
}

static void WriteSparcCall(byte[] data, int pos, int target)
{
	if ((pos & 3) != 0)
		throw new ArgumentException("Позиция SPARC-инструкции должна быть кратна 4.", nameof(pos));

	if ((target & 3) != 0)
		throw new ArgumentException("Цель SPARC call должна быть кратна 4.", nameof(target));

	if ((uint)(pos + 4) > (uint)data.Length)
		throw new ArgumentOutOfRangeException(nameof(pos));

	// Для SPARC CALL смещение кодируется относительно адреса текущей инструкции.
	int diff = target - pos;

	if ((diff & 3) != 0)
		throw new ArgumentException("Смещение SPARC call должно делиться на 4.");

	int disp30 = diff >> 2;
	uint instruction = 0x40000000u | ((uint)disp30 & 0x3FFFFFFFu);

	BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(pos, 4), instruction);
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