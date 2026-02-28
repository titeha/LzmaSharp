using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zHelloCopyNoEncodedHeaderTests
{
  [Fact]
  public void DecodeToArray_Real7z_Copy_HeaderNotEncoded_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/hello_copy_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];
    Assert.Contains(folder.Coders, c => IsCopy(c.MethodId));
    Assert.DoesNotContain(folder.Coders, c => IsLzma2(c.MethodId));
    Assert.DoesNotContain(folder.Coders, c => IsLzma(c.MethodId));

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.Equal("hello.bin", files[0].Name);

    byte[] expected = new byte[16384];
    for (int i = 0; i < expected.Length; i++)
      expected[i] = 0x41;

    Assert.Equal(expected, files[0].Bytes);
  }

  private static bool IsCopy(byte[] methodId) => methodId.Length == 1 && methodId[0] == 0x00;

  private static bool IsLzma2(byte[] methodId) => methodId.Length == 1 && methodId[0] == 0x21;

  private static bool IsLzma(byte[] methodId) =>
    methodId.Length == 3 && methodId[0] == 0x03 && methodId[1] == 0x01 && methodId[2] == 0x01;

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
