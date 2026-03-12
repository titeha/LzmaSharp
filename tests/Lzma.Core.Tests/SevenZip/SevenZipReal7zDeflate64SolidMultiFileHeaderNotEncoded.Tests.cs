using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zDeflate64SolidMultiFileHeaderNotEncodedTests
{
  [Fact]
  public void DecodeToArray_Real7z_Deflate64_Solid_HeaderNotEncoded_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/solid_a_empty_b_deflate64_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Single(folder.Coders);
    Assert.True(IsDeflate64(folder.Coders[0].MethodId));

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(3, files.Length);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);
    foreach (var f in files)
      byName.Add(f.Name.Replace('\\', '/'), f);

    Assert.Equal(MakeFilled(4096, 0x41), byName["a.bin"].Bytes);
    Assert.Empty(byName["empty.bin"].Bytes);
    Assert.Equal(MakeFilled(6000, 0x42), byName["b.bin"].Bytes);
  }

  private static bool IsDeflate64(byte[] methodId)
  {
    return methodId.Length == 3
        && methodId[0] == 0x04
        && methodId[1] == 0x01
        && methodId[2] == 0x09;
  }

  private static byte[] MakeFilled(int length, byte value)
  {
    byte[] bytes = new byte[length];
    bytes.AsSpan().Fill(value);
    return bytes;
  }

  private static byte[] ReadTestDataBytes(
      string relativePathFromSevenZipFolder,
      [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
