---
doc_kind: current-work-plan
status: active
project: LzmaSharp
last_verified_date: 2026-10-19
main_branch: main
main_commit: 3a55297106e6a978b460faa5bfe3b1a1b85b1041
active_work_branch: fix/sec002-multivolume-transaction
active_work_commit: 78ccc2d
next_task: "B9 (остаток, отдельные шаги в production-файлах): комментарии ядра «атомарн» (:809, :1427) и «ничего не остаётся» (:808–810, :1408–1409), XML-док ZipExtractResult.PathTooLong, мёртвая ветвь TrackAttributeChange; capability-probe (todo #39) отложен владельцем; merge A12 — вручную владельцем"
---

# LzmaSharp: текущее состояние работ и ближайший план

## 1. Назначение

Этот документ является текущей рабочей точкой проекта.

Он фиксирует:

- фактическое состояние основной и рабочей веток;
- уже выполненные изменения;
- открытые риски;
- порядок следующих задач;
- правила выполнения работы маленькими проверяемыми шагами;
- критерии, после которых изменение можно вливать в `main`.

Документ не заменяет исходный security-аудит и архитектурные планы. Он определяет,
**что именно делается сейчас и в каком порядке**.

## 2. Иерархия источников истины

При противоречии применяется следующий порядок:

1. текущий код и блокирующие тесты на указанном commit;
2. этот active work plan;
3. `docs/plans/SECURITY_REMEDIATION_PLAN.md`;
4. архитектурные и мультиплатформенные планы;
5. исторические stage/status-файлы.

Документ должен обновляться после завершения каждого крупного рабочего пакета.
Нельзя считать риск закрытым только по старому статусу в документации.

## 3. Проверенные контрольные точки

### Основная ветка

```text
branch: main
commit: 3a55297106e6a978b460faa5bfe3b1a1b85b1041
date:   2026-08-14
```

После объявления SEC-002 закрытым в `main` в основном добавлялись документы,
правила и промпты для агентов. Незавершённая multi-volume транзакция в `main`
не влита.

### Рабочая ветка

```text
branch: fix/sec002-multivolume-transaction
commit: a6bc98ad3eee1cbe9f6c853687874a45fb364fff
date:   2026-09-24
```

В ней сохранены:

- узкий `IStagedVolumeFileOperations`;
- инъекция файловых отказов;
- backup-фаза существующих управляемых томов;
- rollback ошибок backup-фазы;
- журнал опубликованных новых томов;
- rollback частичной публикации;
- тесты отказа до первого, после первого и на последнем publish;
- M4B: commit point и cleanup operation backups;
- M5: отказ от delete-by-scan и conflict policy для дополнительного `{base}.NNN`;
- M6: lifecycle state machine, manifest-shape validation, staged-existence precheck,
  controlled backup-catch;
- M6-F1: приоритет disposed-guard над null-guard в `SetVolumes`;
- принятый config-follow-up `.gitignore` (пять возвращённых локальных ignore-правил).

Эту ветку нельзя выбрасывать или начинать заново.

## 4. Что уже завершено

### 4.1 Fault-injection stream

`ThrowBeforeCrossingWriteStream` реализован и покрыт тестами:

- sync/async overloads;
- точная граница;
- отмена;
- `leaveOpen`;
- ошибки внутреннего потока;
- поведение после `Dispose`.

### 4.2 Staged-создание одиночных архивов

Основные пути создания одиночного 7z, ZIP и записи готового архива используют
staging вместо немедленного открытия конечного пути через `FileMode.Create`.

Основной сценарий сохранения прежнего архива при ошибке writer покрыт тестами.

### 4.3 Начатая multi-volume транзакция

Feature-ветка уже умеет:

```text
existing managed finals
→ backup phase

staged volumes
→ publish phase

controlled publish failure
→ delete only journaled new finals
→ restore journaled backups
→ clean staged files best-effort
```

### 4.4 SEC002-M4B — завершён

Реализованы:

- commit point после полной публикации нового набора и текущей stale-фазы;
- best-effort cleanup только operation-owned backup-файлов;
- отсутствие rollback после commit point;
- сохранение опубликованного нового набора при ошибке удаления backup;
- отсутствие backup-файлов после обычного успешного commit.

Проверки:

- `StagedVolumeSetTests`: 7 passed;
- `CreateVolumes_SuccessOverLargerOldSet_PublishesAndRemovesStaleVolumes`:
  1 passed;
- `Lzma.Ui.Tests`: 205 passed;
- независимый reviewer: `APPROVE`.

Остаточные риски:

- ownership дополнительных `{base}.NNN`;
- повторный `Commit`;
- manifest validation;
- ошибки rollback;
- crash/power-loss recovery;
- платформенная CI-матрица.

Из этого списка позже закрыто: повторный `Commit` и manifest validation — в M6.
Crash/power-loss recovery и платформенная CI-матрица остаются открытыми.

### SEC002-M5 — завершён

Реализовано:

- удалён successful-path delete-by-scan дополнительных `{base}.NNN`;
- immediate numbered path за новым manifest проверяется до первой мутации;
- при недоказанном ownership операция возвращает существующий `InternalError`;
- существующий многотомный набор сохраняется байт-в-байт;
- файлы после разрыва нумерации не обнаруживаются и не изменяются;
- публичный API не изменён.

Проверки:

- `StagedVolumeSetTests`: 9 passed;
- preservation tests: 2 passed;
- `Lzma.Ui.Tests`: 207 passed;
- full solution: 1906 passed, 0 failed;
- independent reviewer: `APPROVE`.

Остаточные риски:

- numbered path как каталог;
- публичный `DestinationConflict`;
- orphan-файлы после разрыва;
- repeated `Commit`;
- transaction state;
- manifest validation;
- rollback-failure tests;
- crash/power-loss recovery;
- platform CI matrix.

Из этого списка позже закрыто в M6: repeated `Commit`, transaction state,
manifest validation. Rollback-failure tests — M7; crash/power-loss recovery и
platform CI matrix остаются открытыми.

### SEC002-M6 — завершён

Реализовано в `src/Lzma.Ui/Services/StagedVolumeSet.cs`:

- один источник lifecycle-состояния: внутренний enum `State` и поле `_state`;
  состояния: `Created`, `Ready`, `Committing`, `Committed`, `Failed`, `Disposed`;
- `Commit()` — one-shot: повтор после успеха или после любой попытки отказа
  отклоняется `InvalidOperationException` без файловых операций;
- операции после `Dispose()` отклоняются `ObjectDisposedException`, включая
  исправленный случай `SetVolumes(null)` (M6-F1: disposed-guard выполняется раньше
  null-guard); в состоянии `Created` null-аргумент по-прежнему даёт точный
  `ArgumentNullException`, экземпляр после отказа остаётся пригодным;
- `Dispose()` идемпотентен, после `Committed` не выполняет файловых операций,
  read-only свойства остаются читаемыми;
- lexical manifest-shape validation в `SetVolumes()`: пустой список,
  null/empty/whitespace, дубликаты и коллизии staged-пути с финальными путями
  отклоняются `ArgumentException`; сравнение нормализованное, comparer OS-aware
  (`OrdinalIgnoreCase` на Windows, `Ordinal` иначе); оригинальные строки manifest
  сохраняются — это действующий контракт M6.2;
- staged-file existence precheck в `Commit()` до conflict probe, backup, publish и
  Delete (`FileNotFoundException` до первой мутации назначения);
- controlled backup-catch ограничен `IOException` / `UnauthorizedAccessException`;
  non-controlled исключение backup-фазы уходит без controlled rollback и без
  обёртки, повторный `Commit` запрещён;
- M4B commit point (до backup cleanup) и M5 conflict policy сохранены;
  preservation-тесты остаются зелёными.

Проверки на `a6bc98ad3eee1cbe9f6c853687874a45fb364fff` (2026-09-24):

- `dotnet build -c Release --no-incremental`: 0 errors, 67 warnings;
- `StagedVolumeSetTests`: 21 passed;
- preservation tests: 2 passed;
- `Lzma.Ui.Tests`: 219 passed;
- full solution: 1918 passed = `Lzma.Ui.Tests` 219 + `Lzma.Core.Tests` 1699;
- failed: 0; skipped: 0;
- independent final review: `APPROVE` (SEC002-M6.5-FINAL).

### B9 (документационная часть) — платформенная матрица и актуализация рабочего пакета

- Шаг docs-only (production-код и тесты не менялись): раздел 6 этого файла приведён к фактическому
  состоянию (текущий пакет — SEC-001, а не SEC-002; «documentation synchronization» исключён из
  «вне scope» — doc-tail выполняется после каждого шага), front matter актуализирован.
- Documentation lint по списку `docs/plans/04_DOCUMENTATION_AUDIT.md` §7 на current-документах:
  запрещённых формулировок нет (0 совпадений); все вхождения корня «атомарн» — отрицания или
  запреты («атомарности набора имён на уровне ОС нет», «не заявляются»). DOC-001 в части
  документации закрыт; остаток — формулировки в комментариях ядра (ниже).
- Платформенная матрица (отчёт `.agent/reports/B9_PLATFORM_MATRIX.md`; каталог `.agent/` в git
  не входит). Windows 11 Pro (10.0.28000), SDK 10.0.112 при `global.json` 10.0.101, HEAD `b714dc5`:
  `Lzma.Core.Tests` 1760 / 0 / 0, `Lzma.Ui.Tests` 239 / 0 / 0, целевые SEC-001 (Transactional,
  Journal, Preflight, PathLength, PreFlight) 55 / 0 / 0. Linux и macOS — CI runs `39287836382`
  (`78ccc2d`) и `39290490314` (`b714dc5`): ubuntu, windows, macos — success; точные числа по ОС
  недоступны (анонимный доступ к логам и TRX — 403), поэтому в документации они приводятся как
  «ожидается»: `Lzma.Core.Tests` 1752 passed / 6 skipped (всего 1758).
- Не измерено (долг): capability-probe `NAME_MAX`/`PATH_MAX`, семантика `File.Move` и атрибутов
  на ubuntu/macos, хост с `LongPathsEnabled=0`, UNC — todo #39, отложен отдельным решением
  владельца; пункт 2 плана B9 в этой сессии не выполнялся.
- Остаток B9 — отдельные шаги в production-файлах (вне allowlist docs-шага, требуют решения):
  комментарии ядра «с атомарным откатом» (`SevenZipArchiveDecoder.cs:809`), «атомарный откат»
  в XML-доке потокового маршрута (`:1428`), «на диске «ничего не остаётся»» (`:1030`),
  неточный XML-док `ZipExtractResult.PathTooLong` (`Zip/ZipExtractResult.cs:31–43`),
  мёртвая ветвь `TrackAttributeChange` (`StagedExtractionJournal.cs:338`, вызовов в `src` и
  `tests` нет). Comments-only правки и удаление кода — разные шаги (`AGENTS.md` §5).

### todo #44 + B8 — сообщения UI об отказах pre-flight и интеграция маршрутов сервиса

- Шаг todo #44 (коммит `1c09fe1`, дифф 14/2 в `MainViewModel.cs` + 94/0 тесты; MINOR-4 ревью B5b):
  UI показывает отдельные человекочитаемые сообщения для `InsufficientSpace` и `PathTooLong`
  в обоих маршрутах — 7z (`ExtractStatus`) и ZIP (`ZipExtractStatus`); раньше оба кода попадали
  в общую ветвь «ошибка данных или файл уже существует» / «небезопасный путь, конфликт имён».
  В `StreamingExtractStatus` отказы предварительных проверок обрабатываются ДО ветви `encrypted`:
  иначе зашифрованный 7z со слишком длинным именем или без свободного места сообщал бы «неверный
  пароль или повреждённый архив», хотя ядро возвращает эти коды до декодирования и с паролем они
  не связаны. Видимость `ExtractStatus` и `StreamingExtractStatus` расширена `private → internal`
  (по образцу уже `internal` `ZipExtractStatus`; `InternalsVisibleTo Lzma.Ui.Tests` в csproj) —
  публичный API не изменён. Тесты: `tests/Lzma.Ui.Tests/MainViewModelExtractStatusTests.cs`
  (10 тест-кейсов, включая guards прежних формулировок `Ok`/`InvalidData`/`IOError` и подсказки
  про пароль).
- Шаг B8 (коммит `78ccc2d`, только тесты 191/0; production-код НЕ менялся): аудит call graph всех
  семи extract-методов `LzmaArchiveService` (таблица «метод → файл:строка → точка входа ядра →
  staged-ядро → overwrite» и grep-факты — в отчёте `.agent/reports/B8_CALL_GRAPH_REPORT.md`;
  каталог `.agent/` в git не входит). Вывод: оба потоковых маршрута идут через те же staged-ядра,
  что и in-memory (`ZipStreamExtractor.ExtractToDirectory` → `ZipExtractor.ExtractCore`;
  `SevenZipArchiveDecoder.ExtractToDirectoryFromStream` → `ExtractHeaderToDirectory`); других
  потребителей у ядер нет; сервис не выполняет прямых записей на диск; UI вне сервиса ядро не
  вызывает (9 extract-вызовов `MainViewModel` — только через `IArchiveService`); `overwrite: false`
  во всех семи маршрутах. Единственная запись на диск вне журнала стадии —
  `VolumeSpanningWriteStream` (маршрут создания архива, SEC-002).
