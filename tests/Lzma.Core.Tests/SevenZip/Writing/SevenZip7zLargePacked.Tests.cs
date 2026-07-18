using Lzma.Core.SevenZip;
using Xunit;

namespace Lzma.Core.Tests.SevenZip.Writing;

/// <summary>
/// Регресс: 7z с упакованными данными БОЛЬШЕ 64 МБ должен открываться/извлекаться in-memory.
/// Раньше SevenZipNextHeaderReader отсекал такие архивы (лимит буфера 64 МБ → NotSupported),
/// хотя формат валиден (7-Zip их читал). Проявлялось на несжимаемых наборах (Copy/большой архив).
/// </summary>
public sealed class SevenZip7zLargePackedTests
{
  [Fact]
  public void Packed80МБ_ОткрываетсяИИзвлекается()
  {
    // 80 МБ случайных → Copy (несжимаемо) → packed > 64 МБ.
    var rnd = new Random(1);
    byte[] data = new byte[80 * 1024 * 1024];
    rnd.NextBytes(data);

    var entries = new[] { new SevenZipStreamingEntry("big.bin", data.LongLength, () => new MemoryStream(data, writable: false)) };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoSolidArchiveToStream(entries, ms, 1 << 20));
    Assert.True(ms.Length > 64L * 1024 * 1024, "архив должен быть больше 64 МБ");

    byte[] archive = ms.ToArray();

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.DecodeToEntries(archive, out var decoded));
    Assert.Single(decoded);
    Assert.Equal(data, decoded[0].Bytes);

    string dest = Path.Combine(Path.GetTempPath(), "lzs-bigpacked-" + Guid.NewGuid().ToString("N"));
    try
    {
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.ExtractToDirectory(archive, SevenZipDecodeOptions.Default, dest, overwrite: false, out _));
      Assert.Equal(data, File.ReadAllBytes(Path.Combine(dest, "big.bin")));
    }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
  }
}
