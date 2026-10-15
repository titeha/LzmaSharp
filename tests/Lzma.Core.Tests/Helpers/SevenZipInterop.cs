using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Lzma.Core.Tests.Helpers;

/// <summary>
/// Поиск исполняемого файла 7-Zip для interop-тестов.
/// Не запускает процессы — только проверяет существование файлов.
/// </summary>
/// <remarks>
/// Порядок поиска (первый найденный побеждает):
/// <list type="number">
///   <item>Переменная окружения <c>SEVENZIP_PATH</c> — если задана, путь обязан существовать.</item>
///   <item><c>7zz</c> в PATH (официальный консольный 7-Zip, ставится CI шагом G2a).</item>
///   <item><c>7z</c> в PATH (пакет дистрибутива, например p7zip-full).</item>
///   <item>На Windows — <c>C:\Program Files\7-Zip\7z.exe</c>.</item>
/// </list>
/// Метод идемпотентный и потокобезопасный.
/// </remarks>
internal static class SevenZipInterop
{
    /// <summary>
    /// Находит исполняемый файл 7-Zip или бросает исключение с подробным сообщением.
    /// </summary>
    /// <returns>Полный путь к найденному исполняемому файлу.</returns>
    /// <exception cref="FileNotFoundException">
    /// Исполняемый файл не найден. Сообщение содержит перечень проверенных кандидатов
    /// и подсказку по установке для трёх ОС.
    /// </exception>
    public static string RequireExecutable()
    {
        var checkedPaths = new List<string>();

        // (a) Переменная окружения SEVENZIP_PATH
        string? envPath = Environment.GetEnvironmentVariable("SEVENZIP_PATH");
        if (!string.IsNullOrEmpty(envPath))
        {
            if (File.Exists(envPath))
                return envPath;
            checkedPaths.Add($"SEVENZIP_PATH → {envPath} (задан, но файл не существует)");
        }

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // (b) 7zz в PATH
        string? found7zz = FindInPath("7zz", isWindows, checkedPaths);
        if (found7zz != null)
            return found7zz;

        // (c) 7z в PATH
        string? found7z = FindInPath("7z", isWindows, checkedPaths);
        if (found7z != null)
            return found7z;

        // (d) На Windows — стандартный путь установки
        if (isWindows)
        {
            string defaultWinPath = @"C:\Program Files\7-Zip\7z.exe";
            if (File.Exists(defaultWinPath))
                return defaultWinPath;
            checkedPaths.Add(defaultWinPath + " (не существует)");
        }

        // Ничего не найдено — бросаем исключение с подробным сообщением
        var message = new StringBuilder();
        message.AppendLine("Не найден исполняемый файл 7-Zip для interop-теста.");
        message.AppendLine();
        message.AppendLine("Проверенные кандидаты:");
        foreach (string entry in checkedPaths)
            message.AppendLine("  • " + entry);
        if (string.IsNullOrEmpty(envPath))
            message.AppendLine("  • SEVENZIP_PATH не задан");
        message.AppendLine();
        message.AppendLine("Как установить 7-Zip:");
        message.AppendLine("  Linux:   скачать официальный 7zz из релиза ip7z/7zip на GitHub");
        message.AppendLine("           или установить пакет p7zip-full / 7zip из репозитория дистрибутива.");
        message.AppendLine("  macOS:   скачать официальный 7zz из релиза ip7z/7zip на GitHub");
        message.AppendLine("           или установить через Homebrew: brew install 7zip");
        message.AppendLine("  Windows: установить 7-Zip с https://www.7-zip.org/");
        message.AppendLine("           или задать переменную окружения SEVENZIP_PATH с полным путём к исполняемому файлу.");

        throw new FileNotFoundException(message.ToString());
    }

    /// <summary>
    /// Ищет исполняемый файл с заданным именем в каталогах из переменной PATH.
    /// На Windows дополнительно проверяет расширение .exe.
    /// </summary>
    private static string? FindInPath(string name, bool isWindows, List<string> checkedPaths)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return null;

        string[] dirs = pathVar.Split(Path.PathSeparator);
        foreach (string dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;

            if (isWindows)
            {
                string candidateExe = Path.Combine(dir, name + ".exe");
                if (File.Exists(candidateExe))
                    return candidateExe;
            }
        }

        // Записываем, что искали в PATH (для диагностического сообщения)
        checkedPaths.Add($"{name} в PATH (не найден)");
        return null;
    }
}
