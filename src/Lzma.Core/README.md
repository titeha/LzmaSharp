# Ti-Soft.LzmaSharp

Чисто **управляемая** (100% managed C#, **без нативных зависимостей**, без `unsafe`, без P/Invoke)
реализация 7-Zip/LZMA для .NET. Работает везде, где есть .NET (Windows, Linux, macOS, мобильные).

В отличие от большинства решений в экосистеме, это **не обёртка над `7z.dll`** и умеет не только
читать, но и **писать** `.7z` — совместимо с настоящим 7-Zip (проверено интеропом).

## Возможности

- **Чтение и запись `.7z`.**
- Кодеки: **LZMA**, **LZMA2**, **PPMd** (var.H), фильтр **BCJ2** (плотное сжатие `.exe`/`.dll`),
  `Copy`, а также на чтение — Delta/Swap, BCJ (x86/ARM/ARM64/…), BZip2, Deflate/Deflate64.
- **Шифрование 7zAES** (AES-256) на чтение и запись.
- Экспериментальная поддержка **ГОСТ**.
- **Многотомные** архивы (`.7z.001/.002/…`).
- Потоковые API для файлов и архивов **больше 2 ГиБ** (без загрузки целиком в память).
- Автовыбор кодека по содержимому (текст → PPMd, `.exe` → BCJ2, несжимаемое → store).
- **ZIP** — базовое чтение/запись/распаковка (см. ограничения ниже).

## ZIP (ограничения)

Поддержка ZIP пока **частичная** — для полноценной работы используйте `.7z`:

- Методы сжатия: только **Store (0)** и **Deflate (8)**.
- **Без шифрования** — зашифрованный ZIP не открыть даже с паролем (ни ZipCrypto, ни WinZip-AES).
- **Без ZIP64** — архивы больше 4 ГиБ либо с более чем 65535 записями не поддержаны.
- Только **in-memory** (≤ 2 ГиБ на архив) — потокового пути, как у `.7z`, для ZIP пока нет.

Планируется расширить со временем.

## Установка

```
dotnet add package Ti-Soft.LzmaSharp
```

## Быстрый старт

Распаковка `.7z` в память:

```csharp
using Lzma.Core.SevenZip;

byte[] archiveBytes = File.ReadAllBytes("data.7z");

if (SevenZipArchiveDecoder.DecodeToEntries(archiveBytes, out SevenZipDecodedEntry[] entries)
    == SevenZipArchiveDecodeResult.Ok)
{
    foreach (SevenZipDecodedEntry entry in entries)
        if (!entry.IsDirectory)
            Console.WriteLine($"{entry.Name}: {entry.Bytes.Length} байт");
}
```

Создание `.7z`:

```csharp
using Lzma.Core.SevenZip;

var entries = new[]
{
    new SevenZipArchiveWriterEntry("hello.txt", "Привет, мир!"u8.ToArray()),
};

if (SevenZipArchiveWriter.BuildArchive(entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] archive)
    == SevenZipArchiveWriteResult.Ok)
{
    File.WriteAllBytes("out.7z", archive);
}
```

Извлечение прямо на диск (с проверкой путей и CRC):

```csharp
SevenZipArchiveDecoder.ExtractToDirectory(
    File.ReadAllBytes("data.7z"), SevenZipDecodeOptions.Default,
    destinationDirectory: "out", overwrite: false, out _);
```

## Лицензия

MIT. Эталонные исходники LZMA SDK — public domain; данная реализация — самостоятельный порт.
