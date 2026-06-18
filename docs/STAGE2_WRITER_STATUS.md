# Статус этапа 2 — encoder / writer

Этап 2 в работе. Это авторитетный документ о текущем состоянии writer / encoder-направления.
Карта этапов и общий план — в [`ROADMAP.md`](ROADMAP.md).

Цель этапа — production-ориентированная запись данных и архивов без шифрования. Writer
развивается маленькими проверяемыми шагами рядом с уже стабилизированным decoder-path и не
переписывает его.

## Границы этапа

Входит: развитие LZMA и LZMA2 энкодеров, базовый 7z writer, запись простых архивов,
round-trip тесты и тесты совместимости с декодером (по возможности — с эталонными
инструментами).

Не входит: AES/GOST writer, Magma, полноценный GOST KDF через Стрибог, encrypted header
writer, UI, CLI, тяжёлая оптимизация, streaming writer API, поддержка всех возможностей 7z.

## Состояние энкодера

Низкоуровневая машинерия LZMA-кодирования реализована и покрыта round-trip тестами через
собственный декодер: `LzmaRangeEncoder`, энкодеры литералов / длин / дистанций / bit-tree,
`LzmaAloneEncoder` и `LzmaAloneIncrementalEncoder`, `Lzma2LzmaEncoder` (нарезка на чанки +
COPY-fallback, режимы literal-only и script), `Lzma2CopyEncoder` и инкрементальный вариант.

Реальное сжатие LZMA реализовано. Добавлен match finder (`LzmaMatchFinder`): хеш-цепочки
по 3 байтам, жадный (greedy) разбор, минимальная длина match — 3 байта, дистанция ограничена
размером словаря. `LzmaAloneEncoder.Encode(...)` пропускает данные через match finder и
кодирует их существующим путём (литералы + matches → range coder).

Проверено:

- round-trip нашим декодером (текст, нули, повторяющиеся паттерны, случайные данные);
- повторяющиеся/паттерные данные дают выигрыш по размеру;
- сжатый `.lzma` распаковывается настоящими 7-Zip (`7z e`) и `xz --format=lzma` побайтово
  идентично оригиналу.

LZMA2 поверх LZMA тоже сжимает: `Lzma2LzmaEncoder.Encode(...)` режет данные на чанки
(≤ 64 КБ), каждый чанк независимо сжимает через match finder и выбирает меньший вариант
(LZMA-чанк или COPY-чанк). Режим MVP — словарь сбрасывается на каждом чанке (control 0xE0),
что просто и удобно для будущего распараллеливания. Проверено round-trip нашим LZMA2-декодером
(текст, нули, паттерны, случайные данные, многочанковые входы > 64 КБ) и фактом сжатия.

Текущие ограничения (отдельные поздние шаги):

- разбор только жадный (нет lazy/optimal parsing);
- не используются rep-дистанции (rep0..rep3) — кодируются только обычные matches;
- LZMA2: словарь сбрасывается на каждом чанке (несущий режим между чанками — позже),
  размер чанка ограничен 64 КБ;
- match finder подключён к LZMA-Alone и LZMA2; **интеграция в 7z-writer — следующий шаг**
  (там же — внешняя сверка сжатого `.7z` настоящим 7-Zip);
- оптимизация скорости/памяти — этап 3.

Детальный план LZMA2 — [`ENCODER_MVP_PLAN.md`](ENCODER_MVP_PLAN.md).

## Состояние writer-path

Основной вход — `SevenZipArchiveWriter.BuildArchive(...)`. Входная модель
`SevenZipArchiveWriterEntry` описывает: имя, содержимое, признак директории, опциональные
Windows attributes, опциональное `LastWriteTimeUtc`. Результат — `SevenZipArchiveWriteResult`.

### Поддержанные сценарии

- пустой архив;
- пустой файл, пустая директория и их смесь;
- один и несколько непустых файлов через `Copy`;
- mixed-набор: empty entries и непустые `Copy`-файлы в одном архиве;
- безопасные вложенные `/`-paths (включая явную директорию + файл внутри неё).

Маршрутизация после входной validation:

- нет entry → пустой архив;
- все entry без файловых данных → empty-entry path;
- есть непустой файл → `Copy` path;
- некорректные входные данные → `InvalidData`.

Для empty-entry path формируется только header-структура (`FilesInfo`, `EmptyStream`,
`EmptyFile`, имена). Для `Copy` path packed data, `PackInfo` и `UnpackInfo` формируются
только для непустых файлов; `FilesInfo` описывает все entry. Для непустых файлов CRC
считается в `PackInfo` (packed stream) и `UnpackInfo` (folder stream). Вложенный path
сохраняется в `FilesInfo.Names` как имя entry (например, `dir/file.bin`).

CRC файлов в блок `FilesInfo` **не** пишется: `kCRC` не входит в свойства `FilesInfo` по
формату 7z, и настоящий 7-Zip помечает такой архив как «Unsupported feature». Целостность
непустых файлов уже покрыта folder-CRC в `UnpackInfo` (в `Copy`-раскладке на каждый файл —
свой folder). Архивы writer-а проверены на чтение настоящим 7-Zip (`7z t` / `7z x`) без
предупреждений.

