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

	string archivePath = Path.Combine(outputDir, "bcj2_solid_a_empty_b_lzma_d1m_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_BCJ2_Solid_X86_LZMA_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		byte[] aBytes = BuildExpectedX86LikeBytes(
			length: 4096,
			fill: 0x90,
			target1: 0x200,
			target2: 0x300,
			target3: 0x180);

		byte[] bBytes = BuildExpectedX86LikeBytes(
			length: 6000,
			fill: 0xCC,
			target1: 0x280,
			target2: 0x340,
			target3: 0x1C0);

		File.WriteAllBytes(Path.Combine(workDir, "a.bin"), aBytes);
		File.WriteAllBytes(Path.Combine(workDir, "empty.bin"), Array.Empty<byte>());
		File.WriteAllBytes(Path.Combine(workDir, "b.bin"), bBytes);

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

		// BCJ2 + 3 LZMA producer-coder'а.
		psi.ArgumentList.Add("-m0=BCJ2");
		psi.ArgumentList.Add("-m1=LZMA:d=1m");
		psi.ArgumentList.Add("-m2=LZMA:d=64k");
		psi.ArgumentList.Add("-m3=LZMA:d=64k");

		// Bind'ы для BCJ2:
		// s0 -> coder1, s1 -> coder2, s2 -> coder3, s3 остаётся raw packed stream.
		psi.ArgumentList.Add("-mb0:1");
		psi.ArgumentList.Add("-mb0s1:2");
		psi.ArgumentList.Add("-mb0s2:3");

		// Один solid block на всё содержимое.
		psi.ArgumentList.Add("-ms=on");
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
			ASize = aBytes.Length,
			BSize = bBytes.Length,
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

static byte[] BuildExpectedX86LikeBytes(int length, byte fill, int target1, int target2, int target3)
{
	var data = new byte[length];
	data.AsSpan().Fill(fill);

	WriteRel32(data, pos: 0x00, opcode: 0xE8, target: target1);
	WriteRel32(data, pos: 0x40, opcode: 0xE9, target: target2);
	WriteRel32(data, pos: 0x80, opcode: 0xE8, target: target3);

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

		return candidate;
	}

	throw new FileNotFoundException(
		"Не найден 7z.exe. " +
		"Укажи путь через переменную окружения SEVEN_ZIP_EXE " +
		"или установи 7-Zip в стандартную папку.");
}