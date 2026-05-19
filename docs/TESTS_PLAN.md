# План тестирования

## Текущий статус

Репозиторий давно вышел за пределы старого состояния с несколькими smoke-тестами.

Сейчас тесты покрывают основные зоны проекта:

- `Checksums`;
- `Lzma1`;
- `Lzma2`;
- `SevenZip`.

Зона `SevenZip` включает:

- тесты чтения структуры архива;
- тесты `StreamsInfo`;
- тесты `FilesInfo`;
- тесты `PackInfo`;
- тесты `SubStreamsInfo`;
- тесты CRC и метаданных;
- сценарии с пустыми потоками и подпотоками;
- сценарии с несколькими folder и pack stream;
- тесты encoded header;
- тесты безопасности распаковки;
- тесты `SevenZipFolderDecoder`;
- тесты `SevenZipArchiveDecoder`;
- интеграционные тесты цепочек coder;
- тесты на реальных `.7z`-архивах для нескольких методов и комбинаций фильтров;
- явные `NotSupported`-сценарии для архивов, связанных с AES, GOST и другими неподдерживаемыми возможностями.

Фреймворк тестирования: `xUnit`.

## Цель тестирования на этапе 1

К концу этапа 1 каждый поддерживаемый путь декодирования 7z должен быть закреплён тестами хотя бы на одном из уровней:

- unit-тест;
- интеграционный тест;
- тест на реальном архиве.

Эта цель фактически выполнена для основного поддерживаемого подмножества без AES.

Дальше новые синтетические тесты этапа 1 добавляются только при наличии конкретного риска, а не ради полного перебора всех искусственных комбинаций.

## Состояние покрытия SevenZipFolderDecoder

`SevenZipFolderDecoder` покрыт прямыми тестами по основным поддерживаемым веткам.

Покрыты одиночные coder-сценарии:

- `Copy`;
- `LZMA`;
- `LZMA2`;
- `Delta`;
- `Swap2`;
- `Swap4`;
- BCJ-фильтры;
- `BZip2`;
- `PPMd`;
- `Deflate`;
- `Deflate64`;
- неизвестные / неподдерживаемые coder-ы.

Покрыты BCJ2-сценарии:

- подготовка входных потоков;
- producer coder-ы;
- topology-проверки;
- size-limit проверки;
- malformed-stream проверки;
- выбор финального выхода;
- merge через `TryDecodeBcj2ToArray`;
- ветки `E8`, `E9`, `Jcc`;
- граничные случаи `disp32` и `rel32`.

Покрыты общие сценарии folder decoder:

- линейная topology folder-а;
- некорректные `BindPair`;
- некорректные размеры входных и выходных потоков;
- выбор `pack stream` для одного и нескольких folder-ов;
- проверки `PackInfo`;
- проверки `UnpackInfo`;
- проверки `FolderUnpackSizes`;
- проверки `PackSizes`;
- проверки `PackPos`;
- negative-path для недопустимых свойств coder-ов;
- negative-path для неконсистентных размеров.

## Области покрытия SevenZip, которые должны оставаться видимыми

В дереве тестов и дальше должно быть легко найти такие группы:

- корректность archive reader;
- обработка encoded header;
- поведение folder decoder;
- поведение archive decoder;
- безопасность распаковки;
- совместимость на реальных архивах;
- неподдерживаемые, но явно отслеживаемые сценарии.

Если новая группа тестов разрастается, лучше выделять её в отдельный файл с понятным именем, чем смешивать с уже существующими сценариями.

## Правила добавления тестов

- Одно маленькое изменение — один соответствующий маленький тест.
- По возможности предпочитать понятные синтетические фикстуры огромным непрозрачным бинарникам.
- Реальные `.7z`-файлы использовать там, где важна совместимость на уровне контейнера.
- Если имя теста перестало соответствовать коду, переименовывать его сразу.
- Не добавлять новые синтетические edge-case тесты без явной причины.

Новый тест должен закрывать хотя бы один из рисков:

