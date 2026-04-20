<Query Kind="Program" />

// Генерирует тестовый архив:
// tests/Lzma.Core.Tests/SevenZip/TestData/Real/aes_lzma2_singlefile_pwd_mhe_off.7z
//
// Назначение:
// реальный 7z AES-архив без шифрования заголовков,
// один файл, метод LZMA2, пароль LzmaSharp-AES-Stage15.
//
// Используется тестом:
// SevenZipReal7zAesLzma2Tests

void Main()
{
	string repoRoot = @"G:\Projects\Windows\LzmaSharp";

	string sevenZipExe = FindSevenZipExe();
	string password = "LzmaSharp-AES-Stage15";

	string realDir = Path.Combine(
		repoRoot,
		"tests",
		"Lzma.Core.Tests",
		"SevenZip",
		"TestData",
		"Real");

	Directory.CreateDirectory(realDir);

	string workDir = Path.Combine(
		Path.GetTempPath(),
		"LzmaSharpAesLzma2RealArchive",
		Guid.NewGuid().ToString("N"));

	Directory.CreateDirectory(workDir);

	try
	{
		string inputPath = Path.Combine(workDir, "aes-lzma2-real.txt");
		string archivePath = Path.Combine(realDir, "aes_lzma2_singlefile_pwd_mhe_off.7z");

		string text =
			"LzmaSharp AES LZMA2 real 7z test\r\n"
		  + "LzmaSharp AES LZMA2 real 7z test\r\n"
		  + "LzmaSharp AES LZMA2 real 7z test\r\n"
		  + "0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ\r\n";

		File.WriteAllBytes(
			inputPath,
			Encoding.UTF8.GetBytes(text));

		if (File.Exists(archivePath))
			File.Delete(archivePath);

		Run7z(
			sevenZipExe,
			workDir,
			[
			  "a",
		  "-t7z",
		  "-m0=LZMA2:d=64k",
		  "-mhe=off",
		  "-p" + password,
		  archivePath,
		  inputPath,
			]);

		Run7z(
			sevenZipExe,
			workDir,
			[
			  "t",
		  "-p" + password,
		  archivePath,
			]);

		Run7z(
			sevenZipExe,
			workDir,
			[
			  "l",
		  "-slt",
		  "-p" + password,
		  archivePath,
			]);

		Console.WriteLine("Готово:");
		Console.WriteLine(archivePath);
		Console.WriteLine();
		Console.WriteLine("Пароль:");
		Console.WriteLine(password);
	}
	finally
	{
		TryDeleteTree(workDir);
	}
}

static string FindSevenZipExe()
{
	string[] candidates =
	[
	  @"C:\Program Files\7-Zip\7z.exe",
	@"C:\Program Files (x86)\7-Zip\7z.exe",
	"7z.exe",
  ];

	foreach (string candidate in candidates)
	{
		if (candidate.Equals("7z.exe", StringComparison.OrdinalIgnoreCase))
			return candidate;

		if (File.Exists(candidate))
			return candidate;
	}

	throw new FileNotFoundException("Не найден 7z.exe. Укажи путь вручную в FindSevenZipExe().");
}

static void Run7z(
	string sevenZipExe,
	string workingDirectory,
	IReadOnlyList<string> arguments)
{
	var psi = new ProcessStartInfo
	{
		FileName = sevenZipExe,
		WorkingDirectory = workingDirectory,
		UseShellExecute = false,
		RedirectStandardOutput = true,
		RedirectStandardError = true,
	};

	foreach (string arg in arguments)
		psi.ArgumentList.Add(arg);

	using Process process = Process.Start(psi)
		?? throw new InvalidOperationException("Не удалось запустить 7z.");

	string stdout = process.StandardOutput.ReadToEnd();
	string stderr = process.StandardError.ReadToEnd();

	process.WaitForExit();

	Console.WriteLine("> " + sevenZipExe + " " + string.Join(" ", arguments.Select(QuoteIfNeeded)));
	Console.WriteLine(stdout);

	if (!string.IsNullOrWhiteSpace(stderr))
		Console.WriteLine(stderr);

	if (process.ExitCode != 0)
		throw new InvalidOperationException($"7z завершился с кодом {process.ExitCode}.");
}

static string QuoteIfNeeded(string value)
{
	return value.Contains(' ') ? "\"" + value + "\"" : value;
}

static void TryDeleteTree(string path)
{
	try
	{
		if (Directory.Exists(path))
			Directory.Delete(path, recursive: true);
	}
	catch
	{
		// best-effort cleanup для временного каталога
	}
}