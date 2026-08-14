# LzmaSharp: актуальный аудит документации и реестр синхронизации

> Дата актуализации: 2026-08-11.
>
> База: текущая `main` (HEAD `70c0493`) после закрытия RH-001 (25 тестов helper-а, проверено) и отражения fault harness в `TESTS_PLAN.md`.
>
> Этот файл заменяет аудит, привязанный к `dfe8f87`.

## 1. Что уже исправлено

### Закрыто

- DOC-003: README больше не обещает encrypted ZIP member любого размера.
- DOC-013: `TESTS_PLAN.md` отражает security/fault harness (подраздел «Fault-injection harness (test-only)», 2026-08-11).
- DOC-019: Android/iOS не объявляются проверенно поддерживаемыми.

### Частично закрыто

- DOC-004: root/core README различают output streaming и bounded memory.
- DOC-006: root README различает historical GOST decoder stage и более поздний writer.
- DOC-009: root README явно говорит об отсутствии archive authentication tag в GOST v1.

## 2. Что остаётся открытым

| ID | Приоритет | Состояние |
|---|---:|---|
| DOC-001 | Critical | SDL/status обещают атомарность до production fix |
| DOC-002 | High | link/junction/TOCTOU caveat недостаточен |
| DOC-004 | High | старые broad streaming claims остаются вне README |
| DOC-005 | High | AES writer status расходится между docs |
| DOC-006 | Medium | decoder-only формулировки остаются в architecture/stage |
| DOC-007 | High | mobile readiness преувеличена |
| DOC-008 | Medium | encrypted/encoded-header matrix не подтверждена end-to-end |
| DOC-009 | High | GOST primitive vs archive auth нужно синхронизировать |
| DOC-010 | High | совет повышать KDF limit без budget опасен |
| DOC-011 | Medium | writer status исторический, но назван current/authoritative |
| DOC-012 | Medium | architecture encoder sections устарели |
| DOC-014 | Medium | notices не отражают encoder/ZIP scope |
| DOC-015 | Medium | release tag signing statements противоречат |
| DOC-016 | Low | ручные counts тестов |
| DOC-017 | Medium | XML/comments отстают от кода |
| DOC-018 | Low/Medium | history и current docs смешаны |
| DOC-020 | Medium | нет единой нормативной capability matrix |

## 3. Новый статус test harness

Документация тестовой инфраструктуры должна отражать:

- `ThrowBeforeCrossingWriteStream` — test-only;
- моделирует отказ до пересекающего `Write`;
- не моделирует physical partial write внутри одного вызова;
- имеет sticky injected failure;
- покрыт 25 тестами sync/async сценариев (проверено на HEAD `70c0493`);
- поведение после `Dispose` зафиксировано тестом: `Write` после `Dispose` бросает `ObjectDisposedException`;
- не означает, что transactional creation уже реализован.

Не писать «SEC-002 закрыт» до production regression и staged destination.

## 4. Новый приоритет документации

Документы обновляются вместе с production behavior.

### До SEC-002 fix

Разрешено:

- known limitation;
- test harness status;
- planned semantics.

Запрещено:

- заявлять atomic create;
- заявлять preservation existing archive;
- заявлять cleanup временных файлов как гарантию.

### В PR SEC-002

Обновить:

- `docs/SECURITY.md` или runtime section;
- `docs/CAPABILITIES.md`;
- XML comments target API;
- regression test IDs.

## 5. Нормативная иерархия

Целевая структура:

1. `docs/CAPABILITIES.md` — current factual matrix.
2. `docs/SECURITY.md` — threat model, runtime guarantees, known gaps.
3. `docs/architecture/*` — стабильные boundaries.
4. `docs/COMPATIBILITY.md`.
5. `docs/history/*` — snapshots.
6. XML docs — локальный API contract.

Противоречие разрешается в пользу:

```text
current code + blocking test on pinned commit
```

После чего documentation defect исправляется в том же PR.

## 6. Порядок обновления маленькими шагами

### D1. CAPABILITIES skeleton

- создать только заголовки/статусы;
- не заполнять все codec combinations;
- build/link check.

### D2. ZIP read matrix

- один container/operation;
- source tests;
- ограничения memory.

### D3. 7z read matrix

### D4. Writers

- один method family за шаг.

### D5. SECURITY split

- runtime guarantees;
- SDL process;
- known limitations.

### D6. History governance

- front matter одного stage file;
- перенести один file;
- проверить links;
- следующий file.

### D7. XML comments

- только вместе с соответствующим code/behavior PR;
- либо отдельный batch по 1–3 файлам после code audit.

## 7. Documentation lint

Проверять фразы:

```text
любого размера
работает везде
полностью атомарно
без риска
не держит файл в памяти
поддерживает > 2 ГиБ
```

Они допустимы только с точным scope и test evidence.

## 8. Следующий документационный batch

После code warnings/cleanup:

1. Read-only audit XML warnings.
2. Категория docs-only: missing XML docs.
3. Один project, максимум 3 файла.
4. Не менять behavior.
5. Build затронутого project.
6. Не смешивать с using/casts/braces batch.

## 9. Definition of Done

- нет current docs с atomic claim без blocking tests;
- encrypted ZIP limitations точны;
- AES/GOST writer status одинаков в current docs;
- GOST v1 обозначен unauthenticated/experimental;
- desktop/mobile разделены;
- stage snapshots historical;
- test counts генерируются;
- CAPABILITIES — единственный current feature inventory;
- XML docs не противоречат matrix.
