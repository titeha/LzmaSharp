using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

using Lzma.Core.Ppmd;

namespace Lzma.Core.Tests.Ppmd;

public sealed class Ppmd7EncoderTests
{
  private const int Order = 6;
  private const uint Mem = 16u << 20; // 16 МБ — как по умолчанию у 7-Zip

  /// <summary>
  /// Кодирует данные нашим энкодером и проверяет, что наш (независимо сверенный
  /// с настоящим 7-Zip) декодер восстанавливает их байт в байт.
  /// </summary>
  private static void AssertRoundTrip(byte[] data, int order = Order, uint mem = 1u << 20)
  {
    Assert.Equal(Ppmd7EncodeResult.Ok, Ppmd7Encoder.Encode(data, order, mem, out byte[] encoded));

    byte[] decoded = new byte[data.Length];
    Assert.Equal(Ppmd7DecodeResult.Ok, Ppmd7Decoder.Decode(encoded, order, mem, decoded));
    Assert.Equal(data, decoded);
  }

  [Fact]
  public void Encode_Текст()
    => AssertRoundTrip(Encoding.UTF8.GetBytes("Привет, PPMd! The quick brown fox jumps over the lazy dog. " +
        "Съешь же ещё этих мягких французских булок, да выпей чаю."));

  [Fact]
  public void Encode_Нули()
    => AssertRoundTrip(new byte[10000]);

  [Fact]
  public void Encode_ПовторяющийсяПаттерн()
    => AssertRoundTrip(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("ABCABCABCxyz123 ", 2000))));

  [Fact]
  public void Encode_Случайные()
  {
    var rnd = new Random(54321);
    byte[] data = new byte[30000];
    rnd.NextBytes(data);
    AssertRoundTrip(data);
  }

  [Fact]
  public void Encode_ОдинБайт()
    => AssertRoundTrip([42]);

  [Fact]
  public void Encode_ВсеБайтовыеЗначения()
  {
    byte[] data = new byte[256 * 8];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i % 256);
    AssertRoundTrip(data);
  }

  [Theory]
  [InlineData(2)]
  [InlineData(4)]
  [InlineData(16)]
  [InlineData(32)]
  public void Encode_РазныйOrder(int order)
    => AssertRoundTrip(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("order test данные ", 500))), order);

  /// <summary>
  /// Эталонная сверка: наш PPMd-поток должен совпадать байт в байт с тем,
  /// что производит настоящий 7-Zip при тех же order/memSize.
  /// </summary>
  [Fact]
  public void Encode_СовпадаетСНастоящим7Zip()
  {
    const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
    if (!File.Exists(sevenZip))
      return; // Настоящий 7-Zip недоступен в этом окружении.

    byte[] data = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("PPMd ↔ 7-Zip байт-в-байт. The quick brown fox. 12345. ", 800)));

    string dir = Path.Combine(Path.GetTempPath(), "ppmdlive_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string inputFile = Path.Combine(dir, "input.bin");
      File.WriteAllBytes(inputFile, data);

      string archive = Path.Combine(dir, "out.7z");
      // PPMd, order 6, память 16 МБ; без заголовочного сжатия (mhc=off), чтобы packed-поток лежал «как есть».
      int exit = Run(sevenZip, $"a -t7z -m0=PPMd:o{Order}:mem16m -mhc=off \"{archive}\" \"{inputFile}\"");
      Assert.Equal(0, exit);

      byte[] sevenZipPacked = ExtractSinglePackedStream(File.ReadAllBytes(archive));

      Assert.Equal(Ppmd7EncodeResult.Ok, Ppmd7Encoder.Encode(data, Order, Mem, out byte[] ours));

      Assert.Equal(sevenZipPacked, ours);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  /// <summary>
  /// Извлекает единственный упакованный поток из простого .7z: данные пакетов
  /// лежат сразу после 32-байтного сигнатурного заголовка и занимают
  /// [32, 32 + NextHeaderOffset).
  /// </summary>
  private static byte[] ExtractSinglePackedStream(byte[] archive)
  {
    // Сигнатурный заголовок 7z: 6 байт сигнатуры + 2 версии + 4 CRC + NextHeaderOffset(u64)@12.
    ulong nextHeaderOffset = BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(12, 8));
    return archive.AsSpan(32, (int)nextHeaderOffset).ToArray();
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
