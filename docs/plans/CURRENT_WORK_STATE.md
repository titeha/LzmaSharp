---
doc_kind: current-work-plan
status: active
project: LzmaSharp
last_verified_date: 2026-10-13
main_branch: main
main_commit: 3a55297106e6a978b460faa5bfe3b1a1b85b1041
active_work_branch: fix/sec002-multivolume-transaction
active_work_commit: 7a2d792
next_task: "фаза B (SEC-001) — транзакционное извлечение; ожидается команда запуска"
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

Текущий пакет ограничен только:

```text
SEC-002 / multi-volume transactional publication
```

Вне scope:

- extraction;
- ZIP internals;
- codecs;
- cryptography;
- resource budget;
- package separation;
- mobile heads;
- style cleanup;
- warnings cleanup;
- documentation synchronization до завершения production-пакета.

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
