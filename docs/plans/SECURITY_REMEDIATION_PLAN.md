# LzmaSharp: актуальный план устранения рисков и программа тестирования

> Статус документа: текущий рабочий план.
>
> Дата актуализации: 2026-08-11.
>
> База: ветка `main`, HEAD `70c0493` — фаза RH-001 закрыта: `ThrowBeforeCrossingWriteStream` покрыт 25 тестами синхронных/асинхронных сценариев (число проверено на этом HEAD).
>
> Перед каждой задачей агент обязан получить фактический `HEAD`, проверить пути и не считать риск устранённым только потому, что код перемещён или переименован.

## 1. Текущее состояние

Уже выполнено:

- README уточняет ограничения streaming, encrypted ZIP и mobile portability;
- добавлен test-only helper `ThrowBeforeCrossingWriteStream`;
- helper проверяет отказ до пересекающей записи;
- покрыты array, span, `WriteByte`, `Task WriteAsync`, `ValueTask WriteAsync`, cancellation и sticky-failure;
- локальные тесты helper прошли;
- контракт helper-а по RH-001 закрыт на уровне test harness (HEAD `70c0493`): `inner == null`, отрицательный `byteLimit`, `leaveOpen=true/false`, проброс исключения из `Write` inner-потока, неизменность счётчика wrapper при ошибке inner, поведение `Write` после `Dispose`;
- всего 25 targeted-тестов helper-а (число проверено на HEAD `70c0493`).

Это только часть regression harness. Production-дефекты ещё не считаются исправленными.

Открытые направления:

| ID | Риск | Статус |
|---|---|---|
| SEC-001 | потеря существующих файлов при extract + overwrite | открыт |
| SEC-002 | создание архива напрямую в конечный путь | закрыт 2026-08-12 (HEAD `10377e2`; блокирующие регрессионные тесты зелёные; независимый критик APPROVE) |
| SEC-003 | symlink/junction/reparse/TOCTOU | открыт |
| SEC-004 | отсутствие общего resource budget | открыт |
| SEC-005 | KDF CPU/memory DoS и слабая отменяемость | открыт |
| SEC-006 | WinZip-AES member целиком в памяти | открыт |
| SEC-007 | experimental GOST без archive authentication tag | открыт |
| SEC-008 | долгоживущие password `string` | открыт |
| SEC-009 | нестабильная/нетипизированная граница ошибок | открыт |
| SEC-010 | переполнение размеров и смещений | открыт |
| SEC-011 | недостаточная CI/security matrix | открыт |

## 2. Обязательный способ работы

Любой пункт выполняется маленькими проверяемыми шагами.

### 2.1. Размер шага

Один шаг:

- одна причина изменения;
- один класс или одна функция;
- максимум два production-файла;
- максимум один тестовый файл либо один небольшой test helper;
- один независимый критерий приёмки;
- один отдельный commit после зелёной проверки.

Нельзя объединять в одном шаге:

- новый abstraction layer;
- миграцию всех call sites;
- исправление безопасности;
- архитектурное разделение;
- обновление UI;
- массовое обновление документации.

### 2.2. Для нового файла

Новый файл создаётся по фазам:

1. **Каркас**
   - namespace;
   - тип;
   - поля;
   - constructor;
   - минимально необходимые abstract/override members;
   - без функционала кроме `NotImplementedException("STEP_N")`.
   - Проверка: сборка проекта.

2. **Одна функция**
   - реализуется ровно одна функция или overload;
   - существующий работающий код не переписывается;
   - проверка: сборка.

3. **Тесты функции**
   - positive;
   - boundary;
   - negative/failure;
   - side-effect invariants.
   - Проверка: только целевые тесты.

4. **Следующая функция**
   - только после зелёного предыдущего шага.

5. **Финальная интеграция**
   - полный targeted suite;
   - полный `dotnet test` один раз;
   - read-only review;
   - commit.

### 2.3. Правило «не ломаем то, что работает»

