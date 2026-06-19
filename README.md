# LzmaSharp

Управляемая реализация на C# для семейства алгоритмов LZMA / LZMA2 и обработки
контейнера 7z. Цель проекта — собственное читаемое управляемое ядро без скрытой
магии работы с памятью.

## Текущий этап

**Этап 2 — encoder / writer (в работе).** Этапы 1, 1.5 и 1.6 завершены.

- этап 1 — базовый decoder-path 7z без AES;
- этап 1.5 — AES / 7zAES decoder-path;
- этап 1.6 — экспериментальный GOST decoder-path;
- этап 2 — запись данных и архивов; развитие собственного LZMA/LZMA2 энкодера.

Полная карта этапов — в [`docs/ROADMAP.md`](docs/ROADMAP.md). Текущий статус writer/encoder —
в [`docs/STAGE2_WRITER_STATUS.md`](docs/STAGE2_WRITER_STATUS.md).

## Что уже работает

### Чтение и распаковка 7z

Decoder-path поддерживает методы и фильтры: `Copy`, `LZMA`, `LZMA2`, `Delta`, `Swap2`,
`Swap4`, BCJ-фильтры (`x86`, `ARM`, `ARMT`, `ARM64`, `PPC`, `SPARC`, `IA64`), `BCJ2`,
`BZip2`, `PPMd`, `Deflate`, `Deflate64`. Также поддержаны AES-сценарии чтения и
экспериментальная GOST-ветка (только чтение).

Контур распаковки валидирует пути вывода до записи на диск: отклоняются абсолютные и
небезопасные пути, выход за пределы целевой директории, зарезервированные имена Windows,
коллизии и конфликты структуры. Подробности — в [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

### Запись 7z (этап 2)

Базовый writer через `SevenZipArchiveWriter.BuildArchive(...)` умеет писать пустой архив,
пустые файлы и директории, непустые файлы (метод `Copy` или **`LZMA2`** на выбор через
`SevenZipWriterCompressionMethod`), mixed-сценарии, безопасные вложенные `/`-paths, а также
`FilesInfo.WinAttrib` и `FilesInfo.MTime`.

Энкодер даёт **реальное сжатие**: match finder питает `LzmaAloneEncoder.Encode(...)` (`.lzma`)
и LZMA2-writer. Сжатые `.lzma` и `.7z` (LZMA2) распаковываются настоящими **7-Zip** и **`xz`**
побайтово. Дальнейшие шаги — rep-дистанции, lazy parsing и производительность
(см. [`docs/STAGE2_WRITER_STATUS.md`](docs/STAGE2_WRITER_STATUS.md) и
[`docs/ENCODER_MVP_PLAN.md`](docs/ENCODER_MVP_PLAN.md)).

## Принципы разработки

- Маленькие проверяемые шаги; следующий шаг — после того, как предыдущий работает.
- Сначала корректность и тесты, потом оптимизация.
- Понятный и честный C# без скрытой магии.
- Комментарии, XML-документация и заголовки коммитов — на русском языке.
- Документация в `docs/` — основной источник правды о состоянии проекта.

## Структура репозитория

```text
LzmaSharp.sln

src/
  Lzma.Core/              # Основная библиотека
    Checksums/            # CRC и вспомогательные вычисления
    Lzma1/                # LZMA / LZMA-Alone: декодер и энкодер
    Lzma2/                # LZMA2: декодер и энкодер
    SevenZip/             # 7z: чтение, декодирование, распаковка, writer

tests/
  Lzma.Core.Tests/        # Тесты на xUnit (Checksums / Lzma1 / Lzma2 / SevenZip)

docs/                     # Документация (см. ниже)

vendor/
  lzma-sdk/               # Эталонные исходники для сверки
```

## Документация

- [`docs/ROADMAP.md`](docs/ROADMAP.md) — единый план и карта этапов;
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — архитектура, пайплайн, контракты, безопасность;
- [`docs/STAGE1_STATUS.md`](docs/STAGE1_STATUS.md) — статус decoder-path (этап 1);
- [`docs/STAGE15_AES_STATUS.md`](docs/STAGE15_AES_STATUS.md) — статус AES (этап 1.5);
- [`docs/STAGE16_GOST_STATUS.md`](docs/STAGE16_GOST_STATUS.md) — статус GOST (этап 1.6);
- [`docs/STAGE2_WRITER_STATUS.md`](docs/STAGE2_WRITER_STATUS.md) — статус writer / encoder (этап 2);
- [`docs/TESTS_PLAN.md`](docs/TESTS_PLAN.md) — философия и правила тестирования;
- [`docs/ENCODER_MVP_PLAN.md`](docs/ENCODER_MVP_PLAN.md) — план доведения энкодера до реального сжатия;
- [`docs/PERFORMANCE_PLAN.md`](docs/PERFORMANCE_PLAN.md) — план по производительности.

## Сборка и тесты

Требования: .NET SDK 10.

```bash
dotnet build
dotnet test
```

## Внешние зависимости

У `Lzma.Core` **нет внешних codec-зависимостей** — все методы декодируются и кодируются
собственными управляемыми реализациями:

- `Deflate`, `Deflate64` — `DeflateDecoder` (RFC 1951 + режим Deflate64), `DeflateEncoder`;
- ZIP-контейнер — `ZipReader` / `ZipWriter` (Store + Deflate);
- `BZip2` — `BZip2Decoder` / `BZip2Encoder`;
- `PPMd` — `Ppmd7Decoder` / `Ppmd7Encoder` (PPMd var.H / 7z; поток энкодера бит-в-бит
  совпадает с настоящим 7-Zip).

`SharpZipLib` и `SharpCompress` полностью удалены из production. `SharpZipLib` оставлен только
в тест-проекте как эталон для round-trip сверки BZip2. Сторонние компоненты и их лицензии
перечислены в [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
