using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zLzmaSolidMultiFileTests
{
  [Fact]
  public void DecodeToArray_Real7z_7Zip_Lzma_Solid_EmptyFile_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/solid_a_empty_b_lzma_d1m_mhc.7z");

    // Проверяем, что это реально LZMA (03 01 01), а не LZMA2.
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];
    Assert.Contains(folder.Coders, c => IsLzma(c.MethodId));
    Assert.DoesNotContain(folder.Coders, c => IsLzma2(c.MethodId));

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(3, files.Length);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);
    foreach (var f in files)
      byName.Add(f.Name, f);

    Assert.Equal(MakeBytes(4096, mul: 17, add: 3), byName["a.bin"].Bytes);
    Assert.Empty(byName["empty.bin"].Bytes);
    Assert.Equal(MakeBytes(6000, mul: 31, add: 7), byName["b.bin"].Bytes);
  }

  private static bool IsLzma(byte[] methodId) =>
    methodId.Length == 3 && methodId[0] == 0x03 && methodId[1] == 0x01 && methodId[2] == 0x01;

  private static bool IsLzma2(byte[] methodId) =>
    methodId.Length == 1 && methodId[0] == 0x21;

  private static byte[] MakeBytes(int length, int mul, int add)
  {
    var bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));
    return bytes;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