- Не переписывать зелёный участок без доказанного дефекта.
- Не улучшать соседний код попутно.
- Не менять публичный API без отдельного решения.
- После ошибки — одно минимальное исправление, а не перезапуск задачи.
- Максимум два цикла исправления одной проблемы.
- После второго неуспеха — `BLOCKED`, текущий diff сохраняется для разбора.
- Не использовать `git reset --hard`, `git clean`, force push.
- Commit создаётся только после сборки, целевых тестов и проверки diff.

## 3. Завершение regression harness

### RH-001. Закрыть контракт `ThrowBeforeCrossingWriteStream`

Статус: закрыт на уровне test harness (HEAD `70c0493`). Это harness-статус, а не production-fix: SEC-001/SEC-002 остаются открытыми. Решение по пункту 7 закреплено тестом: `Write` после `Dispose` бросает `ObjectDisposedException`.

Выполненные микрошаги:

1. Тест `inner == null`.
2. Тест отрицательного `byteLimit`.
3. Тест `leaveOpen=true`.
4. Тест `leaveOpen=false`.
5. Test stream, бросающий из собственного `Write`.
6. Проверка: счётчик wrapper не увеличивается при ошибке inner.
7. Явно решить и тестом закрепить поведение wrapper после `Dispose`.

Не менять helper и тесты всех пунктов одновременно. Один пункт — один микрошаг.

### RH-002. Отдельный `ThrowOnFlushStream`

Нужен как иной fault model:

- каркас;
- sync `Flush`;
- тест failure;
- async `FlushAsync`;
- cancellation;
- `leaveOpen`.

Не объединять его с `ThrowBeforeCrossingWriteStream`.

### RH-003. Виртуальный большой поток

Создать позже отдельным пакетом:

- каркас `VirtualPatternReadStream`;
- `Length`/`Position`;
- синхронный read;
- seek;
- async read;
- тесты > `int.MaxValue`;
- тесты > 4 GiB;
- отсутствие аллокации пропорционально `Length`.

## 4. SEC-002 — транзакционное создание архива

Это следующий production-приоритет.

### 4.1. Сначала characterization

Отдельные read-only задачи:

1. Найти все path-based create entry points.
2. Найти все места `FileMode.Create`/`File.WriteAllBytes` для конечного архива.
3. Отдельно описать ZIP, 7z, AES/GOST и multi-volume.
4. Составить call graph без изменений.

### 4.2. Первый красный тест

Первый production regression:

```text
Create_DestinationWriteFailure_PreservesExistingArchive
```

Инварианты:

- заранее существует архив с известными байтами;
- операция начинает создание;
- выходной поток бросает `IOException`;
- старый архив остаётся byte-for-byte прежним;
- новый partial archive не публикуется;
- временные файлы не остаются.

Тест должен сначала доказанно падать на текущем production path.

### 4.3. Минимальный seam

Не вводить универсальную файловую систему всего проекта.

Первый seam должен позволять только:

- создать staged destination;
- передать writer-у seekable output;
- commit;
- rollback/cleanup.

### 4.4. Реализация по шагам

1. Internal interface/record для staged destination — каркас.
2. Один temp-file path на той же файловой системе.
3. Запись во staging для одного обычного 7z path.
4. Тест write failure.
5. Commit нового файла без existing target.
6. Тест success.
7. Replacement existing target.
8. Тест preservation on failure.
9. ZIP подключается отдельным шагом.
10. Multi-volume — отдельная последняя фаза с manifest.

Нельзя за один шаг подключать 7z, ZIP и multi-volume.

### 4.5. Статус реализации

Реализовано на HEAD `10377e2` (2026-08-12), коммиты `63186c8`, `baf4768`,
`0cb44de`, `10377e2`:

- characterization — `docs/plans/SEC-002_CHARACTERIZATION.md`;
- шаги 1–8 (одиночный 7z-путь), шаг 9 (ZIP), шаг 10 (multi-volume с
  manifest) выполнены; дополнительно staged-запись подключена к
  in-memory-пути `WriteArchiveAsync` (точка W1 characterization);
- seam — внутренние типы `StagedDestination` и `StagedVolumeSet` в
  `Lzma.Ui.Services`, без универсальной файловой абстракции (по §4.3);
