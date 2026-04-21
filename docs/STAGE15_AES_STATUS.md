# Состояние этапа 1.5: AES

## Текущий статус

Этап 1.5 находится в активной разработке на ветке `stage15-aes`.

Цель этапа 1.5 — добавить поддержку AES-сценариев 7z поверх уже закрытого этапа 1, не ломая существующий путь декодирования и распаковки нешифрованных архивов.

На текущий момент реализован и покрыт тестами базовый путь чтения AES-архивов:

- AES coder распознаётся явно;
- свойства 7zAES разбираются отдельно;
- парольный материал хранится как UTF-16LE без BOM;
- реализован direct key derivation для `NumCyclesPower == 0x3F`;
- реализован обычный SHA-256 derivation для поддерживаемых `NumCyclesPower`;
- реализовано построение полного 16-байтового IV из AES properties;
- реализована AES-256-CBC расшифровка без padding;
- реализован wrapper расшифровки packed stream;
- `SevenZipDecodeOptions` протянут через основные decode/extract API;
- AES подключён в `SevenZipFolderDecoder`;
- encrypted header с AES читается через `SevenZipArchiveReader` и верхние decoder API.

## Что уже покрыто тестами

### Низкоуровневые AES-компоненты

Покрыты:

- распознавание 7zAES method id;
- разбор AES properties;
- поддерживаемые и неподдерживаемые значения `NumCyclesPower`;
- парольный материал;
- direct key derivation;
- SHA-256 key derivation;
- общий key derivation wrapper;
- построение IV;
- AES-CBC decrypt helper;
- AES packed stream decrypt wrapper.

### Прокидывание настроек

Покрыты новые options-перегрузки:

- `SevenZipFolderDecoder.DecodeFolderToArray`;
- `SevenZipArchiveDecoder.DecodeToArray`;
- `SevenZipArchiveDecoder.DecodeToEntries`;
- `SevenZipArchiveDecoder.DecodeSingleFileToArray`;
- `SevenZipArchiveDecoder.ExtractToDirectory`;
- `SevenZipArchiveReader.Read`.

Старые перегрузки сохранены и продолжают использовать поведение по умолчанию.

### Реальные AES-архивы

Покрыты real-archive сценарии:

- `mhe=off`, single-file, `AES + Copy`;
- `mhe=off`, single-file, `AES + LZMA2`;
- `mhe=off`, multi-file, `AES + LZMA2`;
- `mhe=off`, solid multi-file, `AES + LZMA2`;
- `mhe=on`, single-file, `AES + LZMA2`;
- `mhe=on`, multi-file, `AES + LZMA2`;
- `mhe=on`, solid multi-file, `AES + LZMA2`.

Для этих сценариев проверяются:

- успешное декодирование с правильным паролем;
- `NotSupported` без пароля;
- `InvalidData` при неверном пароле;
- отсутствие записи на диск при ошибке;
- `ExtractToDirectory` для поддерживаемых real-archive сценариев.

## Текущие границы реализации

Поддержка AES сейчас ориентирована на чтение и распаковку архивов.

В текущую реализацию не входит:

- запись AES-архивов;
- UI для ввода пароля;
- хранение паролей;
- интеграция с системными хранилищами секретов;
- streaming decrypt API;
- оптимизация derivation для больших `NumCyclesPower`;
- собственная реализация AES;
- проверка всех возможных вариантов AES properties из внешних архиваторов.

## Правило дальнейших изменений

Новые AES-тесты добавляются только если они закрывают конкретный риск:

- реальный архив из 7-Zip или совместимого архиватора;
- новый topology-сценарий folder-а;
- crash / exception path;
- ошибка пароля;
- повреждённые encrypted данные;
- безопасность распаковки на диск;
- публичный контракт `Ok` / `InvalidData` / `NotSupported`.

Не нужно добавлять новые synthetic AES edge-case тесты только ради полного перебора комбинаций.

## Оставшаяся работа этапа 1.5

Перед закрытием этапа 1.5 нужно:

1. Прогнать полный набор тестов.
2. Проверить, что все новые AES-архивы и LinqPad-генераторы добавлены в репозиторий.
3. Проверить, что документация описывает AES как реализованный decoder-path, а не только как будущий план.
4. Сверить поведение без пароля и с неверным паролем.
5. Проверить, что ошибки AES не создают файлов и директорий при `ExtractToDirectory`.
6. Сделать финальный обзор ветки `stage15-aes`.
7. После этого решить, можно ли мержить этап 1.5 в `main`.

## Следующие возможные шаги

Ближайшие безопасные шаги:

1. Свести список AES real-archive тестов и генераторов в документации.
2. Добавить короткий раздел в `README.md` о текущей поддержке AES.
3. Обновить `ROADMAP.md`, `TESTS_PLAN.md` и `ARCHITECTURE.md` после финальной стабилизации ветки.
4. Сделать полный прогон тестов.
5. Запушить ветку `stage15-aes` и сверить remote.
6. Подготовить финальный коммит завершения этапа 1.5.

## Дальше после AES

После закрытия этапа 1.5 возможны два крупных направления:

- этап 2: writer / encoder;
- этап 1.6: экспериментальные ГОСТ-crypto расширения 7z.

ГОСТ-crypto следует делать только после закрытия совместимого AES decoder-path.
