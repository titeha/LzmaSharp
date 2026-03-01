using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zUltraLzma2D64mSolidTests
{
  [Fact]
  public void DecodeToArray_Real7z_Ultra_Lzma2_D64m_Solid_EmptyFile_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/ultra_a70m_empty_lzma2_d64m_solid_mhc.7z");

    // Проверяем, что это реально LZMA2 и что словарь действительно 64m.
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];

    SevenZipCoderInfo lzma2 = default!;
    bool found = false;
    foreach (var c in folder.Coders)
    {
      if (c.MethodId.Length == 1 && c.MethodId[0] == 0x21)
      {
        lzma2 = c;
        found = true;
        break;
      }
    }
    Assert.True(found);
    Assert.NotNull(lzma2.Properties);
    Assert.Single(lzma2.Properties!);

    Assert.True(SevenZipLzma2Coder.TryDecodeDictionarySize(lzma2.Properties[0], out uint dict));
    Assert.Equal(64u * 1024u * 1024u, dict);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(2, files.Length);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);
    foreach (var f in files)
      byName.Add(f.Name, f);

    Assert.Equal(2, byName.Count);
    Assert.True(byName.ContainsKey("a.bin"));
    Assert.True(byName.ContainsKey("empty.bin"));

    Assert.Equal(70 * 1024 * 1024, byName["a.bin"].Bytes.Length);
    AssertFilled(byName["a.bin"].Bytes, 0x41);

    Assert.Empty(byName["empty.bin"].Bytes);

    static void AssertFilled(byte[] data, byte value)
    {
      for (int i = 0; i < data.Length; i++)
      {
        if (data[i] != value)
          Assert.Fail($"Неверный байт в позиции {i}: {data[i]:X2}, ожидалось {value:X2}");
      }
    }
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
