---
doc_kind: design
status: approved (APPROVE_WITH_COMMENTS, независимое ревью A1-R)
severity: Medium (conditional); внешняя карточка AUD-005 — High
scope: StagedVolumeSet validate/use consistency для staged-путей manifest
implementation_started: false
design_head_commit: 042ffeb
design_branch: fix/sec002-multivolume-transaction
design_date: 2026-10-13
---

# F3 / AUD-005 — дизайн frozen path identity

## 1. Статус

- Дизайн утверждён владельцем (вариант B, решения F3-Q1…F3-Q8, F3-Q4a).
- Независимое ревью дизайна A1-R (deepseek-v4-pro): `APPROVE_WITH_COMMENTS`, BLOCKING/MAJOR нет.
- Реализация **внесена** шагами A2 (RED-тест) и A3 (фикс) и проверена прогоном:
  `Lzma.Ui.Tests` 221/0, `Lzma.Core.Tests` 1699/0, сборка решения — 0 ошибок.
- Независимое кросс-семейное ревью диффа (`deepseek-v4-pro`, исполнители были из семьи `qwen`)
  дало **`APPROVE_WITH_COMMENTS`**: BLOCKING/MAJOR/MINOR нет, 5 NIT документационного и
  теоретического характера. Ревьюер независимо воспроизвёл сборку и оба прогона и сверил
  более 40 line-ссылок.
- Отклонение от F3-Q4a (fail-fast вместо capability-skip при несобранной пробе) признано
  ревьюером приемлемым и предпочтительным: `Assert.Skip` в `xunit 2.9.3` отсутствует,
  а `Xunit.SkippableFact` означал бы новую зависимость; тихий пропуск исключён.
- Реализация закоммичена по разрешению владельца: `c789515` «Безопасность: заморозить
  path identity staged-томов между SetVolumes и Commit» (код и тесты); документация —
  отдельным коммитом. Дизайн зафиксирован при HEAD `042ffeb`.
- Статус риска: ограничение устранено только в пределах F3-Q3 («validate/use consistency для
  staged-путей manifest»). SEC-002 остаётся открытым (впереди M7, global gate, doc sync, merge);
  оснований заявлять атомарность или закрытие SEC-002 нет.

## 2. Проблема

`StagedVolumeSet.SetVolumes` (`src/Lzma.Ui/Services/StagedVolumeSet.cs`) нормализует пути
через `Path.GetFullPath` только для валидации:

- `:150` — проверка дубликатов;
- `:164` — проверка коллизии с финальным путём.

Значение `normalized` после проверки отбрасывается. В `_manifest` (`:55`) сохраняются
**исходные строки** (`:176`).

Поздние файловые операции используют эти исходные строки:

- `:228`–`:230` — pre-mutation existence precheck (`_fileOperations.Exists(stagedPath)`);
- `:232`–`:234` — `FileNotFoundException` с `FileName = stagedPath`;
- `:290`–`:294` — публикация: `Move(_manifest[i], finalPath)`;
- `:378`–`:380` — rollback: `TryDelete(staged)`;
- `:480`–`:503` — `Dispose`: `victims = new List<string>(_manifest)` → `Delete(path)`.

Все эти операции разрешают относительные пути относительно `Environment.CurrentDirectory`
на момент вызова. Если CWD изменился между `SetVolumes` и `Commit`/`Dispose`, проверенное
множество путей может отличаться от множества, над которым выполняется I/O.

## 3. Квалификация риска

Дефект **латентный** (hardening контракта, defense-in-depth):

- В production относительных staged-путей не найдено. Единственная цепочка:
  `MainViewModel.cs:1828` → `LzmaArchiveService.cs:218`/`:257` → путь из
  `AvaloniaSaveFilePicker` (`:33`, `file.TryGetLocalPath()`).
- CWD в репозитории никто не меняет: 0 вхождений `SetCurrentDirectory` /
  `Environment.CurrentDirectory` в `src/` и `tests/`.
- `Path.IsPathRooted` не используется (0 вхождений).

Риск — для внешних потребителей публичного контракта `IArchiveService`
(`src/Lzma.Ui/Services/IArchiveService.cs:153`, `:161`, `:217`): формальной гарантии
абсолютности `destinationPath` в коде нет.

Severity: Medium (conditional) по внутренней оценке
(`01_AUDIT_SUMMARY.md`); внешняя карточка AUD-005 — High.

## 4. Утверждённое решение — вариант B

В `SetVolumes` один раз вычисляется frozen (канонический) operational path
через уже имеющийся `Path.GetFullPath` (`:150`). Значение сохраняется во
внутреннем поле (параллельный `List<string>`, без нового типа). Все поздние
I/O-операции (`Commit`/`Dispose`/`ProbeStagedVolumes`) используют frozen-пути.

Observable `Manifest` (`:99`, `_manifest.AsReadOnly()`) продолжает возвращать
исходные строки — контракт M6.2 не меняется.

### Отклонённые варианты

- **A** (запретить не-rooted staged-пути): противоречит закреплённому контракту
  sibling-типа `StagedDestination` (`StagedDestinationTests.cs:71`–`:76`) и
  относительному branch `BuildStagedBasePath` (`:536`–`:540`); не закрывает
  destination-сторону.
- **C1** (freeze только не-rooted): разводит identity валидации и I/O для
  rooted-неканонических входов — нарушение требования ревью
  («валидация и I/O используют одну и ту же frozen path identity»).
- **C2** (frozen в `_manifest`, оригиналы отдельно): меняет способ построения
  observable `Manifest` (`:99`) — повышенный риск F2-регрессии при том же поведении.
- **C3** (freeze destination/staged-base в конструкторе): меняет observable
  `StagedBasePath` (`:96`) и `LzmaArchiveService.cs:226`; выходит за allowlist A3.
  Зарезервирован как отдельный follow-up.