- Интеграционные тесты отказов через сервис
  (`tests/Lzma.Ui.Tests/LzmaArchiveServiceExtractTransactionalTests.cs`) — characterizeции уже
  реализованного поведения (RED отсутствует, механизм отката и pre-flight существовали до шага):
  порча данных второго члена ZIP (Store, CRC) → `InvalidData`, опубликованный первый член снят
  откатом (не-вакуумность подтверждена порядком отчётов `currentFile` = `[a.bin, b.bin]`), прежние
  байты соседнего файла целы, остатков `.tmp`/`.bak` нет; конфликт имени при `overwrite: false` →
  прежний файл не изменён и не уехал в `.bak`; 7z потоково (`ExtractArchiveFileAsync`) с именем
  300 символов → `PathTooLong` виден через сервис (раньше предварительная проверка длины была
  покрыта только in-memory на уровне ядра).
- Не покрыто (долг): 7z-сценарий «частичное извлечение + отказ второго ВЫБРАННОГО члена» без seam
  не индуцируется (solid-folder декодируется целиком) — остаётся вместе с SEAM-2; аудит call graph
  — снимок строк на `78ccc2d`. Раздел 6 этого файла («текущий рабочий пакет») по-прежнему описывает
  только SEC-002 и исключает extraction — устарел, исправляется в B9.
- Кросс-семейное ревью этих двух шагов не проводилось: B8 не меняет production-код (ревью нужны
  только для production-диффов), production-дифф todo #44 — 14/2 в маппинге сообщений; отдельной
  команды на запуск ревью не было. Зафиксировано честно.
- Факты после шагов (Windows, прогон 2026-10-19 на HEAD `78ccc2d`): Build — 0 ошибок,
  67 предупреждений (baseline; из новых файлов — 0); `Lzma.Ui.Tests` 239 / 0 / 0 (+13 к 226);
  `Lzma.Core.Tests` 1760 / 0 / 0; итого 1999. CI run `39287836382` (HEAD `78ccc2d`, оба коммита
  одним push) — все три job'а success (ubuntu, windows, macos). На POSIX ожидается
  1752 passed / 6 skipped в `Lzma.Core.Tests` (дельта не менялась: ядро не правилось).

### B7 — наблюдение много-членной семантики (TX-EX-05), отмены и hardening отката

- Шаг (коммит `47b9cf4`, дифф 24/4 в `SevenZipArchiveDecoder.cs` + 420/9 тесты):
  production — exception-safe Dispose-цикл потоков folder'а (MINOR-4 ревью B5a: каждый
  поток в отдельном try/catch, первое исключение запоминается и перебрасывается после
  закрытия всех — откат может удалить `.tmp`; переброс через `ExceptionDispatchInfo`
  с сохранением исходного стека — доводка по m-3 ревью B7) и удаление недостижимых внешних
  catch `CleanupAfterCommit` (MINOR-5; недостижимость подтверждена ревью по коду журнала —
  внутренние catch глушат IOException/UACE/NotSupported/Argument/Security).
- Тесты (+5, все — честные характеризации: механизм отката реализован в B3/B5a/B6,
  RED отсутствует, что зафиксировано в отчёте): TX-EX-05 multi-folder (Copy-фикстура,
  порча сырых байтов второго члена → CRC-mismatch второго folder'а ПОСЛЕ публикации
  первого → `InvalidData`; опубликованный `Replaced` восстановлен из `.bak` — байты и
  атрибуты; ветвь, ранее подтверждённая только чтением, теперь наблюдается); отмена ПОСЛЕ
  публикации первого folder'а (инициируется первым ненулевым отчётом прогресса —
  `OperationCanceledException` пробрасывается, опубликованный член восстановлен); отмена
  заранее отменённым token (OCE, sentinel цел); заблокированное назначение
  (`FileShare.None`, Windows; POSIX — видимый skip) → `InternalError` + откат; частичное
  извлечение + атрибуты (долг ревью B6). Доводка тимлида: коды результатов зафиксированы
  точно (`InvalidData`/`InternalError`) + утверждение локализации порчи во втором
  pack-потоке (m-2 ревью B7).
- Кросс-семейное ревью (`deepseek/deepseek-flash`, read-only, пакет
  `.agent/reports/B7_REVIEW_PACKET.md`): **APPROVE_WITH_COMMENTS**, 0 BLOCKING, 2 MAJOR,
  6 MINOR (отчёт `.agent/reports/B7_CROSS_FAMILY_REVIEW_deepseek-flash.md`). M-1 (отмена
  после публикации не наблюдалась) — закрыта доводкой (новый тест, seam не вводился —
  progress-callback); M-2/m-5 (формулировка «root удалён откатом» и путь структурного теста
  в пакете) — исправлены; m-1 (посторонние символы в комментарии теста) — удалены; m-2 —
  коды зафиксированы; m-3 — `ExceptionDispatchInfo`; m-6 — формулировка теста 4. Ревьюер
  подтвердил: маскирование первичного исключения броском из `finally` — pre-existing класс;
  `catch (Exception)` не может проглотить OCE (перебрасывается после цикла); удаление
  внешних catch безопасно (подмножество внутренних); возврат `InvalidData` из блока
  метаданных после commit НЕДОСТИЖИМ (`IsValidFileTime` полностью дублирует построчные
  проверки) — прежний долг снят; Q10=а не требует диагностики «какие файлы опубликованы».
  Долг m-4: формулировки корня «атомарн» в комментариях ядра (:809, :1427) — в B9.
