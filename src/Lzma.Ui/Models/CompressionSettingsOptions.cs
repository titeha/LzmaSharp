namespace Lzma.Ui.Models;

/// <summary>Пункт выбора числа потоков сжатия: значение (0 = авто/все ядра) и имя для UI.</summary>
public sealed record ThreadCountOption(int Value, string DisplayName);

/// <summary>Пункт выбора размера словаря LZMA2: размер в байтах и имя для UI.</summary>
public sealed record DictionarySizeOption(int Bytes, string DisplayName);

/// <summary>Пункт выбора размера тома: размер в байтах (0 = один файл, без томов) и имя для UI.</summary>
public sealed record VolumeSizeOption(long Bytes, string DisplayName);
