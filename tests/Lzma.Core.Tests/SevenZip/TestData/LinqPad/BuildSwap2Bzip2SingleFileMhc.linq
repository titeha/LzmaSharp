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

	string archivePath = Path.Combine(outputDir, "swap2_bzip2_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_Swap2_BZip2_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		// Данные: big-endian ramp из UInt16, чтобы Swap2 имел смысл.
		// 16384 * 2 = 32768 байт.
		byte[] input = CreateU16BigEndianRamp(sampleCount: 16 * 1024);
		File.WriteAllBytes(Path.Combine(workDir, "swap2_bzip2.bin"), input);

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
		psi.ArgumentList.Add("swap2_bzip2.bin");
		psi.ArgumentList.Add("-t7z");

		// Цепочка: Swap2 -> BZip2
		psi.ArgumentList.Add("-m0=Swap2");
		psi.ArgumentList.Add("-m1=BZip2");

		psi.ArgumentList.Add("-ms=off");
		psi.ArgumentList.Add("-mhc=on");

		using var p = Process.Start(psi)!;
		string stdout = p.StandardOutput.ReadToEnd();
		string stderr = p.StandardError.ReadToEnd();
		p.WaitForExit();

		new
		{
			Args = string.Join(" ", psi.ArgumentList),
			p.ExitCode,
			stdout,
			stderr,
		}.Dump("7z");

		if (p.ExitCode != 0)
			throw new InvalidOperationException("7z завершился с ошибкой.");

		new { archivePath, size = new FileInfo(archivePath).Length }.Dump("Готово");
	}
	finally
	{
		try { Directory.Delete(workDir, recursive: true); } catch { }
	}
}

static byte[] CreateU16BigEndianRamp(int sampleCount)
{
	byte[] data = new byte[sampleCount * 2];

	for (int i = 0; i < sampleCount; i++)
	{
		BinaryPrimitives.WriteUInt16BigEndian(
			data.AsSpan(i * 2, 2),
			(ushort)i);
	}

	return data;
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

		// "7z.exe" пусть резолвится через PATH.
		return c;
	}

	throw new FileNotFoundException("Не найден 7z.exe (SEVEN_ZIP_EXE/Program Files/PATH).");
}