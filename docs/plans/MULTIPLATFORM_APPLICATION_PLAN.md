# LzmaSharp: актуальный план мультиплатформенного приложения

> Статус: архитектурный план; mobile heads ещё не реализованы.
>
> Дата актуализации: 2026-07-30.
>
> Предусловие: сначала transactional ArchiveIO и stream-first application contracts.

## 1. Целевая модель

Не один исполняемый файл, а:

- общий application core;
- общие use cases/ViewModel;
- общая часть Avalonia views/styles;
- один Desktop head для Windows/Linux/macOS;
- отдельный Android head;
- отдельный iOS head.

```text
App.Core
   ↑
App.Avalonia
  ↑    ↑    ↑
Desktop Android iOS
```

## 2. Текущее ограничение

Текущий UI остаётся desktop/path-oriented:

- classic desktop lifetime;
- desktop `MainWindow`;
- path-based services;
- filesystem tree;
- raw `File`/`Directory`/`Path` assumptions.

Pure managed core повышает переносимость, но не доказывает:

- Android build/runtime;
- iOS AOT/trimming;
- content URI;
- security-scoped document access;
- mobile memory profile.

## 3. Главный prerequisite

Application API должен стать stream/storage-provider first.

```csharp
public interface IAppReadableFile
{
    string DisplayName { get; }
    long? DeclaredLength { get; }
    ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken);
}
```

Raw path остаётся desktop optimization, но не обязательным контрактом ViewModel.

## 4. Целевая структура

```text
src/
  LzmaSharp.App.Core/
  LzmaSharp.App.Avalonia/
  LzmaSharp.App.Desktop/
  LzmaSharp.App.Android/
  LzmaSharp.App.iOS/
```

Отдельные `Platform.*` projects добавляются только если конкретный head становится перегружен. Не создавать их заранее.

## 5. Миграция маленькими шагами

### Фаза 0. Baseline

0.1. Characterization одного desktop use case.
0.2. Один ViewModel test.
0.3. Один integration fixture.
0.4. Список path-based dependencies.

### Фаза 1. Storage contracts

1.1. Каркас `IAppReadableFile`.
1.2. Fake implementation + test.
1.3. Один desktop adapter.
1.4. Перевести один open/list use case.
1.5. Не менять create/extract одновременно.

1.6. `IAppWritableFile`.
1.7. Fake + tests.
1.8. Один create use case.

1.9. `IAppDirectory`.
1.10. Один extract use case.

### Фаза 2. Seekable materializer

2.1. Interface skeleton.
2.2. Already-seekable path.
2.3. Test.
2.4. Temp-file materialization.
2.5. Budget.
2.6. Cancellation cleanup.
2.7. Non-seekable test.

### Фаза 3. App.Core extraction

3.1. Пустой project skeleton.
3.2. Один neutral state type.
3.3. Один use case.
3.4. Один ViewModel segment.
3.5. Tests.
3.6. Следующий segment.

Не переносить `MainViewModel` целиком одним commit.

### Фаза 4. Shared Avalonia

4.1. Project skeleton.
4.2. Один reusable control.
4.3. Desktop host использует control.
4.4. Следующий view.
4.5. Mobile shell skeleton только после shared content.

### Фаза 5. Desktop head

5.1. Новый Desktop project skeleton.
5.2. Program/lifetime.
5.3. MainWindow.
5.4. Services.
5.5. Windows smoke.
5.6. Linux.
5.7. macOS.

### Фаза 6. Android

6.1. Template project skeleton.
6.2. Build only.
6.3. Single-view shell.
6.4. Picker adapter read-only.
6.5. Open/list small archive.
6.6. create to app-private staging.
6.7. export.
6.8. extract.
6.9. lifecycle.
6.10. memory profile.

### Фаза 7. iOS

Повторяет Android, отдельно:

- security-scoped access;
- AOT/trimming;
- signing;
- suspension/cleanup.

## 6. Правило «не ломаем Desktop»

Каждый PR должен:

- сохранять текущую Desktop сборку;
- сохранять текущие desktop tests;
- добавлять adapter, а не сразу удалять старый path API;
- переносить один use case;
- удалить старый путь только после полного переключения и тестов.

## 7. Платформенные публикации

Desktop:

```text
Windows / Linux / macOS
→ один head
→ разные RID/package/signing
```

Mobile:

```text
Android / iOS
→ отдельные heads
→ document provider streams
→ app-private staging
→ best-effort export, если provider не даёт atomic replace
```

Нельзя обещать whole-tree atomicity мобильного provider без capability.

## 8. Resource profiles

- TrustedDesktop;
- UntrustedDesktop;
- Mobile;
- ServerRestricted.

Mobile profile вводится только после общего `ArchiveResourceBudget`.

## 9. CI gates

Поэтапно:

1. desktop build current;
2. Windows/Linux/macOS matrix;
3. Android build;
4. Android emulator smoke;
5. iOS simulator build;
6. AOT/trimming;
7. device/release workflows.

Успешный build не равен runtime support.

## 10. Definition of Done

- общий App.Core;
- shared ViewModel без обязательных raw paths;
- общий App.Avalonia;
- Desktop head для трёх desktop OS;
- отдельные Android/iOS heads;
- provider streams;
- bounded private staging;
- одинаковые error/resource contracts;
- desktop regression не сломан;
- Android/iOS runtime + lifecycle + AOT/trimming проверены.
