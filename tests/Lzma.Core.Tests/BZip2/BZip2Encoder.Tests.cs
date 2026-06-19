using System.Diagnostics;
using System.Text;

using ICSharpCode.SharpZipLib.BZip2;

using Lzma.Core.BZip2;

namespace Lzma.Core.Tests.BZip2;

public sealed class BZip2EncoderTests
{
  /// <summary>
  /// Кодирует данные нашим энкодером и проверяет, что результат корректно
  /// распаковывается и нашим декодером, и независимым SharpZipLib (эталон).
  /// </summary>
  private static void AssertRoundTrip(byte[] data)
  {
    byte[] encoded = BZip2Encoder.Encode(data);

    // 1) Наш декодер.
    Assert.Equal(BZip2DecodeResult.Ok, BZip2Decoder.Decode(encoded, out byte[] ours));
    Assert.Equal(data, ours);

    // 2) Независимый эталон — SharpZipLib.
    using var input = new MemoryStream(encoded);
    using var bs = new BZip2InputStream(input) { IsStreamOwner = false };
    using var output = new MemoryStream();
    bs.CopyTo(output);
    Assert.Equal(data, output.ToArray());
  }

  [Fact]
  public void Encode_Текст()
    => AssertRoundTrip(Encoding.UTF8.GetBytes("Привет, BZip2! The quick brown fox jumps over the lazy dog. " +
        "Съешь же ещё этих мягких французских булок, да выпей чаю."));

  [Fact]
  public void Encode_Нули()
    => AssertRoundTrip(new byte[20000]);

  [Fact]
  public void Encode_ПовторяющийсяПаттерн()
    => AssertRoundTrip(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("ABCABCABCxyz123 ", 4000))));

  [Fact]
  public void Encode_Случайные()
  {
    var rnd = new Random(12345);
    byte[] data = new byte[50000];
    rnd.NextBytes(data);
    AssertRoundTrip(data);
  }

  [Fact]
  public void Encode_ОдинБайт()
    => AssertRoundTrip([42]);

  [Fact]
  public void Encode_ДваБайта()
    => AssertRoundTrip([1, 1]);

  [Fact]
  public void Encode_ДлинныеПрогоны()
  {
    byte[] data = new byte[30000];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i / 1000);
    AssertRoundTrip(data);
  }

  [Fact]
  public void Encode_МногоБлоков()
  {
    // Больше одного блока (> 80 КБ входа).
    byte[] data = new byte[250000];
    var rnd = new Random(777);
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(rnd.Next(0, 5) + 'a');
    AssertRoundTrip(data);
  }

  [Fact]
  public void Encode_ВсеБайтовыеЗначения()
  {
    byte[] data = new byte[256 * 10];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i % 256);
    AssertRoundTrip(data);
  }

  /// <summary>
  /// Живая проверка: сжимаем нашим энкодером и распаковываем настоящим 7-Zip,
  /// затем сравниваем с исходными данными байт в байт.
  /// </summary>
  [Fact]
  public void Encode_РаспаковываетсяНастоящим7Zip()
  {
    const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
    if (!File.Exists(sevenZip))
      return; // Настоящий 7-Zip недоступен в этом окружении.

    byte[] data = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("Живой BZip2 → 7-Zip. ABCABC xyz 123. ", 1500)));

    byte[] encoded = BZip2Encoder.Encode(data);

    string dir = Path.Combine(Path.GetTempPath(), "bz2live_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string bz2 = Path.Combine(dir, "payload.bz2");
      File.WriteAllBytes(bz2, encoded);

      // Тест целостности архива.
      Assert.Equal(0, Run(sevenZip, $"t \"{bz2}\""));

      // Извлечение.
      Assert.Equal(0, Run(sevenZip, $"e \"{bz2}\" -o\"{dir}\" -y"));

      byte[] extracted = File.ReadAllBytes(Path.Combine(dir, "payload"));
      Assert.Equal(data, extracted);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  private static int Run(string exe, string args)
  {
    var psi = new ProcessStartInfo(exe, args)
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    using var p = Process.Start(psi)!;
    p.StandardOutput.ReadToEnd();
    p.StandardError.ReadToEnd();
    p.WaitForExit();
    return p.ExitCode;
  }
}
