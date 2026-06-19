# Бенчмарки (этап 3)

Проект `Lzma.Core.Benchmarks` на [BenchmarkDotNet](https://benchmarkdotnet.org/) измеряет
горячие пути кодеков перед оптимизацией. Без цифр оптимизация — угадайка
(см. [`docs/PERFORMANCE_PLAN.md`](../docs/PERFORMANCE_PLAN.md)).

## Как запускать

Только в Release:

```bash
# все бенчмарки
dotnet run -c Release --project bench/Lzma.Core.Benchmarks

# фильтр по имени (BenchmarkSwitcher)
dotnet run -c Release --project bench/Lzma.Core.Benchmarks -- --filter "*Lzma2*"
```

Размеры данных задаются параметром `SizeMiB` (сейчас 1 и 64 МиБ). Используется `[ShortRunJob]`
(3 прогрева + 3 измерения) — баланс между скоростью прогона и стабильностью цифр; для финальных
замеров можно временно поднять до полного job.

## Порядок работы

По договорённости — **по одному кодеку за раз**: доводим один до хорошего состояния, затем
переходим к следующему. Текущий фокус — ядро **LZMA2**. Базовые цифры и наблюдения —
в [`BASELINE.md`](BASELINE.md).
