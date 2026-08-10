Это финальная проверка завершённого малого пакета.

Ничего не меняй.

Выполни:

1. `git status --short`
2. `git diff --check`
3. `git diff --name-only`
4. build затронутого project
5. targeted tests
6. полный `dotnet test -c Release --no-build`, только если изменён production-код
7. read-only critic review

Выдай:

- exact files;
- tests passed/failed/skipped;
- warnings;
- critic verdict;
- remaining risks;
- recommended commit title.

Commit не создавай.

Заверши READY_TO_COMMIT или BLOCKED.
