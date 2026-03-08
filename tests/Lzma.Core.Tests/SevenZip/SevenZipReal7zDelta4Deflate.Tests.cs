using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zDelta4DeflateTests
{
  [Fact]
  public void DecodeToArray_Real7z_Delta4_Deflate_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/delta4_deflate_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Equal(2, folder.Coders.Length);
    Assert.Contains(folder.Coders, c => IsDelta(c.MethodId));
    Assert.Contains(folder.Coders, c => IsDeflate(c.MethodId));

    var delta = Array.Find(folder.Coders, c => IsDelta(c.MethodId));
    Assert.NotNull(delta!.Properties);
    Assert.Single(delta.Properties!);
    Assert.NotEqual(0, delta.Properties[0]); // offset точно не 0

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.Equal("delta4.bin", files[0].Name.Replace('\\', '/'));

    Assert.Equal(CreateStereo16SamplesBytes(sampleCount: 16 * 1024), files[0].Bytes);
  }

  private static bool IsDelta(byte[] methodId) => methodId.Length == 1 && methodId[0] == 0x03;

  private static bool IsDeflate(byte[] methodId)
      => methodId.Length == 3 && methodId[0] == 0x04 && methodId[1] == 0x01 && methodId[2] == 0x08;

  private static byte[] CreateStereo16SamplesBytes(int sampleCount)
  {
    byte[] data = new byte[sampleCount * 4];

    for (int i = 0; i < sampleCount; i++)
    {
      ushort left = (ushort)i;
      ushort right = (ushort)(i * 3);

      int pos = i * 4;
      BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos, 2), left);
      BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2, 2), right);
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
