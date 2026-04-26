# Состояние этапа 1.5: AES

## Текущий статус

Этап 1.5 завершён на ветке `stage15-aes`.

Цель этапа 1.5 выполнена: добавлен decoder-path для AES-сценариев 7z поверх уже закрытого этапа 1, без поломки существующего пути декодирования и распаковки нешифрованных архивов.

Реализован и покрыт тестами базовый путь чтения AES-архивов:

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

## Итог этапа 1.5

Этап 1.5 закрыт после финальной сверки ветки `stage15-aes`.

Зафиксировано:

- AES decoder-path реализован для поддерживаемых сценариев чтения 7z;
- encrypted packed streams поддержаны;
- encrypted header поддержан для покрытых real-archive сценариев;
- пароль передаётся через `SevenZipDecodeOptions`;
- старые API-перегрузки сохранены и продолжают использовать поведение по умолчанию;
- отсутствие пароля возвращает согласованный `NotSupported`;
- неверный пароль возвращает согласованный `InvalidData`;
- ошибки AES не создают файлов и директорий при `ExtractToDirectory`;
- real-archive тесты покрывают `mhe=off` и `mhe=on`;
- real-archive тесты покрывают single-file, multi-file и solid multi-file сценарии;
- LinqPad-генераторы AES-архивов добавлены в репозиторий;
- полный набор тестов проходит.

Оставшиеся ограничения осознанно перенесены в будущие этапы:

- запись AES-архивов;
- UI для ввода пароля;
- хранение паролей;
- интеграция с системными хранилищами секретов;
- streaming decrypt API;
- оптимизация derivation для больших `NumCyclesPower`;
- собственная реализация AES.

## Следующие возможные шаги

После закрытия этапа 1.5 ближайшие безопасные направления:

1. Смержить `stage15-aes` в основную ветку после финальной проверки.
2. Поставить тег завершения этапа 1.5, если нужен отдельный ориентир в истории.
3. Перейти к этапу 2: writer / encoder.
4. Либо завести отдельную ветку этапа 1.6 для экспериментальных ГОСТ-crypto расширений 7z.

## Дальше после AES

После закрытия этапа 1.5 возможны два крупных направления:

- этап 2: writer / encoder;
- этап 1.6: экспериментальные ГОСТ-crypto расширения 7z.

ГОСТ-crypto следует делать только после закрытия совместимого AES decoder-path.
