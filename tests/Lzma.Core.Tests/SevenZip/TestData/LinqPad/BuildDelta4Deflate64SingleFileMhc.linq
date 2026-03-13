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

	string archivePath = Path.Combine(outputDir, "delta4_deflate64_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_Delta4_Deflate64_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		byte[] input = CreateStereo16SamplesBytes(sampleCount: 16 * 1024); // 65536 байт
		File.WriteAllBytes(Path.Combine(workDir, "delta4_deflate64.bin"), input);

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
		psi.ArgumentList.Add("delta4_deflate64.bin");
		psi.ArgumentList.Add("-t7z");

		// Цепочка: Delta(offset=4) -> Deflate64
		psi.ArgumentList.Add("-m0=Delta:4");
		psi.ArgumentList.Add("-m1=Deflate64");

		psi.ArgumentList.Add("-ms=off");
		psi.ArgumentList.Add("-mhc=on");

		using var p = Process.Start(psi)!;
		string stdout = p.StandardOutput.ReadToEnd();
		string stderr = p.StandardError.ReadToEnd();
		p.WaitForExit();

		new
		{
			psi = psi.FileName,
			args = string.Join(" ", psi.ArgumentList),
			p.ExitCode,
			stdout,
			stderr,
		}.Dump("7z");

		if (p.ExitCode != 0)
			throw new InvalidOperationException("7z завершился с ошибкой.");

		new
		{
			archivePath,
			size = new FileInfo(archivePath).Length,
		}.Dump("Готово");
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

static byte[] CreateStereo16SamplesBytes(int sampleCount)
{
	// 4 байта на “сэмпл”: 16-bit left + 16-bit right (LE).
	byte[] data = new byte[sampleCount * 4];

	for (int i = 0; i < sampleCount; i++)
	{
		ushort left = (ushort)i;
		ushort right = (ushort)(i * 3);

		int pos = i * 4;
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos, 2), left);
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2, 2), right);
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

		return c;
	}

	throw new FileNotFoundException("Не найден 7z.exe (SEVEN_ZIP_EXE/Program Files/PATH).");
}