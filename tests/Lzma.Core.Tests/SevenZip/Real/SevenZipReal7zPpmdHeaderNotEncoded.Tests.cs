using System.Runtime.CompilerServices;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zPpmdHeaderNotEncodedTests
{
  [Fact]
  public void DecodeToArray_Real7z_Ppmd_HeaderNotEncoded_Ok()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/ppmd_singlefile_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.True(reader.Header.HasValue);
    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);

    SevenZipFolder folder = reader.Header.Value.StreamsInfo.UnpackInfo!.Folders[0];
    Assert.Single(folder.Coders);
    Assert.True(IsPpmdMethodId(folder.Coders[0].MethodId));

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.Equal("ppmd.txt", files[0].Name.Replace('\\', '/'));
    Assert.Equal(CreatePpmdTextBytes(), files[0].Bytes);
  }

  private static bool IsPpmdMethodId(byte[] methodId)
  {
    return methodId.Length == 3
        && methodId[0] == 0x03
        && methodId[1] == 0x04
        && methodId[2] == 0x01;
  }

  private static byte[] CreatePpmdTextBytes()
  {
    const string line1 = "PPMd real test line 01: alpha beta gamma delta epsilon zeta.\n";
    const string line2 = "PPMd real test line 02: the quick brown fox jumps over the lazy dog.\n";
    const string line3 = "PPMd real test line 03: 0123456789 repeated text for compression.\n";

    var sb = new StringBuilder(capacity: 32 * 1024);
    for (int i = 0; i < 180; i++)
    {
      sb.Append(line1);
      sb.Append(line2);
      sb.Append(line3);
    }

    return Encoding.ASCII.GetBytes(sb.ToString());
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