В mixed-сценарии: пустой файл → `EmptyStream = true`, `EmptyFile = true`; пустая директория
→ `EmptyStream = true`, `EmptyFile = false`; непустой файл → `EmptyStream = false`.

### Windows attributes (`FilesInfo.WinAttrib`)

- пишутся для всех entry, `AllAreDefined = true`, `External = false`;
- если `WindowsAttributes` не заданы явно — default по типу entry: файл → `Archive` (0x20),
  директория → `Directory` (0x10);
- если заданы явно — пишется переданное значение после validation;
- validation: директория должна иметь `Directory` bit, файл — не должен; иначе `InvalidData`.

### Время модификации (`FilesInfo.MTime`)

- задаётся через `SevenZipArchiveWriterEntry.LastWriteTimeUtc`, хранится как Windows FILETIME;
- пишется только `MTime`; `CTime` и `ATime` пока не пишутся;
- значение должно иметь `DateTimeKind.Utc`; `Local` / `Unspecified` и непредставимое в
  FILETIME значение → `InvalidData`;
- не задано ни у одного entry → `MTime` не пишется;
- задано у всех → `AllAreDefined = true`; задано частично → пишется defined bit-vector;
- `External = false`, timestamps — в порядке entry, только для defined entry.

### Контракт имён и путей entry

Полный path состоит из сегментов, разделённых `/`. Каждый сегмент должен быть непустым, не
из одних пробелов, без `\0` и `\`, без недопустимых Windows-символов (`< > : " | ? *`), без
управляющих символов `0x00..0x1F`, не зарезервированным Windows-именем, не заканчиваться
точкой или пробелом. `/` — только разделитель сегментов.

Writer дополнительно отклоняет: абсолютные пути; завершающий `/`; пустые сегменты;
сегменты `.` / `..`; точные дубли имён и имена, отличающиеся только регистром; конфликт
файла и директории (в т.ч. по регистру); директорию с данными; path, где parent-entry
существует как файл. Проверка parent-entry — без учёта регистра, поэтому `Dir` (директория)
+ `dir/file.txt` разрешено, а `Dir` (файл) + `dir/file.txt` → `InvalidData`.

Зарезервированные Windows-имена (без учёта регистра, в т.ч. с расширением): `CON`, `PRN`,
`AUX`, `NUL`, `COM1`…`COM9`, `LPT1`…`LPT9`. Похожие, но не зарезервированные (`COM10.txt`,
`CONSOLE.txt`, `auxiliary.txt`) разрешены. Точка и пробел внутри имени разрешены
(`file.name.txt`, `file name.txt`, `.config`).

Это сделано намеренно, чтобы writer не создавал архивы с конфликтами при безопасной
распаковке на case-insensitive файловых системах.

### Контракт ошибок

- `Ok` — архив построен;
- `InvalidData` — некорректные входные данные (см. контракты выше);
- `NotSupported` — сценарий распознан, но не входит в текущий writer-path;
- `InternalError` — неожиданное внутреннее состояние.

Writer не должен молча создавать частично некорректный архив.

## Тестовое покрытие

Каждый поддержанный сценарий закреплён тестом; источник правды по конкретным тестам — код
в `tests/Lzma.Core.Tests` (`Lzma1`, `Lzma2`, `SevenZip`). Покрытие по группам:

- **round-trip через decoder-path** — пустой архив, пустые файлы/директории и их смесь,
  один и несколько `Copy`-файлов, mixed-сценарии, вложенные entry;
- **структурные проверки через `SevenZipArchiveReader`** — `SignatureHeader`, packed data,
  `PackInfo` / `UnpackInfo` (включая несколько packed stream-ов и folder-ов), `Copy` coder,
  `FilesInfo`, `EmptyStream` / `EmptyFile` / `Crc` bit-vector-ы (включая граничные размеры и
  второй байт), `WinAttrib` payload, `MTime` payload;
- **CRC** — packed stream, folder stream и файл, в т.ч. для нескольких потоков;
- **negative / `InvalidData`** — `null`, повреждённые packed data и CRC, директория с
  данными, некорректные имена и пути (см. контракт), дубли и конфликты имён, несогласованные
  attributes, не-UTC `LastWriteTimeUtc`;
- **энкодер** — literal-only и script-кодирование LZMA, LZMA-Alone (обычный и
  инкрементальный), LZMA2 (`Copy` и LZMA-чанки) с round-trip проверкой.

Новые writer-тесты добавляются только под конкретный реализуемый сценарий — без
заблаговременного перебора synthetic edge-case комбинаций.

## Пока не поддержано

- реальное сжатие произвольных данных (нет match finder-а);
- LZMA / LZMA2 как coder в writer-path 7z (writer пишет только `Copy`);
- `CTime` / `ATime` и platform-specific attributes кроме `WinAttrib`;
- solid-группировка;
- AES / GOST writer.

## Критерий завершения этапа

Этап 2 завершён, когда в проекте есть production-ориентированный базовый writer с реальным
сжатием, покрытый тестами и согласованный с decoder-path. До этого любые writer-сценарии —
поэтапное развитие, а не полная реализация архиватора.
