using System.Reflection;

using BenchmarkDotNet.Running;

// Точка входа: запуск через BenchmarkSwitcher позволяет фильтровать бенчмарки
// аргументами командной строки (например, --filter *Lzma2* или -f *Encode*).
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
