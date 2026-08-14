# LzmaSharp: актуальный план разделения compression, cryptography, containers и ArchiveIO

> Статус: целевая архитектура, реализация ещё не начата.
>
> Дата актуализации: 2026-07-30.
>
> Предусловие: не начинать физическое перемещение кода до стабилизации transactional I/O tests, resource budget и characterization fixtures.

## 1. Главный инвариант

Кодеки сжатия и криптография должны жить отдельно и не ссылаться друг на друга.

```text
Compression  ─X─> Cryptography
Cryptography ─X─> Compression
Compression  ─X─> UI / filesystem
Cryptography ─X─> UI / filesystem
```

Совместное использование выполняют:

- 7z/ZIP container orchestration;
- composition root;
- ArchiveIO transaction layer.

## 2. Почему разделение отложено

На текущем этапе в проекте ещё открыты:

- прямое создание final archive;
- небезопасный overwrite extraction;
- resource limits;
- checked metadata arithmetic;
- streaming encrypted ZIP;
- GOST authentication semantics.

Если одновременно исправлять безопасность и перемещать сотни типов, будет трудно доказать, что поведение сохранилось.

Поэтому порядок:

```text
security characterization
→ transactional I/O
→ budgets/errors
→ internal abstractions
→ package extraction
```

## 3. Целевая структура

```text
src/
  LzmaSharp.Primitives/
  LzmaSharp.Pipeline.Abstractions/

  LzmaSharp.Compression.Abstractions/
  LzmaSharp.Compression/
  LzmaSharp.Compression.Filters/

  LzmaSharp.Cryptography.Abstractions/
  LzmaSharp.Cryptography.Standard/
  LzmaSharp.Cryptography.Gost.Experimental/

  LzmaSharp.Containers.Abstractions/
  LzmaSharp.Containers.SevenZip/
  LzmaSharp.Containers.Zip/

  LzmaSharp.ArchiveIO/
  LzmaSharp.Compatibility/
```

На первом этапе допустимо меньше проектов:

```text
LzmaSharp.Abstractions
LzmaSharp.Compression
LzmaSharp.Cryptography
LzmaSharp.Containers
LzmaSharp.ArchiveIO
LzmaSharp.Compatibility
```

Но `Compression` и `Cryptography` нельзя объединять.

## 4. Границы ответственности

### Compression

- LZMA/LZMA2;
- PPMd;
- Deflate/Deflate64;
- BZip2;
- Copy;
- match finders/range coders;
- без method IDs 7z/ZIP;
- без файловой системы;
- без AES/GOST.

### Filters

- BCJ variants;
- BCJ2;
- Delta;
- Swap;
- reversible byte transforms.

### Cryptography

- AES transforms;
- KDF primitives;
- HMAC/tag helpers;
- secret leases и zeroization;
- без 7z/ZIP headers;
- без destination paths.

### GOST Experimental

- отдельный package;
- opt-in;
- legacy v1 read/write policy отдельно;
- не попадает в стандартный meta-package;
- новый authenticated v2 — отдельное утверждённое задание.

### Containers

- wire-format;
- method IDs;
- properties;
- coder graph;
- ZIP extra fields;
- CRC semantics;
- mapping container → normalized algorithm IDs;
- не публикуют пользовательские файлы.

### ArchiveIO

Единственный слой для:

- paths;
- staging;
- commit/rollback;
- overwrite;
- backups;
- symlink/reparse policy;
- temp budget;
- provider/path adapters.

## 5. Работа маленькими шагами

Разделение выполняется только после security baseline.

### Фаза A. Architecture tests без перемещений

A1. Каркас test project/fixture.
A2. Один тест запрещённой зависимости.
A3. Тест текущего монолита с allowlist.
A4. Public API snapshot.
A5. Fixture inventory.

### Фаза B. Internal abstractions в текущем assembly

B1. Один strongly typed algorithm ID.
B2. Один internal codec interface.
B3. Adapter для одного codec.
B4. Один call site container перевести на adapter.
B5. Тест поведения.
B6. Повторить по одному codec.

Не создавать все интерфейсы заранее.

### Фаза C. Вынесение Compression

C1. Создать пустой проект-каркас.
C2. Перенести один leaf type без изменений.
C3. Сборка.
C4. Перенести один codec internal cluster.
C5. Тесты codec.
C6. Container adapter.
C7. Следующий codec.

Один commit не должен переносить все codec families.

### Фаза D. Вынесение Cryptography

D1. Abstractions.
D2. Secret handling.
D3. AES primitive.
D4. KDF.
D5. 7z integration adapter.
D6. ZIP integration adapter.
D7. GOST project skeleton.
D8. GOST opt-in registration.

### Фаза E. Containers

E1. Neutral entry contracts.
E2. ZIP parser path.
E3. ZIP writer path.
E4. 7z parser.
E5. 7z writer.
E6. registries.
E7. unknown-provider errors.

### Фаза F. ArchiveIO и Compatibility

F1. transaction facade;
F2. path adapter;
F3. stream/storage adapter;
F4. compatibility facade;
F5. package smoke tests.

## 6. Правило нового проекта

Каждый новый `.csproj` создаётся отдельно:

1. пустой project skeleton;
2. solution reference;
3. build;
4. один type;
5. test project skeleton;
6. один test;
7. первый dependency;
8. architecture test.

Нельзя в одном шаге создать 8 проектов и сразу переместить код.

## 7. Обязательные architecture gates

- Compression не ссылается на Crypto/Containers/ArchiveIO/Avalonia.
- Crypto не ссылается на Compression/Containers/ArchiveIO/Avalonia.
- Primitives не ссылается на верхние слои.
- Containers не ссылаются на приложения.
- GOST не является транзитивной стандартной зависимостью.
- Algorithm packages не используют `File`, `Directory`, `FileStream`.
- Reflection scanning не используется для регистрации.
- Public API drift проходит отдельное согласование.

## 8. Definition of Done

Разделение завершено, когда:

- compression package работает без crypto assemblies;
- crypto package тестируется без compression/container;
- container строит pipeline только через abstractions;
- filesystem отсутствует в algorithm/container parsing layers;
- compatibility package сохраняет текущие сценарии;
- experimental GOST подключается отдельно;
- architecture tests блокируют запрещённые рёбра;
- security regression и interoperability tests остаются зелёными.
