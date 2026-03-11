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

	string archivePath = Path.Combine(outputDir, "bcj_ppc_deflate_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_BCJPpc_Deflate_Single_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		byte[] fileBytes = BuildExpectedPpcLikeBytes(4096);
		File.WriteAllBytes(Path.Combine(workDir, "ppc.bin"), fileBytes);

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
		psi.ArgumentList.Add("ppc.bin");

		psi.ArgumentList.Add("-t7z");

		// Явно задаём цепочку: PPC -> Deflate
		psi.ArgumentList.Add("-m0=PPC");
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

static byte[] BuildExpectedPpcLikeBytes(int length)
{
	if ((length & 3) != 0)
		throw new ArgumentOutOfRangeException(nameof(length), "Для PPC нужен размер, кратный 4.");

	if (length < 0x400)
		throw new ArgumentOutOfRangeException(nameof(length), "Для теста нужен буфер побольше.");

	var data = new byte[length];

	// PPC NOP = ori r0, r0, 0 = 0x60000000 (big-endian).
	for (int i = 0; i + 4 <= data.Length; i += 4)
		BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i, 4), 0x60000000u);

	// Несколько branch-инструкций, которые PPC-фильтр умеет нормализовать.
	// Делаем именно LK=1, чтобы попасть под паттерн фильтра.
	WritePpcBranch(data, pos: 0x00, target: 0x200, link: true);
	WritePpcBranch(data, pos: 0x40, target: 0x300, link: true);
	WritePpcBranch(data, pos: 0x80, target: 0x180, link: true);

	return data;
}

static void WritePpcBranch(byte[] data, int pos, int target, bool link)
{
	if ((pos & 3) != 0)
		throw new ArgumentException("Позиция PPC-инструкции должна быть кратна 4.", nameof(pos));

	if ((target & 3) != 0)
		throw new ArgumentException("Цель PPC branch должна быть кратна 4.", nameof(target));

	if ((uint)(pos + 4) > (uint)data.Length)
		throw new ArgumentOutOfRangeException(nameof(pos));

	int diff = target - pos;

	if ((diff & 3) != 0)
		throw new ArgumentException("Смещение PPC branch должно делиться на 4.");

	uint instruction = 0x48000000u | ((uint)diff & 0x03FFFFFCu);
	if (link)
		instruction |= 0x00000001u;

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