- безопасность распаковки;
- crash / exception path;
- публичный контракт результата `Ok` / `InvalidData` / `NotSupported`;
- ветку, которую реально можно сломать при оптимизации или рефакторинге;
- поведение на реальном архиве.

## AES и зашифрованные архивы

AES не входил в этап 1 и был вынесен в отдельный этап 1.5.

На этапе 1.5 реализован и покрыт тестами decoder-path для основных AES-сценариев 7z.

Покрыты низкоуровневые компоненты:

- распознавание 7zAES method id;
- разбор AES properties;
- проверка поддерживаемых и неподдерживаемых `NumCyclesPower`;
- парольный материал UTF-16LE без BOM;
- direct key derivation для `NumCyclesPower == 0x3F`;
- SHA-256 key derivation для поддерживаемых `NumCyclesPower`;
- общий key derivation wrapper;
- построение IV из AES properties;
- AES-256-CBC decrypt helper;
- AES packed stream decrypt wrapper.

Покрыто прокидывание `SevenZipDecodeOptions` через основные API:

- `SevenZipFolderDecoder.DecodeFolderToArray`;
- `SevenZipArchiveReader.Read`;
- `SevenZipArchiveDecoder.DecodeToArray`;
- `SevenZipArchiveDecoder.DecodeToEntries`;
- `SevenZipArchiveDecoder.DecodeSingleFileToArray`;
- `SevenZipArchiveDecoder.ExtractToDirectory`.

Покрыты real-archive сценарии, созданные настоящим `7z`:

- `mhe=off`, single-file, `AES + Copy`;
- `mhe=off`, single-file, `AES + LZMA2`;
- `mhe=off`, multi-file, `AES + LZMA2`;
- `mhe=off`, solid multi-file, `AES + LZMA2`;
- `mhe=on`, single-file, `AES + LZMA2`;
- `mhe=on`, multi-file, `AES + LZMA2`;
- `mhe=on`, solid multi-file, `AES + LZMA2`.

Для AES-сценариев должны оставаться обязательными проверки:

- успешное чтение с правильным паролем;
- `NotSupported` без пароля;
- `InvalidData` при неверном пароле;
- отсутствие файловой записи при ошибке;
- сохранение поведения обычных нешифрованных архивов;
- `ExtractToDirectory` для поддерживаемых real-archive сценариев.

Новые AES-тесты дальше добавляются только если они закрывают конкретный риск:

- новый real-archive сценарий;
- новый topology-сценарий folder-а;
- crash / exception path;
- повреждённые encrypted данные;
- ошибка пароля;
- безопасность распаковки на диск;
- публичный контракт `Ok` / `InvalidData` / `NotSupported`.

Не нужно добавлять новые synthetic AES edge-case тесты только ради полного перебора комбинаций.

## Итог этапа 1.5

Этап 1.5 закрыт после полного зелёного прогона тестов и сверки документации.

Зафиксировано:

- AES decoder-path покрыт unit-тестами, synthetic archive-level тестами и real-archive тестами;
- real-archive генераторы добавлены в `tools/TestArchiveGenerators`;
- новые `.7z`-архивы добавлены в `TestData/Real`;
- ошибки AES не создают файлов и директорий при `ExtractToDirectory`;
- `README.md`, `ROADMAP.md`, `ARCHITECTURE.md`, `STAGE15_AES_STATUS.md` и этот файл согласованы по AES-контракту.

## Тесты GOST

GOST-сценарии тестируются synthetic-архивами.

Реальные архивы через стандартный 7-Zip для GOST не используются, потому что GOST-поддержка является экспериментальным расширением LzmaSharp, использует private method id и не совместима со стандартным 7-Zip.

Покрытые сценарии:

- Kuznyechik success;
- Kuznyechik + Copy;
- Kuznyechik + LZMA2;
- Kuznyechik single coder;
- GOST encrypted header;
- GOST encrypted header + encrypted file;
- отсутствие пароля;
- неверный пароль;
- invalid properties;
- unsupported KDF;
- Magma как `NotSupported`.