- Не покрыто (долг): отказ второго `Move` внутри `Publish` — без seam практически
  не индуцируем → SEAM-2; Dispose-hardening без регрессионного наблюдения (нет seam для
  инъекции сбоя `FileStream.Dispose`) — принятый риск, зафиксирован здесь и в SDL как
  «hardening без регрессионного наблюдения»; атрибуты на POSIX (#39); частичное извлечение +
  отказ второго ВЫБРАННОГО члена (кандидат B8); UNC/FullPathLimit-сценарии (#39).
- Факты после B7 (Windows, прогон 2026-10-19 на HEAD `47b9cf4`): Build — 0 ошибок,
  0 предупреждений из изменённых файлов (baseline); `Lzma.Core.Tests` 1760 / 0 / 0;
  `Lzma.Ui.Tests` 226 / 0 / 0; итого 1986; точечно Transactional + Journal + Preflight —
  25 passed. CI run `39160779414` (HEAD `47b9cf4`) — все три job'а success. На POSIX
  ожидается 1752 passed / 6 skipped (всего 1758; новый видимый skip — заблокированное
  назначение).

### B6 — атрибуты прежнего файла не мутируются до успеха (TX-EX-06)

- Шаг (коммит `acda556`, дифф 0/26 `SevenZipArchiveDecoder.SafeExtraction.cs` + 0/3
  `SevenZipArchiveDecoder.cs` + 4/3 комментарии журнала + два тестовых файла): закрывает
  MINOR-3 ревью B5a и MINOR-4 ревью B5c. Решение (тимлид, internal API): из 7z-маршрута
  удалён вызов `TryPrepareExistingFileForOverwrite` (ставил `FileAttributes.Normal` на
  назначение до всяких проверок — откат возвращал байты, но ReadOnly/Hidden утрачивались)
  и удалён осиротевший helper — выравнивание с ZIP (там вызов удалён в B3: backup-first
  rename не требует снятия ReadOnly, удаление `.bak` с атрибутом обрабатывает журнал,
  откат возвращает тот же файловый объект с байтами и атрибутами). Согласованный в B1
  capability-probe `File.Move` для read-only источника ОТМЕНЁН как избыточный (rename ZIP
  измерен в CI на трёх ОС с B3) — аддендум в задании `tasks/B_SEC001_TRANSACTIONAL_EXTRACTION.md`
  §B6 и отметка в §4 этого документа.
- Краевое следствие (зафиксировано в SDL): при `overwrite: true` файл с запретом записи
  (ACL), но каталогом с правом rename теперь заменяется (согласованно с ZIP), а
  заблокированное назначение отказывает на публикации (`InternalError` + корректный откат)
  вместо раннего `InvalidData`; потери гарантии нет (такая гарантия не заявлялась),
  тестовое наблюдение — долг B7/probe.
- Тесты: `TX-EX-06` (реальная фикстура, три sentinel с `ReadOnly|Hidden`, неверный пароль +
  `overwrite: true` → байты на всех ОС + атрибуты на Windows; RED до фикса «атрибуты
  утрачены: expected ReadOnly|Hidden, actual Normal»); характеристика «overwrite:true +
  компонент 240 + прежние байты → PathTooLong» (рекомендация ревью B5b); журнальный
  `Откат_ПовторныйВызов…` расширен проверкой сохранности атрибутов через публикацию→откат
  (закрывает пробел ревью «rename-back не измерен»). Установка и проверка атрибутов
  Windows-guarded (Hidden на POSIX нестандартен — todo #39).
- Кросс-семейное ревью (`deepseek/deepseek-flash`, read-only, пакет
  `.agent/reports/B6_REVIEW_PACKET.md`): **APPROVE_WITH_COMMENTS**, 0 BLOCKING, 3 MAJOR,
  5 MINOR (отчёт `.agent/reports/B6_CROSS_FAMILY_REVIEW_deepseek-flash.md`). Код принят
  («возврат к прежнему состоянию не требуется»). MAJOR-1/2 (SDL описывал удалённый механизм)
  — закрыты этим doc-tail; MAJOR-3 (allowlist задания устарел; удаление helper'а сильнее
  оговорки «не менять сигнатуру») — аддендум в задании + запись решения; ссылка ревьюера
  «02_ROADMAP.md §5 не проверить» неверна — матрица в `.agent/` (вне git), строка 320 относит
  decoder к B6. MINOR-1 (XML-док журнала обещал «подключение в B6») — исправлен доводкой
  (comments-only); MINOR-2 (запись согласования probe) — отменена аддендумом; MINOR-3
  (устаревший комментарий теста OverwriteMetadata) — отдельный doc-коммит; MINOR-4/5
  (семантика и смена кода для заблокированного/ACL-назначения) — строки в SDL. Не покрыто
  (долг): заблокированное назначение и «ACL-замена успех» тестом (B7/probe), отказ второго
  `Move` внутри Publish (B7), атрибуты на POSIX (#39), частичное извлечение + атрибуты (B7),
  pre-existing: `InvalidData` из блока метаданных после commit (наблюдение → B7).
- Факты после B6 (Windows, прогон 2026-10-19 на HEAD `acda556`): Build — 0 ошибок,
  0 предупреждений из изменённых файлов (baseline); `Lzma.Core.Tests` 1755 / 0 / 0;
  `Lzma.Ui.Tests` 226 / 0 / 0; итого 1981; точечно JournalRollback + Transactional +
  Real7zExtractOverwrite — 11 passed. CI run `39157021475` (HEAD `acda556`) — все три job'а
  success. На POSIX ожидается 1748 passed / 5 skipped (всего 1753).

### B5c — проверки конфликтов на длинных путях (обёртка HasFileOnPath/HasFileOnDirectoryPath)

- Шаг (коммит `f903162`, дифф 9/3 в `SevenZipArchiveDecoder.SafeExtraction.cs` + 99/1 тесты):
  снимает ограничение, созданное B3b-2b, — общие для ZIP и 7z проверки конфликтов
  (`HasFileOnPath`, `HasFileOnDirectoryPath`) вызывали `File.Exists`/`Directory.Exists` без
  расширенной формы, поэтому на хостах без long-path opt-in конфликт промежуточного сегмента
  на пути от 259 символов давал отказ на уровне ОС (`InternalError`/`IOError`) вместо
  отличимого `InvalidData`. Правка: FS-вызовы переведены на
  `StagedExtractionJournal.ToFileSystemPath` (no-op для коротких путей и не-Windows);
  call sites, журнал, маршруты и публичный API не изменены.
- Тесты (+2 в `SevenZipExtractPreflight.Tests.cs`, секция (c)): конфликт промежуточного
  сегмента на длинном пути → `InvalidData` + прежние байты целы + нет остатков `.tmp`/`.bak`;
  root под файлом на длинном пути (`HasFileOnDirectoryPath`) → `InvalidData` до первой мутации.
  Честная граница (зафиксирована в комментариях тестов): RED на хосте разработки НЕ наблюдаем —
  `LongPathsEnabled=1` маскирует дефект; тесты валидны как регрессии; чистая демонстрация
  дефекта — на хосте без opt-in или capability-probe (todo #39).
- Кросс-семейное ревью (`deepseek/deepseek-flash`, read-only, пакет
  `.agent/reports/B5C_REVIEW_PACKET.md`): **APPROVE_WITH_COMMENTS**, 0 BLOCKING, 2 MAJOR,
  5 MINOR (отчёт `.agent/reports/B5C_CROSS_FAMILY_REVIEW_deepseek-flash.md`). Ревьюер
  подтвердил: полноту обёртки обоих helper'ов, отсутствие утечки расширенной формы
  в сравнения/обход, no-op для коротких путей и POSIX, согласованность с уже обёрнутыми
  вызовами обоих маршрутов, причинность теста (InvalidData может прийти только из :1016),
  приемлемость границы RED при трёх условиях. MAJOR-1 (комментарий теста заявлял
  несуществующее RED-доказательство) — исправлен; MAJOR-2 (SDL/Core README противоречат
  коду после шага) — закрыт этим doc-tail; MINOR-1 (адаптивная длина сегмента фикстуры),
  MINOR-2 (точность XML-доков: порог 259/Windows), MINOR-3 (утверждения об остатках) —
  устранены доводкой; MINOR-4 (`File.SetAttributes` в `TryPrepareExistingFileForOverwrite`
  остаётся необёрнутым — ложный `InvalidData` при overwrite:true на длинных путях на хостах
  без opt-in; формулировка SDL «файловые вызовы маршрута переводятся» уточнена) — B6;
  MINOR-5 (терминология пакета) — исправлен. Долг: ветка `includeSelf: true`
  (запись-каталог) на длинном пути не покрыта, UNC-интеграция (#39), компонент каталога >255
  в середине пути не ловится `IsPathTooLong` (pre-existing наблюдение → B7/долг).
- Факты после B5c (Windows, прогон 2026-10-18 на HEAD `f903162`): Build — 0 ошибок,
  0 предупреждений из изменённых файлов (baseline 67); `Lzma.Core.Tests` 1753 / 0 / 0;
  `Lzma.Ui.Tests` 226 / 0 / 0; итого 1979; точечно Preflight + Transactional +
  ZipExtractPathLength — 34 passed. CI run `39153733782` (HEAD `f903162`) — все три job'а
  success. На POSIX ожидается 1746 passed / 5 skipped (всего 1751).

### B5b — pre-flight места и длины пути для 7z (SEC-001)

- Шаг (коммит `13c5243`, дифф 34/0 `SevenZipArchiveDecodeResult.cs` + 67/0
  `SevenZipArchiveDecoder.cs` + новый тестовый файл): закрывает MAJOR-1/MAJOR-2
  кросс-семейного ревью B5a. Добавлены СТРОГО В КОНЕЦ `SevenZipArchiveDecodeResult`
  (значения неявные) два аддитивных значения: `InsufficientSpace` (5) и `PathTooLong` (6) —
  изменение публичного API утверждено владельцем заранее (D2 = а, PathTooLong подтверждён;
  контракт — `B1_AMENDMENT_DECISIONS.md` §2.2/§3.3).
- Pre-flight блок — первое действие внутри внутреннего `try` (покрыт `finally` с
  `journal.Rollback()`), до создания подкаталогов/стадинга/публикации: место —
  `ComputeRequiredSpace(plan, selected)` (сумма распакованных размеров выбранных
  не-каталогов, пропуск ≤ 0 из недоверенного заголовка, насыщение до `long.MaxValue`)
  + `ZipExtractor.HasInsufficientSpace` (internal, та же сборка; helper намеренно не
  переносился — минимальный дифф, перенос — долг архитектурного разделения); длина —
  `IsPathTooLong(root, 0)` + по каждой выбранной записи (файлы с суффиксом стадии
  21 символ, каталоги 0). Невыбранные записи в проверке длины не участвуют (частичное
  извлечение не отклоняется из-за неизвлекаемого длинного имени — доводка тимлида + тест).
- Root-каталог по-прежнему создаётся до проверок (~:932, отслеживается журналом, удаляется
  откатом) — порядок намеренно не менялся (долг G4); при слишком длинном пути назначения
  возвращается `InternalError`, а не `PathTooLong` (честно зафиксировано в XML-доке).
- Тесты (новый файл `SevenZipExtractPreflight.Tests.cs`, 11): end-to-end `PathTooLong`
  (компонент 300 символов; пороговый компонент 240 — изолирует механизм суффикса стадии
  по MAJOR-1 ревью B5b), частичное извлечение с невыбранным длинным именем → `Ok`,
  guard enum (значения 0..6, новые в конце), 7 unit-тестов `ComputeRequiredSpace`
  (сумма, каталоги, невыбранные, ≤ 0, насыщение, пустой план, EmptyFile). Честная
  граница: end-to-end-триггер `InsufficientSpace` для 7z отсутствует (нет шва инъекции
  размеров, заполнение диска запрещено) — аналог «честной границы» ZIP-тестов.
- Кросс-семейное ревью (`deepseek/deepseek-flash`, read-only, пакет
  `.agent/reports/B5B_REVIEW_PACKET.md`): **APPROVE_WITH_COMMENTS**, 0 BLOCKING, 1 MAJOR,
  7 MINOR (отчёт `.agent/reports/B5B_CROSS_FAMILY_REVIEW_deepseek-flash.md`). MAJOR-1
  (тест-300 не изолирует механизм +21) — закрыт доводкой тимлида (тест компонента 240);
  MINOR-1/2/3 (точность комментария, off-by-one и ширина XML-дока, оборванный разделитель)
  — исправлены доводкой (комментарии/док-текст, без изменения поведения); MINOR-4 (UI
  отображает новые коды общим сообщением «ошибка данных или файл уже существует») —
  todo #44; MINOR-5 (зависимость SevenZip→Zip) — архитектурный долг; root-порядок
  (MINOR-6) и родительские каталоги вложенного destination вне журнала (MINOR-7) — долг
  G4; тест «overwrite:true + компонент 240 + прежние байты сохранены» и проверка
  компонентов-предков — в B6/отдельный шаг. Production-поведение после ревью не менялось
  (доводка — только комментарии/XML-доки/тесты).
- Факты после B5b (Windows, прогон 2026-10-18 на HEAD `13c5243`): Build — 0 ошибок,
  предупреждений из изменённых файлов нет (baseline 67); `Lzma.Core.Tests` 1751 / 0 / 0;
  `Lzma.Ui.Tests` 226 / 0 / 0; итого 1977; точечно Preflight + Transactional — 13 passed.
  RED-доказательство (исполнитель): до pre-flight блока тест длинного имени падал
  с `InternalError` при ожидаемом `PathTooLong`. CI run `39146269884` (HEAD `13c5243`) —
  все три job'а success. На POSIX ожидается 1744 passed / 5 skipped (всего 1749).

### B5/B5a — стадинг и публикация folder-а в 7z-маршруте (SEC-001)

- RED (коммит `9340902`): два блокирующих теста `TX-EX-04`
  (`tests/Lzma.Core.Tests/SevenZip/SevenZipExtractTransactional.Tests.cs`) на реальном архиве
  `TestData/Real/aes_lzma2_solid_multifile_pwd_mhe_off.7z` (AES-256 + LZMA2, один solid folder;
  члены `alpha.txt`, `nested/beta.txt`, `empty.bin`; извлечение с неверным паролем и
  `overwrite: true`). До фикса падали по правильной причине — потеря прежних байт.
- Fix (коммит `68bbfaf`, дифф 61/44 в одном production-файле `SevenZipArchiveDecoder.cs`
  и 204/0 тесты): журнал `StagedExtractionJournal` (введён в B3 для ZIP) заменил списки
  `createdFiles`/`createdDirs`; data-файлы folder-а пишутся в staged-пути
  (`{имя}.{16 hex}.tmp`); публикация folder-а выполняется ПОСЛЕ `finally` с `Dispose` всех
  потоков и только при `decodeFolder == Ok` и
  `routing.IsComplete && !SizeOverflow && !CrcMismatch`; пустые файлы публикуются в общей
  фазе после всех folder-ов; откат — `journal.Rollback()` с восстановлением прежних байт
  из `.bak`; `Commit()` остался до применения метаданных. Доводка тимлида: 10 необёрнутых
  FS-вызовов ядра переведены на `StagedExtractionJournal.ToFileSystemPath` (`:914`, `:1020`,
  `:1024`, `:1262/:1264`, `:1291/:1293`, `:1323/:1325`, `:1344`) — устранена асимметрия
  с журналом, ослаблявшая раннюю охрану `overwrite: false` на длинных путях.
- Не менялись: `SubstreamRoutingWriter`, `StagedExtractionJournal`, ZIP-маршрут, enum'ы,
  публичный API, `src/Lzma.Ui`.
- Кросс-семейное ревью (`deepseek/deepseek-flash`, read-only, пакет
  `.agent/reports/B5A_REVIEW_PACKET.md`): **APPROVE_WITH_COMMENTS**, 0 BLOCKING, 2 MAJOR,
  6 MINOR (отчёт `.agent/reports/B5A_CROSS_FAMILY_REVIEW_deepseek-flash.md`). Ревьюер
  подтвердил: `Dispose` до `Publish` (включая раскрутку стека), сохранность порядка сегментов
  routing, отсутствие публикаций для невыбранных подпотоков, восстановление уже опубликованных
  файлов при откате, невозможность «частичной публикации» пустых файлов. Решения тимлида
  по находкам: MAJOR-1 (имя стадии +21 символ, pre-flight длины пути для 7z отсутствует —
  компонент имени 235–255 может дать `InternalError` вместо прежнего `Ok`) и MAJOR-2
  (пик места ~2× при перезаписи, pre-flight места отсутствует) — зафиксированы как
  ограничения в `docs/SDL.md`, закрываются шагом B5b; MINOR-3 (атрибуты прежнего файла
  не восстанавливаются из-за `TryPrepareExistingFileForOverwrite`) — B6; MINOR-4
  (Dispose-цикл не exception-safe) и MINOR-5 (недостижимый внешний catch
  `CleanupAfterCommit`) — долг к B6/B7; MINOR-2 (устаревшие комментарии RED-тестов) —
  исправлены отдельным doc-коммитом; MINOR-1 (пакет ревью устарел относительно HEAD) —
  аддендум в пакете; MINOR-6 (rename меняет идентичность заменяемого файла) — строка
  в `docs/SDL.md`.
- Факты после B5a (Windows, прогон 2026-10-16 на HEAD `68bbfaf`): Rebuild — 0 ошибок,
  67 уникальных предупреждений (baseline); `Lzma.Core.Tests` 1740 passed / 0 failed /
  0 skipped; `Lzma.Ui.Tests` 226 / 0 / 0; итого 1966; точечно
  `SevenZipExtractTransactional` — 2 passed; `FullyQualifiedName~SevenZip` — 1222 passed.
  CI run `39141016587` (HEAD `68bbfaf`) — все три job'а success (ubuntu/windows/macos).
  На POSIX ожидается 1733 passed / 5 skipped (всего 1738).
- Честные границы после B5a: SEC-001 для 7z реализован в части стадинга и отката прежних
  байт; НЕ реализованы: pre-flight места и длины пути (B5b), обёртка
  `HasFileOnPath`/`HasFileOnDirectoryPath` (B5c, ограничение общее с ZIP, создано B3b-2b),
  восстановление атрибутов прежнего файла (B6), много-членная семантика, отмена при
  перезаписи и откат созданных каталогов с опубликованными файлами (B7). Отказ второго
  folder-а после публикации первого и отказ второго `Move` внутри `Publish` подтверждены
  чтением кода и ревью, но не наблюдением в тесте (`TX-EX-05` → B7; seam не вводится по
  решению проекта). Не заявляются: атомарность набора имён на уровне ОС, восстановление
  после обесточивания, гарантированная очистка (остатки `.tmp`/`.bak` при аварии возможны).

### B4b — отклонение Zip64-размеров, не помещающихся в Int64

- Дефект (найден кросс-семейным ревью B3b-1): `ZipStreamReader.TryApplyZip64Extra` читал три
  значения из недоверенного заголовка архива и приводил `UInt64` к `long` без проверки знака,
  поэтому Zip64-значение ≥ 2^63 (например `0xFFFFFFFFFFFFFFFF`) давало отрицательный размер или
  смещение, которое использовалось дальше — в `sizeAt` pre-flight проверки места и в сравнении
  `written != UncompressedSize`. Битые метаданные отклонялись, но не на границе чтения каталога,
  а в середине извлечения — несогласованно с остальным кодом файла.
- Реализовано (коммит `440a7b2`, дифф 4 строки): проверка знака сразу после каждого из трёх
  чтений с возвратом `false`, который вызывающий код уже маппит в `ZipReadResult.InvalidData`
  (`ZipStreamReader.cs:112–113`); XML-дока метода дополнена одной фразой. Это делает метод
  согласованным с собственными проверками знака в том же файле — для Zip64 EOCD-локатора
  (`:224–226`) и Zip64 EOCD-записи (`:242–247`). Новые коды результата не добавлялись,
  публичный API и enum не изменены.
- Полнота подтверждена независимо (тимлид и ревьюер): в ZIP-коде семь приведений `UInt64 → long`
  (`:224`, `:242`, `:243`, `:244`, `:284`, `:292`, `:300`) и все семь защищены; in-memory
  `ZipReader` ZIP64 не поддерживает вовсе (`NotSupported`), а его единственное приведение
  `(long)lh` идёт от `int` с уже выполненной проверкой знака.
- Тесты: три новых (отрицательные uncompressed size, compressed size, local offset ⇒ `InvalidData`
  и пустой каталог); фикстура строится побайтово с настоящим Zip64 extra (id `0x0001`), в каждом
  тесте испорчено ровно одно поле, остальные валидны (по 29 байт — фактическая длина payload),
  поэтому отказ вызван именно испорченным полем. Существующие тесты не изменены (в диффе ноль
  удалённых строк), позитивный контроль `ZIP64_ПоэлементныйExtra_РазмерыИСмещениеИзExtra` зелёный.
- Кросс-семейное ревью (`deepseek-flash`) — **APPROVE_WITH_COMMENTS**, 0 BLOCKING, 0 MAJOR,
  7 MINOR. Ревьюер независимо подтвердил полноту (grep по всем `(long)`-приведениям), корректность
  границы (`< 0` эквивалентно ≥ 2^63 в unchecked-приведении; `long.MaxValue` не отвергается),
  сохранность семантики метода (порядок полей по APPNOTE, `q + 8 > end`, продвижение `q`) и
  ненаблюдаемость частичного применения `ref`-значений при отказе (вызывающий немедленно
  возвращает `InvalidData`, а единственное чтение локальных переменных — конструирование записи,
  до которого управление не доходит).
- Замечания ревью, вынесенные в отдельный шаг **B4c** (не блокируют B4b): позитивный контроль для
  нового helper'а фикстуры (сейчас валидная ветка builder'а не проверяется, поэтому будущая поломка
  builder'а оставила бы три теста зелёными по посторонней причине); ветка частично сентинельных
  полей (когда `needComp = true`, а `needUncomp = false`) не покрыта; тест на принимаемое
  `long.MaxValue`; hardening публичного API извлечения от вручную собранных `ZipStreamEntry`
  с отрицательными `LocalHeaderOffset`/`CompressedSize` (`ZipStreamExtractor.cs:74` вне `try`,
  `:126–129`); overflow-safe арифметика guard'ов `ZipStreamReader.cs:225` и `:60`.
- Факты после B4b (Windows): полный Rebuild — 0 ошибок, 67 уникальных предупреждений (baseline);
  `Lzma.Core.Tests` 1738 passed / 0 failed / 0 skipped; `Lzma.Ui.Tests` 226 / 0 / 0; итого 1964;
  точечно `ZIP64` — 10 passed (7 существующих + 3 новых). `git diff --numstat` — `4/0` production
  и `127/0` тесты; публичный API не изменён. CI run `39054975729` (HEAD `440a7b2`) — success.
  На POSIX ожидается 1731 passed / 5 skipped (всего 1736).

### B4a — покрытие отката in-memory маршрута и потокового маршрута на длинном пути

- Шаг закрывает два пробела покрытия, зафиксированные кросс-семейными ревью B3 и B3b-2b:
  (1) откат всей операции для ZIP in-memory маршрута (`TX-EX-03` из дизайна SEC-001) — отказ на
  N-м элементе после успешной публикации первых N−1; (2) обёртка расширенного пути в
  `ZipStreamExtractor.cs:98`, которая не имела ни одного теста (интеграционные тесты длинных
  путей шли через in-memory `ExtractCore`).
- Добавлено (коммит `6dceae5`, только тесты, production-код не изменён): новый файл
  `tests/Lzma.Core.Tests/Zip/ZipExtractorRollback.Tests.cs` с тремя тестами, вызывающими internal
  `ExtractCore` (доступен через `InternalsVisibleTo`, `Lzma.Core.csproj:36`), и один тест
  в `ZipExtractPathLength.Tests.cs` (существующие тесты не правились).
- Закреплённые инварианты: отказ через `IOException` на втором элементе ⇒ `IOError` и прежние
  байты ОБОИХ файлов восстановлены (первый был опубликован и откачен); то же для ветви
  «делегат вернул отрицательный результат» ⇒ `InvalidData`; опубликованный `CreatedNew` удаляется
  откатом, а существующий файл, которого нет в архиве и у которого нет backup, не трогается никогда;
  остатков `*.tmp`/`*.bak` после отката нет. Потоковый маршрут: извлечение реальным архивом через
  публичный `ZipStreamExtractor.ExtractToDirectory` в каталог назначения ~300 символов ⇒ `Ok`,
  содержимое совпадает, остатков нет.
- Честная квалификация: тесты зелёные СРАЗУ, потому что `ExtractCore` общий для обоих ZIP-маршрутов
  и переведён на staged-запись в шаге B3. Это characterization-покрытие (закрепление существующего
  поведения), а не RED-тесты. Все операции с длинными путями в тестах идут через
  `StagedExtractionJournal.ToFileSystemPath`, чтобы тест не обходил проверяемый механизм
  (урок MAJOR 2 ревью B3b-2b).
- Факты после B4a (Windows): полный Rebuild — 0 ошибок, 67 уникальных предупреждений (baseline);
  `Lzma.Core.Tests` 1735 passed / 0 failed / 0 skipped; `Lzma.Ui.Tests` 226 / 0 / 0; итого 1961;
  точечно `ZipExtractorRollback` — 3 passed. `git status --short` не содержит `src/**`
  (production-код не изменён). CI run `39053033743` (HEAD `6dceae5`) — success.
  На POSIX ожидается 1728 passed / 5 skipped (всего 1733).
- Расхождение в отчёте исполнителя зафиксировано честно: заявленное «46 уникальных предупреждений»
  не подтвердилось — полный Rebuild тимлида даёт 67 (baseline), число не выросло.
- Не покрыто (отнесено к следующим шагам): валидация отрицательных Zip64-размеров
  (`ZipStreamReader.cs:283`, `:290`, `:297` приводят `UInt64` к `long` без проверки знака, тогда
  как Zip64 EOCD на `:242–247` такую проверку имеет) — шаг B4b; откат потокового маршрута при
  отказе внутри `WriteEntry` покрыт существующими `TX-EX-01`/`TX-EX-02`.

### B3b-2b — расширенные пути Windows: интерим-ограничение снято

- Причина шага: после B3b-2a на Windows пути назначения длиной от 239 символов отклонялись
  `PathTooLong` независимо от long-path opt-in хоста. Библиотека не может рассчитывать на opt-in
  потребителя: Win32 требует ОБОИХ условий (реестр `LongPathsEnabled=1` И `longPathAware`
  в манифесте приложения), а `src/Lzma.Ui/app.manifest` его не содержит; .NET собственную проверку
  `MAX_PATH` не выполняет — блокирует Win32. Единственное доступное библиотеке средство — префикс
  `\\?\` (для UNC — `\\?\UNC\`), который отключает нормализацию Win32 и снимает `MAX_PATH`.
- Реализовано (коммит `03adfd8`): internal `StagedExtractionJournal.ToFileSystemPath` —
  идемпотентный, не применяется к не-Windows, к коротким путям (длина < 259), к относительным
  путям и к уже расширенным формам; все 20 файловых вызовов журнала переведены на 7 частных
  обёрток, все 10 мест ZIP-маршрута обёрнуты (`ZipExtractor.cs:42, 82, 147, 149, 156, 160, 166,
  197, 200`; `ZipStreamExtractor.cs:98`); лимит полного пути на Windows поднят до расширенного
  предела с вычетом запаса на префикс (`FullPathLimit = 32767 − 8`), macOS 1024 и прочие POSIX 4096
  не изменены, лимит компонента имени 255 сохранён для всех платформ; `DriveInfo` по-прежнему
  получает обычный `root`, а не расширенную форму.
- Полнота обёрток обязательна, а не косметична: без префикса `File.Exists(fullPath)` для пути
  длиннее `MAX_PATH` возвращает `false`, охрана `overwrite: false` не срабатывает, а обёрнутый
  `RegisterTarget` классифицирует элемент как `Replaced` ⇒ существующий файл пользователя был бы
  перезаписан вместо возврата `InvalidData`. Поэтому частичное применение префикса запрещено,
  а расширенная форма не сохраняется в журнале (сравнения путей остаются согласованными).
- Кросс-семейное ревью (`deepseek-flash`) — **APPROVE_WITH_COMMENTS**, 0 BLOCKING, 2 MAJOR, 6 MINOR.
  Устранено: (MAJOR 1) добавлен тест охраны `overwrite: false` на длинном пути — без него регресс
  обёртки был бы бесшумным; (MAJOR 2) тестовый helper пишет staged-файл через `ToFileSystemPath`,
  чтобы не обходить проверяемый механизм; (MINOR) устаревшие XML-доки, сообщение `Assert.Skip`,
  единый источник лимита `FullPathLimit` вместо дублирования в тестах, запас на префикс в лимите
  (ранее предел 32767 сравнивался с длиной пути без префикса, что давало окно ~4 символа).
  Ревьюер независимо подтвердил полноту обёрток двумя способами (подсчёт удалённых строк диффа
  и перечисление 26 вызовов baseline → 7 обёрток) и отсутствие утечки расширенной формы в журнал.
- Факты после B3b-2b (Windows): полный Rebuild — 0 ошибок, 67 уникальных предупреждений (baseline);
  `Lzma.Core.Tests` 1731 passed / 0 failed / **0 skipped** (интеграционный тест длинного пути
  больше не пропускается на Windows — прямое свидетельство снятия интерим-ограничения);
  `Lzma.Ui.Tests` 226 / 0 / 0; итого 1957; точечно `ZipExtractPathLength` — 18 passed.
  Публичный API не изменён (новые члены только internal, enum не тронут). CI run `39050282290`
  (HEAD `03adfd8`) — все три job'а success (ubuntu/windows/macos).
- Честные границы: поведение на хосте с `LongPathsEnabled=0` подтверждено кодом и ревью, но НЕ
  измерено (на хосте разработки значение равно 1, поэтому зелёный прогон не разделяет гипотезы
  «префикс работает» и «префикс не нужен на этом хосте») — чистое доказательство требует прогона
  на хосте без opt-in или capability-probe; `HasFileOnPath`/`HasFileOnDirectoryPath` (общие с 7z)
  не обёрнуты, из-за чего на длинных путях конфликт промежуточного сегмента даёт `IOError` вместо
  `InvalidData` — это НОВЫЙ класс поведения, созданный шагом (до него такие пути отклонялись
  раньше), откат корректен и данные не теряются; каталог назначения не проходит Win32-нормализацию,
  поэтому для `root` от 259 символов с сегментом, оканчивающимся точкой или пробелом, префикс
  делает путь литеральным; 7z-маршрут по-прежнему ограничен `MAX_PATH` и не имеет `PathTooLong`
  (несогласованность двух маршрутов закрывается в B5).
  (Обновлено 2026-10-18: проверки конфликтов обёрнуты в B5c; стадинг 7z и
  `PathTooLong`/`InsufficientSpace` реализованы в B5a/B5b — см. секции выше.)

### B3b-2a — PathTooLong и pre-flight проверка длины пути (ZIP)

- Причина шага: имя стадии добавляет к полному пути 21 символ (`.{16 hex}.tmp`), поэтому на
  типовой Windows-машине (`LongPathsEnabled=0`, `MAX_PATH=260`) путь назначения длиной примерно
  от 216 символов, работавший до B3, отказывал бы на уровне ОС и выглядел как общий `IOError` —
  причём уже после публикации предыдущих элементов. Теперь такой путь отклоняется отличимым
  состоянием `ZipExtractResult.PathTooLong` до первой мутации (значение подтверждено владельцем
  и добавлено строго в конец enum).
- Реализовано (коммит `54baf25`): internal `StagedExtractionJournal.IsPathTooLong(fullPath, extra)`
  и частный случай `IsPathTooLongForStaging`; проверяются длина последнего компонента имени
  (лимит 255) и длина полного пути против платформенного лимита — Windows 260 (`MAX_PATH`),
  macOS 1024 (`MAXPATHLEN`), прочие POSIX 4096 (`PATH_MAX`); проверка охватывает каталоги и корень
  назначения (без суффикса стадии), потому что `Directory.CreateDirectory` на длинном пути тоже
  отказывает. Вставка — после валидации путей и проверки свободного места, строго до
  `Directory.CreateDirectory(root)`.
- Кросс-семейное ревью (`deepseek-flash`) — **APPROVE_WITH_COMMENTS**, 0 BLOCKING, 4 MAJOR, 7 MINOR.
  Устранено: (MAJOR 1) на POSIX лимиты измеряются в байтах, а `string.Length` считает UTF-16
  единицы, поэтому не-ASCII имена недооценивались (128 кириллических символов = 256 байт) —
  измерение переведено в байты UTF-8 на POSIX и оставлено в символах на Windows;
  (MAJOR 2) формулировки «ограничение платформы» заменены на «принятый в реализации лимит»,
  интерим-ограничение зафиксировано явно; (MAJOR 3) каталоги и корень включены в проверку —
  раньше длинное имя каталога давало бы `IOError` вместо `PathTooLong`; (MAJOR 4) расхождения
  в отчёте исполнителя закрыты ответом на ревью. MINOR: добавлены тесты границы полного пути
  и смешанного списка (отказ до записи первого элемента), лимиты оформлены именованными
  константами, исправлен off-by-one в XML-доке (22 символа: 21 суффикс + завершающий ноль),
  уточнена причина `Assert.Skip`. Подробности: `.agent/reports/B3B2A_REVIEW_RESPONSE.md`.
- Правка тимлида как архитектора (до ревью и подтверждённая ревьюером): платформенный лимит
  macOS 1024 вместо единых 4096 для всех POSIX — при 4096 отказ возник бы на уровне ОС и был бы
  смаплен в общий `IOError`, то есть шаг потерял бы смысл. Spec-pack исправлен поправкой
  архитектора, чтобы исполнители следующих шагов не унаследовали неверную константу.
- Факты после B3b-2a (Windows): полный Rebuild — 0 ошибок, 67 уникальных предупреждений
  (baseline); `Lzma.Core.Tests` 1722 passed / 0 failed / 1 skipped (всего 1723) — пропущен
  только интеграционный тест длинного пути (~300 символов), который на Windows превышает принятый
  лимит 260 и завершается видимым `Assert.Skip`, а на ubuntu/macos выполняется;
  `Lzma.Ui.Tests` 226 / 0 / 0; итого 1949 тестов. Публичный API изменён только аддитивно
  (одно значение enum); `git diff --check` пусто; изменены только файлы allowlist.
  CI run `39046801847` (HEAD `54baf25`) — все три job'а success (ubuntu/windows/macos).
- Честные границы: фактические `NAME_MAX`/`PATH_MAX` на ubuntu/macos локально не измерены
  (Linux недоступен: в WSL только `docker-desktop`; macOS-техники нет) — константы взяты из
  документации платформ и подтверждаются capability-probe в CI; до подтверждения считаются
  неподтверждёнными. Интерим-ограничение: пути назначения от 239 символов на Windows отклоняются
  независимо от long-path opt-in хоста — расширение (префикс `\\?\`, лимит 32767) делается
  следующим шагом B3b-2b, его нельзя откладывать. UI не имеет человекочитаемого сообщения для
  `PathTooLong` и `InsufficientSpace` (default-ветвь `MainViewModel.cs:1535–1540`) — заведено
  отдельной задачей. 7z-маршрут не имеет ни `PathTooLong`, ни `InsufficientSpace` — значения
  появятся в B5 вместе с проверками (мёртвые члены enum не добавляем).

### B3b-1 — InsufficientSpace и pre-flight проверка свободного места (ZIP)

- Шаг B3b разделён на два микро-шага (один шаг = один reason for change): **B3b-1** — свободное
  место, **B3b-2** — длина пути. Решение владельца D2 = а (аддитивное значение enum, изменение
  публичного API утверждено); значение `PathTooLong` владелец подтвердил, но оно добавляется
  в B3b-2, где реально используется — мёртвых членов enum не создаём.
- Реализовано (коммит `076e900`): `ZipExtractResult.InsufficientSpace = 4` (строго в конец enum,
  существующие значения не изменены); internal `ZipExtractor.ExtractCore` получил последним
  необязательный параметр `Func<int, long>? sizeAt` (`null` ⇒ проверка пропускается, обратная
  совместимость); оба ZIP-маршрута передают размеры (in-memory — `Bytes.LongLength`, stream —
  `UncompressedSize`); проверка выполняется после предварительной валидации путей и строго до
  `Directory.CreateDirectory(root)`, то есть до первой мутации.
- Том назначения определяется не лексически (`Path.GetPathRoot` на POSIX всегда `/`), а выбором
  записи `DriveInfo.GetDrives()` с наиболее длинным совпадением префикса пути (Windows — буква
  диска, POSIX — точка монтирования) через internal `TryResolveVolumeRoot`; сравнение идёт по
  границе разделителя, поэтому том `/media/user/USB` не совпадает с путём `/media/user/USBdata`.
  При отсутствии совпадения (UNC) или неготовности тома измерение считается недоступным и операция
  НЕ блокируется (best-effort).
- Сумма требуемого места насыщается до `long.MaxValue` и отбрасывает неположительные слагаемые:
  размеры приходят из недоверенного заголовка, а `ZipStreamReader.cs:283` приводит `UInt64` к `long`
  без проверки диапазона, поэтому Zip64-значение ≥ 2^63 даёт отрицательный `UncompressedSize`.
  Валидация самих Zip64-размеров — отдельный reason for change, отнесена к B4.
- Кросс-семейное ревью (`deepseek-flash`) вернуло **REQUEST_CHANGES**: 1 BLOCKING (на POSIX
  измерялась корневая ФС вместо тома назначения ⇒ возможен ложный отказ для валидной операции;
  вывод ревьюера аналитический, машина под Windows), 1 MAJOR (переполнение/отрицательные размеры),
  9 MINOR. BLOCKING устранён вариантом «выбор тома по наиболее длинному префиксу» (вариант
  «гейт только на Windows» отклонён как лишающий проверки Linux/macOS); MAJOR устранён насыщением
  суммы; MINOR: тест переписан на реальную ветвь `sizeAt == null`, формулировка главного теста
  сужена до доказуемого (порядок «до первой мутации» обеспечивается местом вставки и подтверждён
  ревью, а не утверждением `Directory.Exists`), XML-дока значения enum дополнена контрактом для
  потребителя и оговоркой о приближённости оценки, сообщение для UI заведено в трекер,
  `InsufficientSpace` для 7z оставлен в трекере B5.
- Признана и исправлена ошибка тимлида: утверждение в spec-pack «отрицательные `requiredBytes`
  невозможны» было ложным — в `.agent/tasks/B3B1_SPEC_PACK.md` внесена поправка архитектора,
  чтобы исполнители следующих шагов не унаследовали неверную премиcу.
- Факты после B3b-1 (Windows): полный Rebuild — 0 ошибок, 67 уникальных предупреждений (baseline);
  `Lzma.Core.Tests` 1713 passed / 0 failed / 0 skipped (добавлено 8 детерминированных тестов
  без заполнения диска); `Lzma.Ui.Tests` 226 / 0 / 0; итого 1939. Публичный API изменён только
  аддитивно (одно значение enum); `git diff -U0 -- src/Lzma.Core | grep public` новых
  public-членов не показывает (`HasInsufficientSpace` и `TryResolveVolumeRoot` — internal).
  CI run `39041733534` (HEAD `076e900`) — все три job'а success (ubuntu/windows/macos),
  включая Build, evidence 7-Zip, Test и Upload TRX.
- Честные границы: оценка требуемого объёма приближённая (без запаса и без учёта блоков/записей
  каталогов); квоты, резерв ext4 и параллельная запись не учитываются, поэтому откат при
  `IOException` остаётся обязательным; при одновременном конфликте имени и нехватке места
  результат — `InsufficientSpace` (проверка места стоит до поэлементных проверок ФС), мутаций
  нет в обоих случаях; stream-маршрут и ветви «измерение недоступно» отдельными тестами не покрыты
  (B4); UI показывает для нового состояния общий текст — заведено отдельной задачей.

### B2/B3 — SEC-001 реализован для ZIP-маршрута (7z-маршрут открыт)

- Шаг B2 (мидл `qwen3.7-plus`): добавлены два RED-теста `TX-EX-01` (неверный пароль) и `TX-EX-02`
  (повреждённые данные) в `tests/Lzma.Core.Tests/Zip/ZipStreamExtractorTransactional.Tests.cs`.
  На коде до фикса оба падали с сообщением «прежний файл удалён — потеря данных (SEC-001)»:
  `ZipStreamExtractor.cs:97` открывал конечный файл через `FileMode.Create` до проверки
  пароля/HMAC/CRC, а откат в `ZipExtractor.cs:185–192` удалял его как «созданный»
  (`createdFiles.Add` выполнялся до записи). Коммит `d69a5a6`.
- Шаг B3 (мидл `qwen3.7-plus`, верификация и доводка — тимлид): введён internal-тип
  `StagedExtractionJournal` (`src/Lzma.Core/StagedExtractionJournal.cs`) — общая механика
  стадии/журнала/публикации/отката/очистки; `ZipExtractor.ExtractCore` пишет через делегат
  в staged-путь, публикует после успешной записи и закрытия потока, откатывает операцию целиком
  при любом контролируемом отказе; `ZipStreamExtractor.WriteEntry` открывает поток на staged-пути.
  Публикация идёт через backup-first rename (`final → .bak`, затем `tmp → final`), поэтому атрибуты
  прежнего файла, включая `ReadOnly`, не мутируются до успеха; вызов
  `TryPrepareExistingFileForOverwrite` из ZIP-маршрута удалён (измерено: rename `ReadOnly`-файла
  на Windows работает, байты и атрибуты сохраняются). Публичный API и enum не изменялись.
  Коммит `4713844`.
- Кросс-семейное ревью (`deepseek-flash`) вернуло **REQUEST_CHANGES**: 2 BLOCKING, 3 MAJOR, 6 MINOR.
  Устранено: (1) `.bak` с атрибутом `ReadOnly` не удалялся после успеха и оставался в каталоге
  назначения — перед удалением атрибут снимается; (2) повторный `Rollback()` удалял уже
  восстановленные прежние байты — добавлены флаг `_rolledBack` и сброс `Published`;
  (3) исключение очистки после точки фиксации могло вылететь наружу при успешном извлечении —
  расширен состав catch. MINOR: подбор свободного имени `.bak`, `RegisterTarget` сделан `private`,
  исправлены комментарии и XML-док, мёртвая ветвь атрибутов задокументирована как подключаемая в B6.
  Подробности: `.agent/reports/B3_REVIEW_RESPONSE.md`.
- Добавлены блокирующие регрессии: успешная перезапись `ReadOnly`-файла без остатков backup
  (Windows; на других ОС видимый `Assert.Skip`) и три теста журнала стадии (идемпотентность
  повторного отката, no-op после точки фиксации, откат до публикации). Мутационная проверка:
  при временном удалении обеих BLOCKING-правок падают ровно эти два теста.
- Факты после B3 (Windows): `Lzma.Core.Tests` 1705 passed / 0 failed / 0 skipped;
  `Lzma.Ui.Tests` 226 passed / 0 failed / 0 skipped (итого 1931); полный Rebuild — 0 ошибок,
  67 уникальных предупреждений (baseline сохранён); `git diff -U0 -- src/Lzma.Core | grep public/enum`
  пусто (публичный API не изменён). CI run `38996552464` (HEAD `4713844`) — все три job'а success
  (ubuntu/windows/macos), включая Build, evidence 7-Zip, Test и Upload TRX.
- Честные границы после B3: SEC-001 реализован **только для ZIP-маршрута**; 7z-маршрут по-прежнему
  открывает конечные файлы до проверки (`SevenZipArchiveDecoder.cs:1098`, `:1146`) — шаги B5/B6/B7.
  (Обновлено 2026-10-18: стадинг и публикация после проверок реализованы в B5a — см. секцию
  выше; остаются B5b/B5c/B6/B7.)
  Не заявляются: атомарность набора имён на уровне ОС, восстановление после обесточивания,
  гарантированная очистка (при аварии остатки `.tmp`/`.bak` возможны). Отказ второго переноса
  (`staged → final`) не покрыт тестом: для прямой инъекции нужен seam (`SEAM-2`, рекомендовано
  не вводить), ветвь защищена кодом и ревью, но не наблюдением в тесте.
- Известное временное ограничение (закрывается шагом B3b): имя стадии добавляет 21 символ к полному
  пути, поэтому на Windows без поддержки длинных путей (`LongPathsEnabled=0`, `MAX_PATH=260`)
  путь назначения длиной примерно от 216 символов, работавший до B3, может завершиться `IOError`.
  Поведение на POSIX для write-protected файлов и лимитов `NAME_MAX`/`PATH_MAX` локально не
  измерено и подтверждается capability-probe в CI.
- Отклонения от правила микро-шага, зафиксированные честно: объём B3 превысил лимит
  (3 production-файла, один из них новый, и 2 тестовых файла) — причина: общая абстракция по
  решению архитектора плюс обязательные блокирующие регрессии по находкам ревью; doc-tail шага B2
  был перенесён на B3, чтобы документация не расходилась с запушенным состоянием.

### B1 — дизайн SEC-001 подготовлен и принят архитектором (реализация НЕ начата)

- Шаг B1 фазы B выполнен ролью «сеньор-дизайнер» (`deepseek-v4-pro`) как read-only security design;
  верификацию и вердикт сделал тимлид-архитектор (`qwen3.8-max`), поэтому кросс-семейная проверка
  соблюдена. Production-код и тесты не изменялись, commit шаг не имеет.
- Артефакты (в каталоге `.agent/`, который исключён из git через `.git/info/exclude`):
  отчёт дизайна `…/tasks/B1_SEC001_DESIGN_REPORT.md` (521 строка, разделы 0–8), вердикт
  `…/review/REVIEWER_B1_DESIGN.md` — **APPROVE_WITH_COMMENTS**, 0 блокирующих замечаний,
  spec-pack `.agent/tasks/B1_SPEC_PACK.md`.
- Уровень гарантии не пересматривался (утверждён владельцем 2026-10-12, Q10 = а): per-file staging
  (запись во временный файл рядом с назначением) → публикация после проверки размера/CRC/HMAC →
  откат всей операции с восстановлением прежних байт.
- Суть дизайна: единый операционный журнал на вызов (`CreatedNew`, `Replaced`, `CreatedDir`,
  `AttributesChanged`); публикация заменяемого файла через backup-first rename
  (`final → .bak`, затем `tmp → final`), поэтому атрибуты прежнего файла, включая `ReadOnly`,
  не мутируются до успеха; для 7z публикация folder-а откладывается до
  `routing.IsComplete && !SizeOverflow && !CrcMismatch` и `Dispose` всех временных потоков;
  пустые файлы публикуются без отдельного временного файла (их CRC проверяется на этапе плана);
  механика staging/publish/rollback выносится в один `internal`-тип (вводится в шаге B3),
  публичный API не меняется.
- Верификация отчёта тимлидом: `git status --short` пуст; выборочная независимая сверка около
  25 утверждений `file:line` на HEAD `009f2f5` — все совпали (включая `ZipExtractor.cs:55/:163/:201–213`,
  `ZipStreamExtractor.cs:97/:122–123/:139–142/:197/:220–223`,
  `SevenZipArchiveDecoder.cs:811/:1091/:1146/:1153/:1170–1178/:1185–1188`,
  `SubstreamRoutingWriter.cs:80`, `Lzma.Core.csproj:36`, `ExtractionPlan.cs:163–170`,
  `LzmaArchiveService.cs:45/59/303/488`); запрещённые формулировки в отчёте отсутствуют.
- Решения архитектора по вопросам дизайна: общий `internal`-helper вводится в B3 — согласовано;
  `AttributesChanged` остаётся fallback-видом журнала (B6 обязан начать с capability-probe
  `File.Move` для read-only источника) — согласовано (ОТМЕНЕНО 2026-10-19 по факту исполнения
  B6: probe избыточен — backup-first rename ZIP измерен в CI на трёх ОС с B3; см. секцию B6);
  пустые файлы без временного файла —
  согласовано; вопрос о production-seam для инъекции «настоящего» `IOException` середины записи
  (диск полон) вынесен владельцу — рекомендация архитектора не вводить его сейчас и завести
  отдельную задачу `SEAM-2`.
- Честная граница: **SEC-001 остаётся открытым**. Дизайн не является реализацией: ни один
  production-файл не изменён, блокирующих regression-тестов `TX-EX-01…TX-EX-06` в репозитории нет,
  прежние байты при отказе извлечения по-прежнему не восстанавливаются. Гарантии crash/power-loss
  recovery и атомарности набора имён на уровне ОС не заявляются и дизайном не вводятся.
- По требованию плана фазы работа после B1 остановлена: B2 (RED-тесты) начинается только после
  утверждения дизайна владельцем.

### P1/P2 — xUnit v3 и видимые пропуски тестов выполнены

- Пакет `xunit` 2.9.3 заменён на `xunit.v3` 3.2.2 в обоих тестовых проектах, добавлен
  `<OutputType>Exe</OutputType>` (v3-проекты исполняемые). `xunit.runner.visualstudio` 3.1.4 и
  `Microsoft.NET.Test.Sdk` 17.14.1 сохранены, поэтому `dotnet test` продолжает работать через
  VSTest. На xUnit v4 не переходили осознанно: v4 требует Microsoft Testing Platform, а
  `dotnet test` больше не запускает v4-проекты через VSTest — пришлось бы менять команду прогона
  и способ получения TRX в CI.
- Факты после миграции (Windows): `Lzma.Core.Tests` 1699 passed / 0 failed / 0 skipped;
  `Lzma.Ui.Tests` 226 passed / 0 failed / 0 skipped; прямой запуск исполняемого файла
  (`Lzma.Ui.Tests.exe -noLogo`) — Total 226, Errors 0, Failed 0, Skipped 0; полная пересборка —
  0 ошибок, 67 уникальных предупреждений (baseline сохранён). Подтверждено в CI: run `38808168023`
  (HEAD `b4ec795`) — все три job'а success; шаги Test и Upload TRX работают с v3 без изменения
  workflow.
- Шаг P2 устранил оставшиеся невидимые пропуски: четыре теста Windows-специфичных правил вместо
  тихого `return` вызывают `Assert.Skip` с указанием причины; сценарий смены рабочего каталога
  вместо падения при несобранной пробе также вызывает `Assert.Skip`. Это закрывает остаток TST-001
  и отклонение F3-Q4a. Факты после P2 (Windows): полная пересборка — 0 ошибок, 67 уникальных
  предупреждений; `Lzma.Core.Tests` 1699 / 0 / 0; `Lzma.Ui.Tests` 226 / 0 / 0; `git diff --check`
  пусто; изменены только пять тестовых файлов. CI run `38813708548` (HEAD `7a2d792`) — все три
  job'а success (ubuntu/windows/macos), включая Build, evidence 7-Zip и Test.
- Техническая деталь, найденная при верификации: `Assert.Skip` в xunit.v3 не помечен
  `[DoesNotReturn]`, поэтому `LocateCwdProbe()` (возврат `string`) не компилировался —
  `error CS0161: не все пути к коду возвращают значение`. Исправлено добавлением недостижимого
  `return null!;` с комментарием-обоснованием. Это отклонение от spec-pack вызвано реальностью
  компилятора, а не ошибкой проектирования.
- Честная граница проверки: точное число пропусков на ubuntu/macos (ожидается 4 в
  `Lzma.Core.Tests`) в этой сессии не выводилось — лог CI и TRX-артефакты требуют авторизации.
  Шаг Test завершился успешно на всех трёх ОС; числа доступны владельцу в артефактах.
- Известный технический долг: анализатор v3 добавил правило `xUnit1051` (рекомендация передавать
  `TestContext.Current.CancellationToken` в вызовы с `CancellationToken`) в 518 местах. Правило
  подавлено осознанно через `NoWarn` с комментарием в обоих тестовых csproj: массовая замена
  изменила бы поведение тестов (отмена по таймауту раннера) и не относится к миграции пакетов.
  Внедрение отменяемых токенов — отдельная задача.

### Тестовый фреймворк — миграция на xUnit v3 выполнена

- Пакет `xunit` 2.9.3 заменён на `xunit.v3` 3.2.2 в обоих тестовых проектах, добавлен
  `<OutputType>Exe</OutputType>` (v3-проекты исполняемые). `xunit.runner.visualstudio` 3.1.4
  и `Microsoft.NET.Test.Sdk` 17.14.1 сохранены: `dotnet test` продолжает работать через VSTest.
- Миграция на xUnit v4 не выполнялась осознанно: v4 требует Microsoft Testing Platform,
  а `dotnet test` больше не запускает v4-проекты через VSTest — это потребовало бы менять
  команду прогона и способ получения TRX в CI.
- Фактические прогоны после миграции: `Lzma.Core.Tests` — 1699 passed / 0 failed / 0 skipped;
  `Lzma.Ui.Tests` — 226 passed / 0 failed / 0 skipped; прямой запуск исполняемого файла
  (`Lzma.Ui.Tests.exe -noLogo`) — Total 226, Errors 0, Failed 0, Skipped 0;
  `dotnet build LzmaSharp.sln -c Release` — 0 ошибок, 67 уникальных предупреждений (baseline).
- Подтверждено в CI: run `38808168023` (HEAD `b4ec795`) — все три job'а success
  (ubuntu/windows/macos), шаги Test и Upload TRX работают с v3 без изменения workflow.
- Известный технический долг: анализатор v3 добавил правило `xUnit1051`
  (рекомендация передавать `TestContext.Current.CancellationToken` в вызовы с `CancellationToken`)
  в 518 местах. Правило подавлено осознанно (`NoWarn` в обоих тестовых csproj с комментарием):
  массовая замена изменила бы поведение тестов (отмена по таймауту раннера) и не относится
  к миграции пакетов. Внедрение отменяемых токенов — отдельная задача.
- Цель миграции достигнута: доступен динамический `Assert.Skip`, поэтому следующим шагом
  fail-fast в CWD-пробе и 4 тихих `return` в Windows-only тестах заменяются видимым пропуском.

### G2 / TST-001 — interop-тесты требуют настоящий 7-Zip; совместимость проверяется в CI на трёх ОС

- В CI добавлена установка 7-Zip: на ubuntu и macos — официальный консольный `7zz` 26.04
  из релиза `ip7z/7zip` (ассеты `7z2604-linux-x64.tar.xz`, `7z2604-mac.tar.xz`, наличие
  подтверждено через GitHub API), каталог добавляется в `PATH`; на Windows — предустановленный
  `7z.exe` либо установка через Chocolatey. Добавлен шаг evidence, печатающий версию и путь.
- 8 interop-тестов больше не содержат хардкод `C:\Program Files\7-Zip\7z.exe` и тихий
  `return`: путь даёт helper `tests/Lzma.Core.Tests/Helpers/SevenZipInterop.cs`
  (`RequireExecutable()`: `SEVENZIP_PATH` → `7zz` → `7z` → стандартный путь Windows),
  а при отсутствии исполняемого файла тест **падает** с перечнем проверенных кандидатов
  и подсказкой по установке.
- Фактический прогон: локально на Windows `Lzma.Core.Tests` 1699/0/0 и `Lzma.Ui.Tests` 226/0/0,
  interop-тесты действительно выполняются (фильтр `РаспаковываетсяНастоящим7Zip` — 5 passed;
  найденный исполняемый файл — `C:\Program Files\7-Zip\7z.exe`, 7-Zip 26.03 x64).
- Подтверждение в CI: run `38804489734`, HEAD `2d8a84d` — **success на трёх ОС**
  (`build-test (ubuntu-latest)`, `(windows-latest)`, `(macos-latest)`), шаги установки 7-Zip,
  evidence, Test и Upload TRX — success. Поскольку тесты теперь требуют исполняемый файл,
  зелёный прогон означает, что interop-проверки фактически выполнились на всех трёх ОС.
- Граница честности: конкретные версии/пути 7-Zip по ОС видны в шаге evidence в логах CI;
  логи job'ов без авторизации недоступны, поэтому в этом документе версия указана для
  ubuntu/macos как установленная (26.04), а для Windows — как предустановленная раннером.
- Остаётся открытым (не путать с interop): 4 теста в `Lzma.Core.Tests` с
  `if (!OperatingSystem.IsWindows()) return;` — тихий пропуск, устраняется после миграции
  на xUnit v3 (`Assert.Skip`) либо выделением в Windows-only набор.

### Кроссплатформенность тестов — 4 падения CI устранены и подтверждены прогоном на трёх ОС

Первый фактический прогон CI-матрицы (run `38773220694`, HEAD `a25a411`) показал: сборка
Release успешна на ubuntu/windows/macos, тесты на `windows-latest` зелёные, а на
`ubuntu-latest` и `macos-latest` — 4 падения в `Lzma.Ui.Tests` (221/4/0 из 225).

Причины установлены по коду и устранены; production-дефект найден один:

- **Три теста `ArchiveEntryNamingTests`** подавали Windows-пути (`C:\data\docs`) в
  платформенно-нейтральный production-код (`Path.GetFileName` + `Path.GetRelativePath`).
  На POSIX '\' не является разделителем, поэтому ожидания не совпадали. Тесты переведены на
  платформенно-корректные входы; ожидания по именам записей не изменены.
- **`WriteArchive_PublishFailure_PreservesExistingArchiveAndCleansStaging`** инъектировал отказ
  через `FileAttributes.ReadOnly` на файле: на Windows это запрещает перезапись, а на POSIX
  успех rename/unlink определяется правами каталога, поэтому отказа не возникало. Инъекция
  сделана платформенной (на POSIX — снятие прав записи с каталога через `File.SetUnixFileMode`
  с восстановлением в `finally`); все три инварианта сохранены.
  Ограничение (зафиксировано честно): на POSIX отказ возникает до публикации, поэтому сама
  фаза публикации на POSIX этим тестом не покрывается — для точной инъекции нужен отдельный
  шов (отслеживается отдельным шагом); процесс с правами root игнорирует права каталога.
- **Production-дефект (устранён):** `ArchiveEntryNaming.ForFileUnderFolder` выполнял
  `Replace('\', '/')` безусловно. На POSIX '\' — легальный символ имени файла, поэтому файл
  `a\b.txt` попадал в архив как запись `a/b.txt`, создавая подкаталог, которого у пользователя
  нет, и теряя исходное имя. Замена теперь выполняется только на Windows; добавлен тест
  `ОбратныйСлешВИмениФайла_ПлатформеннаяСемантика`, закрепляющий обе семантики.
- **Разница числа тестов `Lzma.Core.Tests`** (1699 на Windows, 1697 на Linux/macOS) объяснена
  фактом кода: `SevenZipArchiveDecoderExtractUnsafePath.Tests.cs:25–28` порождает два
  theory-кейса (`C:/evil.bin`, `file.bin:ads`) только на Windows. Это видимая платформенная
  зависимость данных, а не тихий пропуск; зафиксировано в `docs/TESTS_PLAN.md`.
- Отдельно известно: 4 теста в `Lzma.Core.Tests` используют `if (!OperatingSystem.IsWindows()) return;`
  — это тихий пропуск (TST-001), устраняется отдельным шагом, а не в этой правке.

Фактический прогон после правок (локально, Windows): `Lzma.Ui.Tests` — **226 пройдено /
0 падений / 0 пропущено**; `Lzma.Core.Tests` — **1699 / 0 / 0**; `dotnet build LzmaSharp.sln
-c Release` — 0 ошибок; новых предупреждений нет (baseline полной пересборки — 67).

Независимое кросс-семейное ревью диффа (ревьюер `deepseek-flash`; исполнитель был `qwen3.7-plus`)
дало вердикт **`APPROVE_WITH_COMMENTS`**: BLOCKING нет; две MAJOR-находки — вводящие в заблуждение
комментарии нового теста (устранены тимлидом: комментарии описывали дефект, который этим же
диффом устранён) и неполное покрытие фазы публикации на POSIX (вынесено в отдельный шаг).

**Подтверждение:** прогон CI после публикации правки — run `38794891354`, HEAD `be413de`:
`build-test (ubuntu-latest)`, `build-test (windows-latest)` и `build-test (macos-latest)` —
все три **success** (шаги Build и Test, TRX-артефакты загружены). Локальной POSIX-среды у
владельца нет (в WSL доступен только служебный дистрибутив `docker-desktop`), поэтому Linux
и macOS проверяются только через CI; локально прогон выполняется на Windows.

### SEC-011 / G1 — матрица ОС в CI прогнана: сборка и тесты зелёные на трёх ОС

- В `.github/workflows/ci.yml` job `build-test` переведён на матрицу
  `[ubuntu-latest, windows-latest, macos-latest]` с `fail-fast: false`; имя job содержит ОС;
  SDK по-прежнему берётся из `global.json` (версия не захардкожена); состав шагов сохранён
  (checkout с `fetch-depth: 0` для MinVer → setup-dotnet → restore → build → test).
- Шаг Test формирует TRX (`--logger trx`, `--results-directory TestResults`), результат
  публикуется как артефакт `actions/upload-artifact@v4` с `if: always()`,
  именем `trx-<os>-<sha>` и `retention-days: 14`.
- Каталог `TestResults/` в репозиторий не попадает: в `.gitignore` уже есть паттерн
  `[Tt]est[Rr]esult*/` (строка 33) — правка `.gitignore` не потребовалась.
- **Фактический прогон выполнен** (push по команде владельца; run `38773220694` для HEAD
  `a25a411`): YAML валиден — все три job'а запустились и отработали шаги; сборка Release
  успешна на `ubuntu-latest`, `windows-latest` и `macos-latest`; TRX-артефакты загружены для
  всех трёх ОС (`trx-<os>-<sha>`, шаг с `if: always()` сработал).
- **Результат тестов в CI: `windows-latest` — success; `ubuntu-latest` и `macos-latest` —
  failure на шаге Test.** Причина на момент записи не установлена: логи job'ов и скачивание
  TRX-артефактов требуют авторизации (HTTP 403), `gh` CLI в окружении тимлида отсутствует.
  Это НЕ регрессия от матрицы: предыдущие прогоны CI на этой ветке (HEAD `6357330`, `3e7ae74`,
  `bf0a14b`, `0dad11c` — все при одной ОС `ubuntu-latest`) тоже завершились `failure`.
  Локально на Windows полный прогон зелёный: 1924 passed / 0 failed / 0 skipped.
- Известные риски, которые матрица ОС НЕ закрывала на тот момент: 8 interop-тестов молча
  завершались при отсутствии `C:\Program Files\7-Zip\7z.exe`; поведение Avalonia-проекта
  `Lzma.Ui` (WinExe) на windows/macos прогоном не подтверждалось.
  **Оба пункта закрыты позже**: interop — шагом G2 (см. секцию выше, run `38804489734`),
  поведение UI-тестов на windows/macos подтверждено зелёными прогонами CI
  (`38794891354` и `38804489734`: `Lzma.Ui.Tests` 226 passed на каждой из трёх ОС).
- Изменение закоммичено (`e04c90e`) и запушено в `origin` (GitHub) и в зеркало GitFlic.

### SEC-002 — глобальный gate пройден (APPROVE_WITH_FINDINGS)

Финальный gate SEC-002 выполнен независимой моделью (`deepseek-v4-pro`) как adversarial-критик
и перепроверен тимлидом собственным прогоном и grep-ом.

- **No-bypass call graph доказан:** все четыре публичные точки создания/записи архива
  (`CreateArchiveAsync` — in-memory, на диск не пишет; `WriteArchiveAsync`;
  `CreateArchiveToFileAsync` — одиночный и многотомный маршруты; `CreateZipToFileAsync`)
  идут через staging (`StagedDestination` / `StagedVolumeSet` + `VolumeSpanningWriteStream`),
  а конечный путь затрагивается только в точке публикации: `StagedDestination.Commit`
  (`File.Move(staged → destination, overwrite: true)`) или `StagedVolumeSet.Commit`
  (`Move(staged → final, overwrite: false)` после backup-фазы).
- **Обходных маршрутов нет:** все вхождения `FileMode.Create` / `File.WriteAllBytes` /
  `new FileStream` / `File.Move` в `src/` классифицированы; записи конечного архива минуя
  staging не обнаружено. Вхождения в маршрутах извлечения
  (`SevenZipArchiveDecoder.cs:1098`, `:1146`; `ZipExtractor.cs:42`; `ZipStreamExtractor.cs:97`)
  относятся к SEC-001 и этим gate не закрываются.
- **Фактический прогон (независимо перепроверен тимлидом):** `dotnet test LzmaSharp.sln` —
  `Lzma.Ui.Tests` 225 пройдено / 0 падений / 0 пропущено, `Lzma.Core.Tests` 1699 / 0 / 0,
  итого **1924 пройдено, 0 падений, 0 пропущено**; сборка — 0 ошибок, 67 предупреждений
  (baseline, подтверждён полной пересборкой).
- **Сохранность гарантий:** F1, F2, M4B, M5.2, M6.1B, M6.4B, F3, M7 — проверены по коду,
  регрессий не найдено.
- **Находки gate:** BLOCKING — 0, MAJOR — 0, MINOR — 1 (устаревший статус коммитов в этом
  файле; устраняется настоящей правкой и шагом doc sync), NIT — 2 (граница ответственности
  `VolumeSpanningWriteStream`; остаточная CWD-зависимость `_stagedBasePath` в `Dispose` —
  относится к SEC-003/фазе D). Отдельно: правдивость interop-тестов (TST-001) — шаг G2.
- **Что gate НЕ доказывает (границы честности):** на момент gate матрица ОС в CI ещё не была
  прогнана (push выполнен позже: сборка зелёная на трёх ОС, тесты на ubuntu/macos падают —
  см. секцию SEC-011/G1); interop с внешним 7-Zip в CI
  не проверяется (8 тестов тихо завершаются без `7z.exe`; на локальной машине 7-Zip есть,
  и эти тесты выполнились) — ПОЗЖЕ УСТРАНЕНО шагом G2, см. отдельную секцию;
  crash/power-loss recovery и атомарность набора томов отсутствуют;
  извлечение (SEC-001) gate не покрывает.
- **Статус SEC-002:** транзакционное **создание** архивов реализовано и закреплено тестами
  в утверждённых границах; SEC-002 как пункт плана готов к doc sync (A11) и к ручному merge
  владельцем (A12). Заявлять атомарность, полное сохранение прежних байтов при overwrite
  или закрытие SEC-001 оснований нет.

### M7 / SEC-002-R1 — фикс внесён и подтверждён независимым ревью

Дизайн recovery-контракта M7 (best-effort восстановление всех backup при двойном отказе)
подготовлен сеньором-ревьюером соседнего отдела и верифицирован тимлидом; реализация не начата.

- Дефект: `StagedVolumeSet.RestoreBackups` не имеет per-item `try/catch`, поэтому первая ошибка
  `Move` прерывает цикл и остальные независимые backup не получают попытку восстановления
  (в отличие от `RollbackPublish`, который ошибки собирает и продолжает).
- Шаг A6 выполнен: добавлен тест
  `StagedVolumeSetTests.Commit_BackupFailureThenRestoreFailure_RestoresRemainingBackups`
  и аддитивно расширен `StagedFileOperationsFake` до множества точек отказа `Move`
  (старый конструктор сохранён, дубль индекса — ошибка конфигурации).
- Фактический RED: падение на инварианте журнала — `MoveCalls.Count` равен 4 вместо 5,
  то есть вызов restore для второго backup не выполняется. Форма исключения
  (`AggregateException` с двумя inner) уже сейчас корректна, поэтому RED не ложный:
  он указывает именно на отсутствие best-effort восстановления.
- Шаг A7 (фикс) выполнен по утверждённому владельцем контракту: `RestoreBackups` стал
  best-effort — каждый перенос обёрнут в per-item `catch … when (IsControlledFailure)`, цикл
  продолжается, ошибки возвращаются списком; в backup-фазе после восстановления добавлена
  best-effort очистка staged-файлов по замороженным путям; итоговое исключение —
  `AggregateException` с порядком `[backupFailure, restoreErr…, cleanupErr…]`, а при отсутствии
  вторичных ошибок — rethrow первичного исключения. Уцелевшие резервные копии (`.bak`)
  не удаляются; неконтролируемые исключения пробрасываются без агрегации.
- Шаг A8 выполнен: добавлены три characterization-теста на оставшиеся строки матрицы M7 —
  несколько restore-отказов с проверкой порядка secondary (строка 2), неконтролируемый
  restore-отказ без агрегации (строка 7), единственный backup с уцелевшей копией (строка 8).
  Тесты зелёные сразу, потому что production-фикс уже внесён; production-код на этом шаге
  не менялся.
- Регрессионная чувствительность доказана мутационной проверкой тимлида: временный возврат
  fail-fast в восстановлении резервных копий роняет ровно три теста M7 (24 остальных зелёные),
  временный возврат широкого `catch` вокруг восстановления роняет ровно тест
  неконтролируемого отказа (26 остальных зелёные). После проверки файл восстановлен,
  `git diff` по `src/` пуст. Это исключает «тесты, которые зелёные всегда».
- Фактический прогон после A8 (независимо перепроверен тимлидом): `Lzma.Ui.Tests` —
  **225 пройдено, 0 падений** (в классе `StagedVolumeSetTests` — 27);
  `Lzma.Core.Tests` — **1699 пройдено, 0 падений**;
  `dotnet build LzmaSharp.sln -c Release` — 0 ошибок; новых предупреждений в изменённых
  файлах нет (baseline полной пересборки — 67 уникальных).
- Независимое кросс-семейное ревью диффа A6+A7 (ревьюер `deepseek-v4-pro`; исполнители были
  из семьи `qwen`) дало вердикт **`APPROVE`**: BLOCKING и MAJOR находок нет, готовность
  к коммиту подтверждена. Ревьюер независимо воспроизвёл сборку и оба прогона, сверил дифф
  с решениями владельца M7-Q1…Q6 и подтвердил отсутствие изменений в существующих тестах.
- Код и тесты закоммичены по разрешению владельца: `d8c43e7` «Безопасность: восстанавливать
  все резервные копии томов при сбое отката»; документация — отдельным коммитом.
- Статус риска: восстановление стало best-effort **в пределах процесса** и закреплено
  блокирующими тестами (сценарий двойного отказа плюс три сценария матрицы). SEC-002 остаётся
  открытым (впереди global gate, doc sync, merge). Заявлять закрытие SEC-002 или атомарность
  оснований нет.
- Изменения G1 и A8 закоммичены по команде владельца тремя коммитами по числу причин
  изменения: `e04c90e` (CI: матрица ОС), `437edb9` (тесты: матрица сценариев M7),
  `412c563` (док: статус). Push не выполнялся — только по явной команде владельца.
- Границы: никаких гарантий атомарности, crash/power-loss recovery, полного восстановления
  при недоступной файловой системе или обязательной очистки этот фикс не добавляет.
  Известное поведение: при отказе восстановления в каталоге назначения может остаться
  резервная копия `*.bak` — она намеренно не удаляется, чтобы не уничтожать последнюю
  копию данных.

### F3 / AUD-005 — фикс внесён и подтверждён независимым ревью

Дизайн варианта B (frozen operational path в `StagedVolumeSet.SetVolumes`) утверждён
владельцем и независимым ревью (APPROVE_WITH_COMMENTS, без BLOCKING/MAJOR).

- Дизайн зафиксирован: `docs/plans/F3_PATH_IDENTITY_DESIGN.md`.
- Шаг A2 выполнен: добавлен тест
  `StagedVolumeSetTests.SetVolumes_RelativePaths_UseFrozenValidatedPathIdentity` и test-only
  проект-проба `tests/Lzma.Ui.CwdProbe` (CWD меняется только в процессе пробы; глобальный CWD
  тест-раннера не затрагивается). В `src/Lzma.Ui/Lzma.Ui.csproj` добавлена одна строка
  `InternalsVisibleTo` для пробы; проект подключён к `LzmaSharp.sln`.
- Шаг A2 (RED) выполнен: до фикса тест падал по целевой причине —
  `Assert.Equal() Failure: Expected: "A1", Actual: "B1"`, то есть том публиковался
  из sentinel-файла непроверенного каталога.
- Шаг A3 (фикс) выполнен по утверждённому варианту B: в `StagedVolumeSet` добавлено поле
  замороженных operational-путей, заполняемое в `SetVolumes` из уже вычисленного
  `Path.GetFullPath` **только после** прохождения всех проверок; поздние файловые операции
  (precheck `Exists` и `FileNotFoundException.FileName`, источник `Move`, cleanup staged
  в `RollbackPublish`, `victims` в `Dispose`) используют замороженные пути. Observable
  `Manifest` по-прежнему возвращает исходные строки (контракт M6.2 не изменён).
  Дифф: 24 вставки, 4 удаления в одном файле `src/Lzma.Ui/Services/StagedVolumeSet.cs`.
- Фактический прогон после фикса (независимо перепроверен тимлидом):
  `Lzma.Ui.Tests` — **221 пройдено, 0 падений**; `Lzma.Core.Tests` — **1699 пройдено,
  0 падений**; `dotnet build LzmaSharp.sln -c Release` — 0 ошибок; новых предупреждений
  в изменённом файле нет (baseline полной пересборки — 67 уникальных).
- Независимое кросс-семейное ревью диффа (ревьюер `deepseek-v4-pro`; исполнители были из
  семьи `qwen`) дало вердикт **`APPROVE_WITH_COMMENTS`**: BLOCKING/MAJOR/MINOR нет,
  5 NIT — все документационные или теоретические, к коду диффа не относятся. Ревьюер
  самостоятельно воспроизвёл сборку и прогоны и сверил более 40 line-ссылок.
- Статус: ограничение устранено **в пределах решения F3-Q3** — «validate/use consistency для
  staged-путей manifest» (коммит `c789515`, тест-проба `tests/Lzma.Ui.CwdProbe`). M7 и
  глобальный gate SEC-002 выполнены после этого; заявлять атомарность или сохранение прежних
  байтов при перезаписи оснований нет.
- Остаточные ограничения (не устраняются этим фиксом):
  freeze лексический — symlink/reparse не разрешаются, ownership не проверяется,
  TOCTOU не устраняется (это SEC-003); destination-сторона (`_destinationBasePath`,
  `_stagedBasePath`, final/backup/conflict/probe-пути) остаётся зависимой от CWD;
  окно «CWD меняется во время записи томов» (`LzmaArchiveService.cs:225`–`:247`,
  `VolumeSpanningWriteStream.cs:129`) — отдельное известное ограничение;
  при не-rooted destination возможна повторная попытка удаления одного и того же
  staged-файла в `Dispose` (best-effort, описано в дизайне §6.4).
- Дефект был латентным (hardening контракта): в production относительных staged-путей
  не найдено, CWD в репозитории никто не меняет.
- Коммит кода и тестов создан по разрешению владельца: `c789515` «Безопасность: заморозить
  path identity staged-томов между SetVolumes и Commit». Документация — отдельным коммитом
  (правило «один шаг = два коммита»).
- Отклонение от F3-Q4a подтверждено ревьюером как приемлемое и предпочтительное: при отсутствии
  сборки пробы тест **падает** с явным сообщением вместо capability-skip, так как в `xunit 2.9.3`
  нет `Assert.Skip`, а `Xunit.SkippableFact` означал бы новую зависимость. Тихий пропуск
  исключён (пропущено 0 во всех прогонах).

F4 disposition (принято владельцем, без преувеличений):

- исторический mixed-scope коммита `bf0a14b` (M6-код + `.gitignore` + vendor)
  принят владельцем как есть; история не переписывалась;
- tracked `vendor/lzma-sdk/**` финальным config-follow-up не менялся;
- `git diff --check` по полному диапазону M5→HEAD остаётся ненулевым только из-за
  точного замороженного исторического исключения: 59 vendor-путей / 740 диагностик;
- проверка всех неисключённых путей — PASS; проверка нового config-commit — PASS;
- происхождение vendor (upstream identity) остаётся `UNVERIFIED`.

Не заявляется (вне доказанного scope M6):

- атомарность набора томов при любом отказе;
- crash/power-loss recovery;
- безопасное извлечение недоверенных архивов;
- проверенная идентичность upstream/vendor;
- полная platform/security CI-матрица: сборка, тесты и interop-проверки с внешним 7-Zip
  зелёные на ubuntu/windows/macos (run `38804489734`, HEAD `2d8a84d`). За пределами CI
  по-прежнему остаются: crash/power-loss recovery, фаззинг, SBOM/advisory/pinning (G8),
  а также 4 Windows-only теста с тихим `return` (устраняются после миграции на xUnit v3).

Открытые hardening/preflight items (в M6 не входили):

- F2/AUD-004: `Manifest` возвращал mutable backing list — ЗАКРЫТО (`042ffeb`,
  закреплён тестом immutability);
- F3/AUD-005: validate/use consistency для относительных путей — ЗАКРЫТО в пределах F3-Q3
  (`c789515`, тест `SetVolumes_RelativePaths_UseFrozenValidatedPathIdentity`);
- M7: double-fault recovery (rollback-failure матрица) — ЗАКРЫТО (`d8c43e7`, тесты матрицы
  `437edb9`: двойной отказ, несколько restore-отказов, неконтролируемый отказ, единственный backup);
- SEC-003: symlink/junction/reparse/TOCTOU;
- MNT-F4-*: deferred maintenance (vendor provenance, локальные `bin/**`,
  output/agent-политики, GOST/RFC, evaluated build/package exclusion).

## 5. Остаточные ограничения SEC-002

Маршрут **создания** архивов реализован транзакционно и прошёл глобальный gate
(`APPROVE_WITH_FINDINGS`, BLOCKING/MAJOR нет, HEAD `412c563`, прогон 1924 passed / 0 failed /
0 skipped). Ниже — пункты, которые остались открытыми или не доказаны; они не позволяют
заявлять атомарность, crash/power-loss recovery или закрытие SEC-001.

### Пункты multi-volume и их статус

1. Backup-файлы после успешного commit — закрыто (M4B).
2. Delete-by-scan дополнительных `{base}.NNN` без доказательства ownership —
   закрыто (M5).
3. State machine для повторного `Commit`, failed/rolled-back/disposed — закрыто (M6).
4. Manifest validation — закрыто (M6).
5. Широкий `Exception` в backup-фазе — закрыто (M6).
6. Недостаточно тестов ошибок самого rollback — ЗАКРЫТО (M7: `d8c43e7` + матрица `437edb9`,
   регрессионная чувствительность доказана мутационной проверкой).
7. Поведение при cleanup failure после commit — закрыто (M4B).
8. Нет crash/power-loss recovery journal — ОТКРЫТО. Это допустимое остаточное
   ограничение, но оно должно быть честно документировано.
9. Windows/Linux/macOS CI matrix для файловых транзакций — ЗАКРЫТО: матрица ОС добавлена
   (`e04c90e`), первый прогон (`38773220694`, HEAD `a25a411`) выявил 4 падения на ubuntu/macos,
   причина устранена (`2d90417`, `020d74e`), контрольный прогон (`38794891354`, HEAD `be413de`)
   — все три ОС success. Остаток (interop-проверки с внешним 7-Zip в CI) закрыт шагом G2:
   run `38804489734`, HEAD `2d8a84d` — success на трёх ОС при явном требовании 7-Zip в тестах.

### Другие риски и их статус

- F3/AUD-005: validate/use consistency для staged-путей manifest — ЗАКРЫТО в пределах F3-Q3
  (`c789515`, тест `SetVolumes_RelativePaths_UseFrozenValidatedPathIdentity`);
  destination-сторона остаётся CWD-зависимой (см. следующий пункт);
- Окно «CWD меняется во время записи томов» (`LzmaArchiveService.cs:225`–`:247`,
  `VolumeSpanningWriteStream.cs:129`) — отдельное известное ограничение, в F3 не исправляется;
- RES-002/RES-003: scope уточнён — packed-регион дополнительно ограничен
  `SevenZipFolderDecoder.cs:278`/`:282`, проверка `int.MaxValue` есть и в
  LZMA1-ветке (`:1043`–`:1044`, `:1625`–`:1626`); карточки требуют уточнения
  до выдачи шагов фазы C;
- Документация синхронизирована по правилу doc-tail, но коммит не создан
  (делает владелец);
- SEC-001: транзакционное извлечение и безопасный overwrite;
- SEC-003: symlink/junction/reparse/TOCTOU;
- SEC-004: единый resource budget;
- SEC-005: KDF limits/cancellation;
- SEC-006: bounded-memory WinZip-AES extraction;
- SEC-007: GOST quarantine/authenticated format;
- SEC-008: время жизни секретов в UI;
- SEC-009: typed error boundary;
- SEC-010: checked metadata arithmetic;
- SEC-011: platform/security CI.

## 6. Текущий рабочий пакет

Текущий пакет (фаза B) ограничен только:

```text
SEC-001 / transactional extraction (стадинг, публикация после проверки,
                                  откат с восстановлением прежних байт)
```

Предыдущий пакет `SEC-002 / multi-volume transactional publication` (M4B–M9, F3, глобальный gate
`APPROVE_WITH_FINDINGS`) завершён по production-коду; merge в `main` выполняется вручную владельцем
(A12) и на момент этой записи не выполнен.

Вне scope текущего пакета:

- архитектурное разделение (Compression / Cryptography / Containers / ArchiveIO);
- ZIP internals и кодеки вне маршрута извлечения;
- криптография (KDF/AES — фаза E) и политика ошибок (фаза F);
- resource budget (RES-001/002/003 — фаза C);
- package separation, mobile heads;
- style cleanup, warnings cleanup;
- отложенное отдельным решением владельца: B4c (todo #47), SEAM-1/SEAM-2, xUnit1051 (todo #35),
  G8 (SBOM/advisory/pinning), DOC-017, capability-probe (todo #39).

Синхронизация документации из «вне scope» исключена: doc-tail выполняется после каждого шага
(правило владельца от 2026-10-12; процедура — `docs/plans/DOC_B_DOCUMENTATION_SYNC_PLAN.md`
и реестр `docs/plans/04_DOCUMENTATION_AUDIT.md`).

## 7. Обязательный принцип маленьких шагов

### 7.1 Общие правила

Один микрошаг:

- одна причина изменения;
- одна проверяемая цель;
- не более двух production-файлов;
- не более одного test-файла;
- точный allowlist;
- targeted build/test;
- отдельный read-only review;
- отдельный commit только после зелёных проверок и `APPROVE`.

После двух неудачных исправлений одной причины:

```text
BLOCKED
```

Запрещено начинать задачу заново и переписывать зелёный код.

### 7.2 Новый файл

Новый production-файл создаётся по фазам:

1. каркас;
2. build;
3. одна функция;
4. tests этой функции;
5. review;
6. commit;
7. следующая функция.

Исключение допускается только для очень малого test helper, если его контракт
уже полностью утверждён и diff остаётся локальным.

### 7.3 Красный regression test

Красный тест:

- должен падать из-за конкретного дефекта;
- не должен падать из-за setup, compile error или неверной инъекции;
- не коммитится в `main` отдельно;
- остаётся в feature-ветке до минимального production-fix;
- после исправления становится блокирующим зелёным тестом.

## 8. Микрошаги: статус и очередь

M4B, M5 и M6 завершены (см. §4); далее — M7, затем M8/M9.

### SEC002-M4B — commit point и cleanup operation backups

Статус: завершён (см. §4.4).

Цель:

- установить commit point после успешной публикации полного нового набора;
- удалить только backups текущей операции;
- cleanup failure после commit не должен откатывать корректный новый набор;
- обычный успех не оставляет `.bak`.

Файл промпта:

```text
agent-prompts/SEC002_M4B_COMMIT_POINT_AND_BACKUP_CLEANUP.md
```

### SEC002-M5 — убрать delete-by-scan

Статус: завершён (см. §4).

Цель:

- не удалять произвольный `{base}.NNN`;
- обнаруживать дополнительный numbered-файл до первой мутации;
- сохранять его байт-в-байт;
- возвращать контролируемый конфликт;
- изменить прежний тест удаления хвостовых томов только после явного утверждения новой политики.

### SEC002-M6 — transaction state и manifest validation

Статус: завершён; independent final review `APPROVE` на
`a6bc98ad3eee1cbe9f6c853687874a45fb364fff` (см. §4).

Цель:

- запрет повторного `Commit`;
- состояния created/in-progress/committed/rolled-back/failed/disposed;
- проверка пустого, дублирующегося и отсутствующего manifest;
- staged/final path collision;
- контролируемые исключения backup-фазы.

### SEC002-M7 — rollback failure tests

Цель:

- Delete опубликованного final падает;
- restore backup падает;
- staged cleanup падает;
- primary exception сохраняется;
- rollback errors остаются диагностируемыми;
- операция никогда не сообщает успех.

### SEC002-M8 — service integration и final gates

Проверить:

- реальный call graph `LzmaArchiveService`;
- отсутствие обходного multi-volume пути;
- все `Lzma.Ui.Tests`;
- полный solution build/test;
- независимый reviewer;
- глобальный adversarial critic.

### SEC002-M9 — документация и merge

После production-gates:

- исправить статус SEC-002;
- описать точный scope:
  controlled in-process failures;
- не заявлять atomicity нескольких файлов на уровне ОС;
- отдельно указать отсутствие crash/power-loss recovery;
- слить feature-ветку в `main`;
- удалить worktree/ветку только после push и проверки.

## 9. Критерии закрытия SEC-002

SEC-002 можно считать закрытым только если одновременно:

- одиночный 7z, ZIP и in-memory write сохраняют прежний destination при ошибке;
- multi-volume rollback восстанавливает старый набор на всех проверенных fault points;
- успешная публикация не оставляет operation backups;
- дополнительные `.NNN` не удаляются без доказанного ownership;
- повторный `Commit` запрещён;
- controlled rollback failures не маскируют primary failure;
- full tests зелёные;
- reviewer дал `APPROVE`;
- global critic дал `CRITIC_APPROVE`;
- документация описывает только фактически доказанный scope.

## 10. Что делать после SEC-002

1. Синхронизировать документацию.
2. Отдельной веткой выполнить warnings/style audit.
3. Перейти к SEC-001.
4. Затем SEC-004 + SEC-010.
5. Затем SEC-003.
6. Затем SEC-005 и SEC-006.
7. Только после стабилизации security-contract начинать физическое разделение
   Compression/Cryptography/Containers/ArchiveIO и mobile heads.

## 11. Правило обновления документа

После каждого завершённого пакета обновить:

- `last_verified_date`;
- commit `main`;
- commit рабочей ветки;
- статусы микрошагов;
- следующий task ID;
- остаточные риски;
- ссылки на блокирующие tests.

Если документ не обновлён, источником истины остаются код и тесты на текущем commit.
