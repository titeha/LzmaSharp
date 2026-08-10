Добавь тесты только для одной уже реализованной функции.

Разрешённый test file:
<PATH>

Не изменяй production-файл.

Обязательные тесты:
- positive;
- exact boundary;
- one negative/failure;
- side-effect invariant.

Не добавляй тесты других overloads.

После изменения:
- `git diff --check`;
- build test project;
- targeted test filter:
  <COMMAND>

Не запускай полный suite.

Заверши DONE или BLOCKED.
