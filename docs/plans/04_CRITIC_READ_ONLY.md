Выполни read-only critic review текущего микрошагa.

Не изменяй файлы.
Не создавай commit.
Не запускай subagents.
Не исследуй весь репозиторий.

Цель шага:
тест Dispose_WithLeaveOpenFalse_ClosesInner: при leaveOpen=false inner закрыт после Dispose wrapper'а

Разрешённые файлы:
tests/Lzma.Core.Tests/Helpers/ThrowBeforeCrossingWriteStream.Tests.cs

Проверь:
1. Diff строго в allowlist.
2. Реализован только заявленный контракт.
3. Нет off-by-one/overflow/side-effect ошибок.
4. Тест действительно вызывает нужную функцию/overload.
5. Тест упал бы при очевидно неверной реализации.
6. Не ослаблены существующие проверки.
7. Не добавлено лишних abstraction/using/refactor.
8. Прежнее зелёное поведение не изменено без причины.

Разрешены:
- `git diff --check`;
- `git diff --name-only`;
- `git diff -- tests/Lzma.Core.Tests/Helpers/ThrowBeforeCrossingWriteStream.Tests.cs`;
- один targeted test command.

Верни строго:
APPROVE

или:

REQUEST_CHANGES
1. file:line — проблема — минимальная правка
