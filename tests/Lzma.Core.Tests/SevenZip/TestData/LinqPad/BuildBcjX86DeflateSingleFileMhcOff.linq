<Query Kind="Program" />

// LINQPad 9
// Язык: C# Program

using System.Buffers.Binary;
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

	string archivePath = Path.Combine(outputDir, "bcj_x86_deflate_mhc_off.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_BCJx86_Deflate_Single_NoHeader_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		byte[] fileBytes = BuildExpectedX86LikeBytes(4096);
		File.WriteAllBytes(Path.Combine(workDir, "x86.bin"), fileBytes);

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
		psi.ArgumentList.Add("x86.bin");

		psi.ArgumentList.Add("-t7z");

		// Цепочка: BCJ(x86) -> Deflate
		psi.ArgumentList.Add("-m0=BCJ");
		psi.ArgumentList.Add("-m1=Deflate");

		psi.ArgumentList.Add("-ms=off");
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
		}
	}
}

static byte[] BuildExpectedX86LikeBytes(int length)
{
	var data = new byte[length];
	data.AsSpan().Fill(0x90);

	WriteRel32(data, pos: 0x00, opcode: 0xE8, target: 0x200);
	WriteRel32(data, pos: 0x40, opcode: 0xE9, target: 0x300);
	WriteRel32(data, pos: 0x80, opcode: 0xE8, target: 0x180);

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