- целевые блокирующие регрессионные тесты существуют и проходят:
  сохранность существующего архива/томов при отказе (валидация, отказ
  чтения источника, отказ публикации), успех/commit, replacement,
  удаление лишних старых томов, чистота каталога от staged-остатков;
  suite 198 тестов зелёный на HEAD `10377e2` (число проверено прогоном).

Ограничения (known limitation):

- отказ в тестах воспроизводится поведением writer-а и файловой системы;
  seam не предоставляет тестовой инъекции `IOException` в выходной поток
  сервиса (в characterization зафиксировано как альтернативный критерий);
- для multi-volume публикация идёт несколькими `File.Move`: сбой в середине
  коммита может оставить частично опубликованный набор (окно узкое,
  одинаковое по природе с любым переносом); откат частичной публикации без
  дополнительного журнала невозможен; одиночный файл и ZIP публикуются одним
  переносом;
- удаление устаревших томов при коммите удаляет любые файлы с именами
  `{база}.NNN` независимо от их происхождения: файлы с такими именами,
  созданные не записью томов, будут потеряны при успешной замене (в рамках
  скоупа SEC-002 приемлемо, зафиксировано критиком);
- пути распаковки не изменялись — это скоуп SEC-001.

Независимая проверка: APPROVE (вердикт `c:\Temp\SEC-002_phase_critic_review.md`,
2 minor + 2 nit, без critical/major; оба minor отражены в ограничениях выше).
SEC-002 закрыт 2026-08-12 на HEAD `10377e2`.

## 5. SEC-001 — транзакционное извлечение

Начинать только после стабилизации staged output creation.

Микрофазы:

1. Красный тест на один existing file.
2. Staged file без overwrite.
3. CRC verification до publish.
4. Existing target backup.
5. Publish одного файла.
6. Rollback одного файла.
7. Multiple entries.
8. Cancellation.
9. ZIP path.
10. 7z path.
11. Directories journal.
12. Platform tests.

## 6. Resource budget и checked arithmetic

Не протягивать сразу через весь проект.

Порядок:

1. Immutable `ArchiveResourceBudget` — только модель + validation tests.
2. `ArchiveResourceCounter` — один counter за шаг.
3. Checked size helper.
4. Один parser call site.
5. Один decoder path.
6. ZIP.
7. 7z.
8. KDF.
9. Temporary storage.
10. Profiles — только после измерений.

## 7. Что локальной модели не поручать автономно

Требует облачного проектирования или обязательного облачного review:

- symlink/junction/reparse/TOCTOU guarantees;
- crash consistency между процессами;
- WinZip-AES streaming + tag verification;
- новый GOST authenticated wire format;
- выбор KDF policy;
- финальная public error model;
- окончательный module split;
- Android/iOS runtime/AOT readiness.

Локальная модель может:

- собрать факты;
- создать test helper;
- написать один regression test;
- реализовать один заранее утверждённый метод;
- провести read-only review малого diff.

## 8. Универсальная проверка шага

До изменения:

```text
git status --short
git branch --show-current
```

После изменения:

```text
git diff --check
git diff --name-only
dotnet build <affected-project> -c Release
dotnet test <test-project> -c Release --no-build --filter "<target>"
```

Перед завершением production-задачи:

```text
dotnet build -c Release
dotnet test -c Release --no-build
```

## 9. Definition of Done микрошагa

Микрошаг завершён, когда:

- изменены только allowlist-файлы;
- код собирается;
- новый targeted test зелёный;
- прежние targeted tests зелёные;
- diff не содержит попутных изменений;
- нет новых warnings;
- critic review дал `APPROVE`;
- commit сделан отдельно;
- следующий шаг ещё не начат.

## 10. Definition of Done безопасности

Библиотека не может заявлять безопасную обработку недоверенных архивов, пока одновременно не выполнены:

- transactional create;
- transactional overwrite extraction;
- resource budget;
- checked metadata arithmetic;
- link/reparse threat model;
- bounded/cancellable KDF;
- authenticated-output staging;
- experimental crypto disabled by default;
- CI Windows/Linux/macOS;
- regression corpus и security tests.
