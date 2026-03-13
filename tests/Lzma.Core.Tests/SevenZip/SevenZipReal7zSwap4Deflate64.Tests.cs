using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zSwap4Deflate64Tests
{
  [Fact]
  public void DecodeToArray_Real7z_Swap4_Deflate64_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/swap4_deflate64_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.True(
        reader.NextHeaderKind == SevenZipNextHeaderKind.Header ||
        reader.NextHeaderKind == SevenZipNextHeaderKind.EncodedHeader);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Equal(2, folder.Coders.Length);
    Assert.Single(folder.BindPairs);
    Assert.Single(folder.PackedStreamIndices);

    Assert.Contains(folder.Coders, c => IsSwap4(c.MethodId));
    Assert.Contains(folder.Coders, c => IsDeflate64(c.MethodId));

    var swap4 = Array.Find(folder.Coders, c => IsSwap4(c.MethodId));
    Assert.True(swap4!.Properties is null || swap4.Properties.Length == 0);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.Equal("swap4_deflate64.bin", files[0].Name.Replace('\\', '/'));

    Assert.Equal(CreateU32BigEndianRamp(sampleCount: 16 * 1024), files[0].Bytes);
  }

  private static bool IsSwap4(byte[] methodId)
      => methodId.Length == 3
         && methodId[0] == 0x02
         && methodId[1] == 0x03
         && methodId[2] == 0x04;

  private static bool IsDeflate64(byte[] methodId)
      => methodId.Length == 3
         && methodId[0] == 0x04
         && methodId[1] == 0x01
         && methodId[2] == 0x09;

  private static byte[] CreateU32BigEndianRamp(int sampleCount)
  {
    byte[] data = new byte[sampleCount * 4];

    for (int i = 0; i < sampleCount; i++)
    {
      BinaryPrimitives.WriteUInt32BigEndian(
          data.AsSpan(i * 4, 4),
          (uint)i);
    }

    return data;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
