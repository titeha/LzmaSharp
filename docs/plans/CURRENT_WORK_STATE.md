---
doc_kind: current-work-plan
status: active
project: LzmaSharp
last_verified_date: 2026-09-14
main_branch: main
main_commit: 3a55297106e6a978b460faa5bfe3b1a1b85b1041
active_work_branch: fix/sec002-multivolume-transaction
active_work_commit: fd5904a34881dd90655715503581d8f50a1d894f
next_task: SEC002-M5
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
commit: fa0313dcef1f39febcc80cb600612fd52f4ad87d
date:   2026-08-27
```

В ней сохранены:

- узкий `IStagedVolumeFileOperations`;
- инъекция файловых отказов;
- backup-фаза существующих управляемых томов;
- rollback ошибок backup-фазы;
- журнал опубликованных новых томов;
- rollback частичной публикации;
- тесты отказа до первого, после первого и на последнем publish.

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

## 5. Почему SEC-002 ещё не закрыт

SEC-002 считается **частично реализованным**, пока не выполнены все пункты ниже.

### Открытые пункты multi-volume

1. После успешного commit backup-файлы не очищаются.
2. Successful path всё ещё удаляет дополнительные `{base}.NNN` через scan,
   не имея доказательства ownership.
3. Нет полной state machine для повторного `Commit`, failed/rolled-back/disposed.
4. Manifest валидируется недостаточно.
5. Backup-фаза ловит слишком широкий `Exception`.
6. Недостаточно тестов ошибок самого rollback.
7. Не определено поведение при cleanup failure после commit.
8. Нет crash/power-loss recovery journal. Это допустимое остаточное ограничение,
   но оно должно быть честно документировано.
9. Нет Windows/Linux/macOS CI matrix для файловых транзакций.

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

## 8. Следующие микрошаги

### SEC002-M4B — commit point и cleanup operation backups

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

Цель:

- не удалять произвольный `{base}.NNN`;
- обнаруживать дополнительный numbered-файл до первой мутации;
- сохранять его байт-в-байт;
- возвращать контролируемый конфликт;
- изменить прежний тест удаления хвостовых томов только после явного утверждения новой политики.

### SEC002-M6 — transaction state и manifest validation

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
