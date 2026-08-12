# LzmaSharp: SEC-002 characterization — создание архива (read-only)

> Статус: characterization завершён; описанный ниже дефект устранён staged-записью
> (HEAD `10377e2`, коммиты `63186c8`, `baf4768`, `0cb44de`, `10377e2`).
> Текущий статус реализации и known limitations — в
> `SECURITY_REMEDIATION_PLAN.md` §4.5. Разделы 1–6 ниже оставлены как историческая
> фиксация дефекта и не редактируются.
>
> Дата: 2026-08-11. HEAD: `680b1b8` (ветка `main`).
>
> Основание: `SECURITY_REMEDIATION_PLAN.md` §4.1 (задачи 1–4).

## 1. Path-based create entry points

Все создания архива в путь идут через UI-слой (`IArchiveService` /
`LzmaArchiveService`). Ядро (`Lzma.Core`) path-based create API не имеет:
все `Build*ArchiveToStream` и `ZipStreamWriter.Write` принимают `Stream`.

| # | Entry point | file:line | Механизм | Сценарий |
|---|---|---|---|---|
| E1 | `WriteArchiveAsync(byte[], path)` | `src/Lzma.Ui/Services/LzmaArchiveService.cs:166` | `File.WriteAllBytes` (стр. 172) | in-memory сборка → запись байтов; вызов из `MainViewModel.cs:1734` после `CreateArchiveAsync` (1724) |
| E2 | `CreateArchiveToFileAsync(entries, destinationPath, method, …, volumeSize, password)` | `src/Lzma.Ui/Services/LzmaArchiveService.cs:187` | `FileStream(destinationPath, FileMode.Create)` (стр. 207) либо `VolumeSpanningWriteStream` при `volumeSize > 0` | потоковое создание 7z; диспетчер `Lzma2`/`Auto`/`Bcj2`/`Aes`/`Ppmd`/`Copy`; вызов из `MainViewModel.cs:1828` |
| E3 | `CreateZipToFileAsync(entries, destinationPath, …, password)` | `src/Lzma.Ui/Services/LzmaArchiveService.cs:346` | `FileStream(destinationPath, FileMode.Create)` (стр. 361) → `ZipStreamWriter.Write` | потоковое создание ZIP, опционально WinZip-AES; вызов из `MainViewModel.cs:1912` |

Верхний уровень UI: `CreateCommand` → `CreateFromFilesAsync`
(`MainViewModel.cs:1609`), `CreateFromFolderCommand` → `CreateFromFolderAsync`
(`MainViewModel.cs:1621`); обе ветвятся на streaming-путь (ссылки на файлы)
и in-memory-путь (байты), формат ZIP уходит в `CreateZipStreamingAsync`
(`MainViewModel.cs:1881`).

Единственный носитель пути в ядре — `VolumeSpanningWriteStream`
(`src/Lzma.Core/SevenZip/Writing/VolumeSpanningWriteStream.cs`), создаётся
только из E2.

## 2. Точки записи конечного архива

| # | file:line | API | Назначение | Поведение при ошибке |
|---|---|---|---|---|
| W1 | `LzmaArchiveService.cs:172` | `File.WriteAllBytes(path, archive)` | конечный путь | `IOException`/`UnauthorizedAccessException` → `false`; файл создаётся/обрезается на месте, при срыве записи возможен partial |
| W2 | `LzmaArchiveService.cs:207` | `FileStream(destinationPath, FileMode.Create, ReadWrite)` | конечный путь 7z (один файл) | существующий архив обрезается УЖЕ при открытии (`FileMode.Create`); при ошибке кодировщика остаётся partial на месте старого архива |
| W3 | `VolumeSpanningWriteStream.cs:130` | `FileStream(volumePath, Create/Open, ReadWrite)` | тома `base.001/.002/…` в конечном каталоге (из E2 при `volumeSize > 0`) | новый том — `Create` (обрезает одноимённый старый); при сбое/отмене остаются partial-тома, удалений нет; `Dispose` — только `Flush`+`Close` |
| W4 | `LzmaArchiveService.cs:361` | `FileStream(destinationPath, FileMode.Create)` → `ZipStreamWriter.Write` | конечный путь ZIP | как W2: truncation при открытии, partial при ошибке |

Отсутствует (проверено grep по `src/` на HEAD `680b1b8`):

- `File.Move` / `File.Replace` / `File.Copy` — нет нигде: атомарного
  publish/swap не существует;
- `Path.GetTempFileName` / `Path.GetTempPath` / `.tmp` / staging — нет:
  писать некуда, кроме конечного пути;
- `File.Delete` в create-путях — нет: partial-файл/тома не подчищаются
  (`File.Delete` существует только в extract-путях как rollback распаковки —
  скоуп SEC-001);
- отмена по `CancellationToken` даёт тот же partial-исход: отдельного
  cleanup-кода нет.

## 3. Разрез по контейнерам и шифрованию

