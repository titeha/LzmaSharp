---
doc_kind: current-work-plan
status: active
project: LzmaSharp
last_verified_date: 2026-09-24
main_branch: main
main_commit: 3a55297106e6a978b460faa5bfe3b1a1b85b1041
active_work_branch: fix/sec002-multivolume-transaction
active_work_commit: a6bc98ad3eee1cbe9f6c853687874a45fb364fff
next_task: SEC002-M7
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
- завершённая platform/security CI-матрица.

Открытые hardening/preflight items (в M6 не входили):

- F2/AUD-004: `Manifest` возвращает mutable backing list;
- F3/AUD-005: validate/use consistency для относительных путей;
- M7: double-fault recovery (rollback-failure матрица);
- SEC-003: symlink/junction/reparse/TOCTOU;
- MNT-F4-*: deferred maintenance (vendor provenance, локальные `bin/**`,
  output/agent-политики, GOST/RFC, evaluated build/package exclusion).

## 5. Почему SEC-002 ещё не закрыт

SEC-002 считается **частично реализованным**, пока не выполнены все пункты ниже.

### Открытые пункты multi-volume

1. Backup-файлы после успешного commit — закрыто (M4B).
2. Delete-by-scan дополнительных `{base}.NNN` без доказательства ownership —
   закрыто (M5).
3. State machine для повторного `Commit`, failed/rolled-back/disposed — закрыто (M6).
4. Manifest validation — закрыто (M6).
5. Широкий `Exception` в backup-фазе — закрыто (M6).
6. Недостаточно тестов ошибок самого rollback — ОТКРЫТО (M7).
7. Поведение при cleanup failure после commit — закрыто (M4B).
8. Нет crash/power-loss recovery journal — ОТКРЫТО. Это допустимое остаточное
   ограничение, но оно должно быть честно документировано.
9. Нет Windows/Linux/macOS CI matrix для файловых транзакций — ОТКРЫТО.

### Другие открытые риски

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