## 5. Границы гарантии

Честно фиксируется, чего решение **НЕ** даёт:

1. Freeze **лексический** (`Path.GetFullPath`): не разрешает symlink/reparse-точки,
   не проверяет ownership, не устраняет TOCTOU. Это SEC-003 (фаза D), отдельная работа.
2. Класс **не** становится полностью CWD-независимым. Destination-сторона
   (`_destinationBasePath` `:88`, `_stagedBasePath` `:89`, final/backup/conflict/probe-пути
   `:241`/`:292`/`:415`/`:516`, `BuildStagedBasePath` `:535`–`:543`) остаётся зависима от CWD.
3. Окно «CWD меняется во время записи томов» (`LzmaArchiveService.cs:225`–`:247`,
   `VolumeSpanningWriteStream.cs:129`) остаётся открытым известным ограничением.
   Минимальный freeze manifest это окно не закрывает.
4. Никаких гарантий атомарности, сохранения существующего архива или обязательной
   очистки временных файлов решение не добавляет.

## 6. Решения владельца (F3-Q1…F3-Q8, F3-Q4a)

| ID | Суть |
|---|---|
| F3-Q1 | Вариант B утверждён; A и C1/C2/C3 отклонены; C3 — отдельный follow-up |
| F3-Q2 | `FileNotFoundException.FileName` = frozen (канонический) путь; текст сообщения не меняется |
| F3-Q3 | Destination-сторона не расширяется; закрытие F3 формулируется ограниченно: «validate/use consistency для staged-путей manifest» |
| F3-Q4 | H2: новый test-only проект `tests/Lzma.Ui.CwdProbe` + одна строка `InternalsVisibleTo` в `src/Lzma.Ui/Lzma.Ui.csproj` |
| F3-Q4a | Проба включается в `LzmaSharp.sln` и CI; при отсутствии бинарника — capability-skip с явной причиной |
| F3-Q5 | In-process seam-тест отложен до TST-003 (журналирование `Exists` в `StagedFileOperationsFake`) |
| F3-Q6 | Severity AUD-005: Medium (conditional) с оговоркой про внешнюю оценку High |
| F3-Q7 | Коррекция ссылок в планах выполняется (A1-cite, A1-cite-2) |
| F3-Q8 | Окно «CWD меняется во время записи томов» — отдельное известное ограничение, в A3 не исправляется |

## 7. План проверки

RED-тест `SetVolumes_RelativePaths_UseFrozenValidatedPathIdentity` в отдельном
процессе-пробе (`tests/Lzma.Ui.CwdProbe`, F3-Q4 = H2). CWD меняется только
в дочернем процессе; глобальный CWD тест-раннера не затрагивается.

Наблюдаемые инварианты (проверяются родителем по фактическому состоянию ФС):

- **И1**: байты целевых файлов в `dirA` совпадают с staged-байтами из `dirA`
  (опубликованы именно те байты, чья path identity была валидирована).
- **И2**: sentinel-файлы в `dirB` не тронуты.
- **И3**: staged-файлы в `dirA` потреблены (удалены публикацией/очисткой).
- **И4**: в `dirA` нет файлов `*.bak` (backup cleanup после commit point).
- **И5**: каждый путь, переданный в seam, — rooted (после фикса).
- **И6**: `Manifest` — исходные относительные строки (pin M6.2, и на base, и после фикса).

Почему красный до фикса: `_manifest` содержит относительные строки, `Exists`/`Move`
разрешаются относительно CWD=`dirB` → публикация переносит sentinel-байты из `dirB`
в `dirA`, нарушая И1/И2/И3.

Почему зелёный после фикса: frozen-пути абсолютны, все поздние операции идут по ним
независимо от CWD → И1–И6 выполняются.

Режим `--mode control` (без смены CWD) — негативный контроль: проходит и на base,
доказывая, что RED вызван именно сменой CWD.

## 8. Совместимость и остаточные риски

1. **Лексическая нормализация уже-rooted строк**: `Path.GetFullPath` может изменить
   строку (схлопывание `.`/`..`, дублирующихся разделителей, унификация `/`→`\`
   на Windows). Существующие тесты сравнивают журнал операций посимвольно
   (`StagedVolumeSetTests.cs:104`–`:107`, `:338`–`:339`, `:431`–`:436`,
   `:1306`–`:1309`). Фактические входы — `Path.Combine(Path.GetTempPath(), …)` +
   суффиксы `.00N` — канонические, поэтому ожидается идентичность, но подтвердить
   можно только прогоном.
2. **Двойной `Delete` в Dispose-dedupe** (`:480`–`:487`): при не-rooted destination
   строки manifest и `ProbeStagedVolumes()` после freeze перестанут совпадать →
   один физический файл получит два `Delete`. Для реального `File.Delete` вторая
   операция — no-op; для fake — лишний элемент в `DeleteCalls`. Рекомендуется
   зафиксировать в отчёте A3 и **не** менять comparer на `:483`.
3. **Публичный API не меняется**: `IStagedVolumeFileOperations`, `IArchiveService`,
   observable-свойства `Manifest`/`DestinationBasePath`/`StagedBasePath` — без изменений.

## 9. Что дальше

1. **A2** — RED-тест F3 (frozen path identity): дочерний процесс-проба + assertions.
2. **A3** — минимальный fix: frozen operational path в `StagedVolumeSet`.
3. Независимое ревью A3-R.
4. Глобальный gate SEC-002 (SEC002-M8/M9).

Ссылки:

- `docs/plans/SECURITY_REMEDIATION_PLAN.md` — текущий security-процесс.
- `docs/plans/CURRENT_WORK_STATE.md` — состояние работ и очередь.
