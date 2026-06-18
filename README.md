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
пустые файлы и директории, непустые файлы через `Copy`, mixed-сценарии, безопасные
вложенные `/`-paths, а также `FilesInfo.WinAttrib` и `FilesInfo.MTime`. Результаты
проверяются round-trip и структурными тестами через существующий decoder-path.

Низкоуровневый LZMA/LZMA2 энкодер реализован и протестирован, но **match finder пока нет** —
поэтому произвольные данные сжимаются только через `Copy` / literal-only. Доведение до
реального сжатия — ближайшая цель этапа 2 (см. [`docs/STAGE2_WRITER_STATUS.md`](docs/STAGE2_WRITER_STATUS.md)
и [`docs/ENCODER_MVP_PLAN.md`](docs/ENCODER_MVP_PLAN.md)).

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

## Временные внешние зависимости

На текущем этапе допускаются узкие управляемые зависимости для отдельных методов 7z:

- `SharpZipLib` — декодирование `BZip2`;
- `SharpCompress` — отдельные методы 7z, например `PPMd` и `Deflate64`.

Долгосрочная цель — постепенно заменить их собственными управляемыми реализациями
(см. этап 2 в [`docs/ROADMAP.md`](docs/ROADMAP.md)). Сторонние компоненты и их лицензии
перечислены в [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
