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

## ZIP

Поддерживается **чтение и запись** `.zip`:

- Методы сжатия: **Store (0)** и **Deflate (8)** — собственный управляемый Deflate.
- **ZIP64** — архивы больше 4 ГиБ и/или с более чем 65535 записями (потоковый путь).
- **Потоковость** — потоковые чтение каталога, создание и извлечение по пути (без загрузки архива в
  память; поддержка архивов больше 2 ГиБ). In-memory путь (`ZipReader`/`ZipWriter`) — до 2 ГиБ.
- **Шифрование WinZip-AES (AES-256)** — открытие и создание зашифрованных ZIP по паролю; совместимо
  с 7-Zip/WinZip (потоковый путь).
- Параллельное сжатие по файлам (загрузка ядер).

Ограничения:

- Legacy **ZipCrypto** (традиционное PKWARE-шифрование) не поддержан — только WinZip-AES.
- **Извлечение** Deflate-члена любого размера (в т.ч. >2 ГиБ) идёт потоком; Store-члены — тоже.
  На **создание** отдельный Deflate-член пока ограничен ~2 ГиБ (потоковое сжатие Deflate — в работе).

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