Контракт этапа 1.6:

- поддерживается только Kuznyechik CTR direct-key;
- Magma decrypt не реализуется в рамках этапа 1.6;
- полноценный KDF через Стрибог не реализуется в рамках этапа 1.6;
- остальные GOST-сценарии относятся к позднему развитию после завершения основных этапов проекта.

## Тестирование этапа 2

Для этапа 2 заведён отдельный контур тестирования encoder / writer-направления.

Первый реализованный writer API:

- `SevenZipArchiveWriter.BuildArchive(...)`;
- `SevenZipArchiveWriterEntry`;
- `SevenZipArchiveWriteResult`.

Сейчас writer-тесты покрывают минимальные сценарии:

- пустой архив;
- архив с одним пустым файлом;
- архив с несколькими пустыми файлами;
- архив с одной пустой директорией;
- архив со смесью пустых файлов и пустых директорий;
- архив с одним непустым файлом через `Copy`;
- архив с несколькими непустыми файлами через `Copy`;
- архив со смесью empty entries и непустых файлов через `Copy`;
- вложенный пустой файл;
- вложенный непустой файл через `Copy`;
- явная пустая директория и файл внутри неё.

Промежуточный итог покрытия writer-а:

- writer покрывает простые валидные entry, включая безопасные вложенные `/`-paths;
- empty-entry path покрывает пустые файлы и пустые директории;
- `Copy` path покрывает один или несколько непустых файлов;
- mixed Copy path покрывает архивы, где empty entries и непустые `Copy`-файлы находятся вместе;
- `FilesInfo` описывает полный набор entry;
- packed data, `PackInfo` и `UnpackInfo` формируются только для непустых файлов;
- вложенные path сохраняются в `FilesInfo.Names`.

Покрытые проверки:

