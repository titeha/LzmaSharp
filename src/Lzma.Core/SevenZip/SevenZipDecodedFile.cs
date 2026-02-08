namespace Lzma.Core.SevenZip;

public readonly struct SevenZipDecodedFile(string name, byte[] bytes)
{
  public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

  public byte[] Bytes { get; } = bytes ?? throw new ArgumentNullException(nameof(bytes));
}
