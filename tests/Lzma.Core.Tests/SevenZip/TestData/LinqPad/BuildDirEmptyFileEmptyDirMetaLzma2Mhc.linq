<Query Kind="Program" />

// LINQPad 9
// Язык: C# Program

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

	string archivePath = Path.Combine(outputDir, "dir_emptyfile_emptydir_meta_lzma2_mhc.7z");

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharp_DirEmptyMeta_" + Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		string dirPath = Path.Combine(workDir, "dir");
		string helloPath = Path.Combine(dirPath, "hello.bin");
		string emptyFilePath = Path.Combine(workDir, "empty.txt");
		string emptyDirPath = Path.Combine(workDir, "emptydir");

		Directory.CreateDirectory(dirPath);
		Directory.CreateDirectory(emptyDirPath);

		File.WriteAllBytes(helloPath, MakePattern(1024, mul: 17, add: 3));
		File.WriteAllBytes(emptyFilePath, Array.Empty<byte>());

		DateTime helloMTimeUtc = new DateTime(2024, 05, 06, 07, 08, 09, DateTimeKind.Utc);
		DateTime emptyFileMTimeUtc = new DateTime(2023, 04, 03, 02, 01, 00, DateTimeKind.Utc);
		DateTime emptyDirMTimeUtc = new DateTime(2022, 11, 10, 09, 08, 07, DateTimeKind.Utc);

		File.SetLastWriteTimeUtc(helloPath, helloMTimeUtc);
		File.SetLastWriteTimeUtc(emptyFilePath, emptyFileMTimeUtc);
		Directory.SetLastWriteTimeUtc(emptyDirPath, emptyDirMTimeUtc);

		if (OperatingSystem.IsWindows())
		{
			// hello.bin -> ReadOnly
			File.SetAttributes(helloPath, FileAttributes.ReadOnly | FileAttributes.Archive);

			// empty.txt -> Hidden
			File.SetAttributes(emptyFilePath, FileAttributes.Hidden | FileAttributes.Archive);

			// emptydir -> Hidden (бит Directory выставит сама файловая система)
			File.SetAttributes(emptyDirPath, FileAttributes.Hidden | FileAttributes.Directory);
		}

		if (File.Exists(archivePath))
			File.Delete(archivePath);

		Run7z(sevenZipExe, workDir, new[]
		{
			"a",
			archivePath,
			"dir",
			"empty.txt",
			"emptydir",
			"-t7z",
			"-r",
			"-m0=LZMA2:d=1m",
			"-mhc=on",
		});

		// Полезно сразу посмотреть технический листинг.
		Run7z(sevenZipExe, workDir, new[]
		{
			"l",
			archivePath,
			"-slt",
		});

		new
		{
			archivePath,
			size = new FileInfo(archivePath).Length,
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
			// При необходимости временную папку можно удалить вручную.
		}
	}
}

static void Run7z(string sevenZipExe, string workingDir, string[] args)
{
	var psi = new ProcessStartInfo
	{
		FileName = sevenZipExe,
		WorkingDirectory = workingDir,
		RedirectStandardOutput = true,
		RedirectStandardError = true,
		UseShellExecute = false,
		CreateNoWindow = true,
	};

	foreach (string arg in args)
		psi.ArgumentList.Add(arg);

	using var process = Process.Start(psi)!;

	string stdout = process.StandardOutput.ReadToEnd();
	string stderr = process.StandardError.ReadToEnd();

	process.WaitForExit();

	new
	{
		Args = string.Join(" ", psi.ArgumentList),
		process.ExitCode,
		stdout,
		stderr,
	}.Dump("7z");

	if (process.ExitCode != 0)
		throw new InvalidOperationException("7z завершился с ошибкой.");
}

static byte[] MakePattern(int length, int mul, int add)
{
	var bytes = new byte[length];
	for (int i = 0; i < bytes.Length; i++)
		bytes[i] = unchecked((byte)(i * mul + add));
	return bytes;
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