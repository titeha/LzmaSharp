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

	string archivePath = Path.Combine(outputDir, "bcj_armt_deflate_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_BCJArmt_Deflate_Single_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		byte[] fileBytes = BuildExpectedArmtLikeBytes(4096);
		File.WriteAllBytes(Path.Combine(workDir, "armt.bin"), fileBytes);

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
		psi.ArgumentList.Add("armt.bin");

		psi.ArgumentList.Add("-t7z");

		// Явно задаём цепочку: ARMT -> Deflate
		psi.ArgumentList.Add("-m0=ARMT");
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

static byte[] BuildExpectedArmtLikeBytes(int length)
{
	if ((length & 1) != 0)
		throw new ArgumentOutOfRangeException(nameof(length), "Для Thumb нужен чётный размер буфера.");

	if (length < 0x400)
		throw new ArgumentOutOfRangeException(nameof(length), "Для теста нужен буфер побольше.");

	var data = new byte[length];

	// Thumb NOP = 0x46C0.
	for (int i = 0; i + 2 <= data.Length; i += 2)
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i, 2), 0x46C0);

	// Несколько Thumb BL, чтобы ARMT-фильтру было что нормализовать.
	WriteThumbBl(data, pos: 0x00, target: 0x200);
	WriteThumbBl(data, pos: 0x40, target: 0x300);
	WriteThumbBl(data, pos: 0x80, target: 0x180);

	return data;
}

static void WriteThumbBl(byte[] data, int pos, int target)
{
	if ((pos & 1) != 0)
		throw new ArgumentException("Позиция Thumb-инструкции должна быть кратна 2.", nameof(pos));

	if ((target & 1) != 0)
		throw new ArgumentException("Цель Thumb branch должна быть кратна 2.", nameof(target));

	if ((uint)(pos + 4) > (uint)data.Length)
		throw new ArgumentOutOfRangeException(nameof(pos));

	// Для BL в Thumb PC = адрес текущей инструкции + 4.
	int pc = pos + 4;
	int diff = target - pc;

	if ((diff & 1) != 0)
		throw new ArgumentException("Смещение Thumb BL должно делиться на 2.");

	int v = diff >> 1;

	ushort hi = (ushort)(0xF000 | ((v >> 11) & 0x07FF));
	ushort lo = (ushort)(0xF800 | (v & 0x07FF));

	BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos, 2), hi);
	BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2, 2), lo);
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