- **7z, потоковое создание (Copy/Lzma2/PPMd/Auto/BCJ2)** — E2 → W2.
  Окно уязвимости: с момента открытия файла (`FileMode.Create` обрезает
  существующий архив до первого полезного байта) до `Ok`. Любая ошибка
  энкодера/IO или отмена внутри окна → partial на месте старого архива.
  `Auto` использует solid-вариант (`BuildAutoSolidArchiveToStream`);
  остальные `Build*SolidArchiveToStream` в production-диспетчере не
  используются.
- **7z, multi-volume** — E2 c `volumeSize > 0` → W3. Тома создаются в
  конечном каталоге по ходу записи; при сбое/отмене остаются partial-тома,
  cleanup нет. Постобработка (`MainViewModel.cs:1845`) считает тома только
  при `Ok`.
- **7z + AES-запись** — E2, метод `Aes` → `BuildAesToStream` →
  `BuildAesArchiveToStream` (core) → W2/W3. AES-writer в production
  существует (расхождение с ROADMAP-формулировкой «AES writer пока не
  реализован» — известно как DOC-005). Окно то же; KDF-фаза до первой
  записи диск не трогает.
- **7z + GOST-запись** — `BuildGostEncryptedArchive` (core) — in-memory,
  вызывается только тестами; в UI-enum метода `Gost` нет, до диска в
  production не доходит. При будущем подключении пойдёт через тот же seam.
- **ZIP (Store/Deflate, опц. WinZip-AES)** — E3 → W4. Окно: с открытия до
  `Ok`. In-memory `ZipWriter.Build` — только тесты.
- **7z, in-memory сборка (малые файлы)** — E1-цепочка → W1. Кодирование
  целиком в памяти: при его ошибке запись не начинается; существующий архив
  страдает только от самой записи (один вызов, создание/обрезание на месте).

## 4. Call graph и точки под seam

```text
CreateCommand ─→ CreateFromFilesAsync ─┐
CreateFromFolderCommand ─→ CreateFromFolderAsync ─┤
                                       │
        (байты) ─→ CreateFromSourceAsync
                     → CreateArchiveAsync → SevenZipArchiveWriter.BuildArchive (byte[])
                     → WriteArchiveAsync → File.WriteAllBytes              [W1]
        (ссылки, 7z) ─→ CreateStreamingFromSourceAsync
                     → CreateArchiveToFileAsync
                         → FileStream(dest, Create)                        [W2]
                         | VolumeSpanningWriteStream(dest, volSize)        [W3]
                         → Build{Lzma2|AutoSolid|Bcj2|Aes|Ppmd|Copy}ArchiveToStream
        (ссылки, ZIP) ─→ CreateZipStreamingAsync
                     → CreateZipToFileAsync
                         → FileStream(dest, Create)                        [W4]
                         → ZipStreamWriter.Write
```

Точки под staged-destination seam (§4.3) — все три в одном классе
`LzmaArchiveService`:

| Seam | file:line | Что подменить | Покрывает |
|---|---|---|---|
| S1 | `LzmaArchiveService.cs:205–207` | конструктор output-потока | 7z один файл + multi-volume |
| S2 | `LzmaArchiveService.cs:361` | конструктор output-потока | ZIP |
| S3 | `LzmaArchiveService.cs:172` | `File.WriteAllBytes` | in-memory-путь |

Требования к seam, вытекающие из графа:

- staged output должен быть seekable (сигнатурный патч в writer-ах,
  `Seek` назад в `VolumeSpanningWriteStream`);
- staging на той же файловой системе, что и назначение (перенос при commit
  без копирования);
- commit — публикация только после `Ok`, с заменой существующего назначения;
- rollback/cleanup на всех путях отказа: ошибка энкодера,
  `IOException`/`UnauthorizedAccessException` (сейчас преобразуются в
  `InternalError`/`InvalidData` в catch-блоках до return — rollback должен
  срабатывать раньше), отмена по token;
- multi-volume (staged-набор томов + manifest) — отдельная поздняя фаза
  (§4.4.10), в первый seam не входит.

## 5. Формулировка дефекта SEC-002 (known limitation)

На HEAD `680b1b8` все production-пути создания архива пишут сразу в конечный
путь/тома. Существующий архив по тому же пути уничтожается в момент открытия
выходного файла, до записи первого полезного байта. При любой ошибке или
отмене операции остаётся partial-файл/набор томов; staging, временные файлы,
publish/swap и cleanup отсутствуют. Транзакционное создание архива
**не реализовано**; это known limitation, а не planned semantics.

## 6. Входы для следующих шагов (по SECURITY_REMEDIATION_PLAN §4)

- §4.2: первый красный тест
  `Create_DestinationWriteFailure_PreservesExistingArchive` — должен
  доказанно падать на текущем пути (воспроизведение: отказ операции ПОСЛЕ
  открытия конечного файла — см. шаг RED-1 в работе);
- §4.3: минимальный seam в трёх точках S1–S3;
- §4.4: реализация по шагам (7z один файл → ZIP → multi-volume отдельно).