- round-trip пустого архива через существующий decoder-path;
- round-trip одного пустого файла через существующий decoder-path;
- round-trip нескольких пустых файлов через существующий decoder-path;
- round-trip одной пустой директории через существующий decoder-path;
- round-trip смеси пустого файла и пустой директории через существующий decoder-path;
- round-trip одного непустого `Copy`-файла через существующий decoder-path;
- round-trip нескольких непустых `Copy`-файлов через существующий decoder-path;
- структурная проверка `Copy` writer-архива через `SevenZipArchiveReader`;
- структурная проверка multi-Copy writer-архива через `SevenZipArchiveReader`;
- структурная проверка `FilesInfo` для empty entries;
- корректность `SignatureHeader`;
- корректность packed data;
- корректность `PackInfo`;
- корректность нескольких packed stream-ов в `PackInfo`;
- корректность `UnpackInfo`;
- корректность нескольких folder-ов в `UnpackInfo`;
- корректность `FilesInfo`;
- корректность `Copy` coder-а;
- корректность отдельного `Copy` coder-а для каждого folder-а в multi-Copy;
- корректность `EmptyStream`;
- корректность `EmptyFile`;
- корректность списка имён пустых файлов и пустых директорий;
- граничная проверка bit-vector для 9 empty entries;
- проверка второго байта `EmptyStream` bit-vector;
- проверка второго байта `EmptyFile` bit-vector;
- CRC packed stream-а;
- CRC folder stream-а;
- CRC файла;
- CRC нескольких packed stream-ов, folder stream-ов и файлов;
- повреждение packed data возвращает `InvalidData`;
- повреждение файлового CRC в header возвращает `InvalidData`;
- `null`-входные данные возвращают `InvalidData`;
- директория с данными возвращает `InvalidData`;
- некорректные имена entry возвращают `InvalidData`;
- whitespace-only имена entry возвращают `InvalidData`;
- имена entry с недопустимыми Windows-символами возвращают `InvalidData`;
- имена entry с управляющими символами возвращают `InvalidData`;
- зарезервированные Windows-имена entry возвращают `InvalidData`;
- зарезервированные Windows-имена entry с расширением возвращают `InvalidData`;
- имена entry с завершающей точкой или завершающим пробельным символом возвращают `InvalidData`;
- директории с некорректными именами возвращают `InvalidData`;
- дублирующиеся имена entry возвращают `InvalidData`;
- имена entry, отличающиеся только регистром, возвращают `InvalidData`;
- файл и директория с одинаковым или регистронезависимо совпадающим именем возвращают `InvalidData`;
- допустимые имена с пробелом, точкой или безопасными символами внутри разрешены;
- похожие, но не зарезервированные Windows-имена entry разрешены;
- round-trip смешанного сценария с пустым файлом и непустым `Copy`-файлом через существующий decoder-path;
- round-trip смешанного сценария с пустой директорией и непустым `Copy`-файлом через существующий decoder-path;
- round-trip смешанного сценария с несколькими empty entries и несколькими `Copy`-файлами через существующий decoder-path;
- структурная проверка mixed Copy writer-архива через `SevenZipArchiveReader`;
- корректность `EmptyStream` bit-vector для mixed-сценария;
- корректность `EmptyFile` sub-vector для mixed-сценария;
- корректность `FilesInfo.Crc` defined bit-vector для mixed-сценария;
- CRC в mixed-сценарии задаётся только для непустых файлов;
- граничная проверка bit-vector для mixed Copy на 12 entry;
- проверка второго байта `EmptyStream` bit-vector для mixed Copy;
- проверка второго байта `EmptyFile` sub-vector для mixed Copy;
- проверка второго байта `FilesInfo.Crc` defined bit-vector для mixed Copy;
- проверка, что `FilesInfo.Crc` defined bit-vector в mixed Copy отмечает только непустые файлы;
- round-trip вложенного пустого файла через существующий decoder-path;
- round-trip вложенного непустого `Copy`-файла через существующий decoder-path;
- round-trip явной директории и файла внутри неё через существующий decoder-path;
- структурная проверка nested path writer-а через `SevenZipArchiveReader`;
- сохранение `/` в `FilesInfo.Names`;
- корректность `EmptyStream`, `EmptyFile` и `FilesInfo.Crc` для вложенных entry;
- absolute path возвращает `InvalidData`;
- path с завершающим `/` возвращает `InvalidData`;
- path с пустым сегментом возвращает `InvalidData`;
- path с `.` или `..` сегментом возвращает `InvalidData`;
- path с `\` возвращает `InvalidData`;
- path с зарезервированным Windows-сегментом возвращает `InvalidData`;
- parent-file conflict возвращает `InvalidData`.

Дальнейшие writer-тесты должны добавляться только под конкретный реализуемый сценарий.

Минимальный набор дальнейших направлений:

- file attributes;
- timestamp-метаданные;
- LZMA writer;
- LZMA2 writer;
- round-trip внутри собственных компонентов проекта;
- сравнение структуры записанных 7z-архивов с ожидаемой;
- совместимость с эталонными инструментами там, где это применимо;
- профилирование размера и скорости только после функциональной стабилизации.

Для writer-а не нужно заранее добавлять synthetic edge-case тесты без реализуемого сценария.

Шифрование не входит в базовый writer-контур этапа 2:

- AES writer рассматривается только после стабилизации обычного writer-а;
- GOST writer не входит в текущий этап;
- Magma и полноценный GOST KDF через Стрибог остаются поздними направлениями.

## После появления собственных codec-реализаций

Когда временные внешние зависимости начнут заменяться собственными реализациями, для каждого codec-а нужна отдельная тестовая группа.

В первую очередь это касается:

- `PPMd`;
- `BZip2`;
- `Deflate`;
- `Deflate64`;
- возможных ZIP-совместимых сценариев.

Для каждого такого codec-а нужны:

- тесты на эталонных данных;
- roundtrip-тесты, если появляется encoder;
- тесты совместимости с реальными архивами;
- negative-тесты на повреждённый поток;
- тесты на граничные размеры;
- проверка, что внешний контракт `SevenZipFolderDecoder` не меняется при замене реализации.

