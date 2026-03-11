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

	string archivePath = Path.Combine(outputDir, "bcj_arm_deflate_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_BCJArm_Deflate_Single_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		byte[] fileBytes = BuildExpectedArmLikeBytes(4096);
		File.WriteAllBytes(Path.Combine(workDir, "arm.bin"), fileBytes);

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
		psi.ArgumentList.Add("arm.bin");

		psi.ArgumentList.Add("-t7z");

		// Явно задаём цепочку: ARM -> Deflate
		psi.ArgumentList.Add("-m0=ARM");
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

static byte[] BuildExpectedArmLikeBytes(int length)
{
	if (length < 0x400)
		throw new ArgumentOutOfRangeException(nameof(length), "Для теста нужен буфер побольше.");

	var data = new byte[length];

	// ARM NOP = MOV r0, r0 = 0xE1A00000
	for (int i = 0; i + 4 <= data.Length; i += 4)
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i, 4), 0xE1A00000u);

	// Несколько branch-инструкций, чтобы ARM-фильтру было что нормализовать.
	WriteArmBranch(data, pos: 0x00, link: true, target: 0x200); // BL
	WriteArmBranch(data, pos: 0x40, link: false, target: 0x300); // B
	WriteArmBranch(data, pos: 0x80, link: true, target: 0x180); // BL

	return data;
}

static void WriteArmBranch(byte[] data, int pos, bool link, int target)
{
	if ((pos & 3) != 0)
		throw new ArgumentException("Позиция ARM-инструкции должна быть кратна 4.", nameof(pos));

	if ((target & 3) != 0)
		throw new ArgumentException("Цель ARM branch должна быть кратна 4.", nameof(target));

	if ((uint)(pos + 4) > (uint)data.Length)
		throw new ArgumentOutOfRangeException(nameof(pos));

	int pc = pos + 8;
	int diff = target - pc;

	if ((diff & 3) != 0)
		throw new ArgumentException("Смещение ARM branch должно делиться на 4.");

	int imm24 = diff >> 2;

	// B / BL, cond = AL
	uint instruction = (link ? 0xEB000000u : 0xEA000000u) | ((uint)imm24 & 0x00FFFFFFu);

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