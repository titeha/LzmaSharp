# Статус этапа 1.6 — экспериментальная GOST-ветка

Этап 1.6 завершён.

На этапе добавлен decoder-path для экспериментальных GOST coder-ов LzmaSharp внутри 7z-контейнера.

## Границы этапа

GOST-поддержка является внутренним экспериментальным расширением LzmaSharp.

Она не совместима со стандартным 7-Zip и использует закрытые private method id проекта.

В этап входит только чтение и декодирование архивов.

В этап не входит:

- запись GOST-архивов;
- совместимость со стандартным 7-Zip;
- Magma decrypt;
- полноценный GOST KDF через Стрибог;
- UI для ввода пароля;
- хранение паролей;
- streaming decrypt API.

## Реализовано

- распознавание экспериментальных GOST method id;
- отдельные private method id для Kuznyechik и Magma;
- разбор GOST properties версии 1;
- проверка версии properties;
- проверка flags;
- проверка размеров salt и IV;
- direct-key KDF через `NumCyclesPower == 0x3F`;
- построение IV для Kuznyechik CTR;
- Kuznyechik block cipher;
- Kuznyechik CTR transform;
- расшифровка packed stream через Kuznyechik CTR;
- подключение GOST decrypt в `SevenZipFolderDecoder`;
- поддержка GOST на archive-level API;
- поддержка GOST encrypted header в decoder-path.

## Зафиксированные ограничения

### Kuznyechik

Поддержан только сценарий:

- Kuznyechik;
- CTR;
- IV размером 8 байт;
- direct-key режим;
- decoder-only.

### Magma

Magma method id распознаётся, но сам decrypt не реализован.

Результат для Magma:

- `NotSupported`.

### KDF

Поддержан только direct-key режим.

Любой другой `NumCyclesPower` сейчас возвращает:

- `NotSupported`.

Полноценная парольная функция формирования ключа через Стрибог не реализована.

## Контракт ошибок

Для GOST-сценариев используется общий pattern result enum:

- `Ok` — данные успешно расшифрованы и декодированы;
- `InvalidData` — некорректные properties, IV, method id или результат расшифровки;
- `NotSupported` — сценарий распознан, но не входит в текущую поддержку.

Отсутствующий пароль для GOST-сценария возвращает `NotSupported`.

Неверный пароль фиксируется как `InvalidData`.

## Тестовое покрытие

Сценарии GOST покрыты synthetic-тестами.

Покрыто:

- Kuznyechik success;
- Kuznyechik без пароля;
- Kuznyechik с неверным паролем;
- Kuznyechik + Copy;
- Kuznyechik + LZMA2;
- Kuznyechik single coder;
- GOST encrypted header;
- GOST encrypted header + encrypted file;
- Magma как `NotSupported`;
- unsupported KDF как `NotSupported`;
- invalid properties как `InvalidData`.

## Итог

Этап 1.6 закрывает экспериментальный decoder-only путь для GOST Kuznyechik CTR direct-key.

Дальнейшее развитие GOST возможно только отдельными этапами и не является частью текущего завершённого контракта.
