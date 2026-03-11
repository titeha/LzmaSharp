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

	string archivePath = Path.Combine(outputDir, "bcj_arm64_deflate_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_BCJArm64_Deflate_Single_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		byte[] fileBytes = BuildExpectedArm64LikeBytes(4096);
		File.WriteAllBytes(Path.Combine(workDir, "arm64.bin"), fileBytes);

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
		psi.ArgumentList.Add("arm64.bin");

		psi.ArgumentList.Add("-t7z");

		// Явно задаём цепочку: ARM64 -> Deflate
		psi.ArgumentList.Add("-m0=ARM64");
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

static byte[] BuildExpectedArm64LikeBytes(int length)
{
	if ((length & 3) != 0)
		throw new ArgumentOutOfRangeException(nameof(length), "Для ARM64 нужен размер, кратный 4.");

	if (length < 0x400)
		throw new ArgumentOutOfRangeException(nameof(length), "Для теста нужен буфер побольше.");

	var data = new byte[length];

	// AArch64 NOP = 0xD503201F (little-endian в файле).
	for (int i = 0; i + 4 <= data.Length; i += 4)
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i, 4), 0xD503201Fu);

	// Несколько BL imm26, чтобы ARM64-фильтру было что нормализовать.
	WriteArm64Bl(data, pos: 0x00, target: 0x200);
	WriteArm64Bl(data, pos: 0x40, target: 0x300);
	WriteArm64Bl(data, pos: 0x80, target: 0x180);

	return data;
}

static void WriteArm64Bl(byte[] data, int pos, int target)
{
	if ((pos & 3) != 0)
		throw new ArgumentException("Позиция ARM64-инструкции должна быть кратна 4.", nameof(pos));

	if ((target & 3) != 0)
		throw new ArgumentException("Цель ARM64 branch должна быть кратна 4.", nameof(target));

	if ((uint)(pos + 4) > (uint)data.Length)
		throw new ArgumentOutOfRangeException(nameof(pos));

	// Для AArch64 BL offset кодируется как imm26 * 4 относительно адреса текущей инструкции.
	int diff = target - pos;

	if ((diff & 3) != 0)
		throw new ArgumentException("Смещение ARM64 BL должно делиться на 4.");

	int imm26 = diff >> 2;
	uint instruction = 0x94000000u | ((uint)imm26 & 0x03FFFFFFu);

	BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(pos, 4), instruction);
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