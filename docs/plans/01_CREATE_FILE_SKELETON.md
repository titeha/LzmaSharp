Выполни только создание каркаса нового файла.

Не реализуй business logic.
Не создавай тесты.
Не меняй другие файлы.
Не запускай полный suite.

Разрешённый файл:
<PATH>

Требования к каркасу:
- namespace: <NAMESPACE>
- type: <TYPE>
- constructor/fields/properties: <LIST>
- required overrides/interfaces: <LIST>
- функциональный метод временно бросает:
  `NotImplementedException("STEP_2")`

После сохранения:
- `git diff --check`
- build затронутого project

Верни:
- файл;
- полный каркас;
- build result;
- diff name list.

Заверши DONE или BLOCKED.
