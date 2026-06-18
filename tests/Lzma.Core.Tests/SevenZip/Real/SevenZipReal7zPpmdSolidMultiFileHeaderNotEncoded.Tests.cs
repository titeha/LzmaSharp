using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zPpmdSolidMultiFileHeaderNotEncodedTests
{
  [Fact]
  public void DecodeToArray_Real7z_PPMd_Solid_HeaderNotEncoded_Ok()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/solid_a_empty_b_ppmd_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Single(folder.Coders);
    Assert.True(IsPpmd(folder.Coders[0].MethodId));

    Assert.NotNull(folder.Coders[0].Properties);
    Assert.Equal(5, folder.Coders[0].Properties!.Length);

    byte order = folder.Coders[0].Properties[0];
    Assert.InRange(order, (byte)2, (byte)64);

    uint mem = BinaryPrimitives.ReadUInt32LittleEndian(folder.Coders[0].Properties.AsSpan(1, 4));
    Assert.True(mem >= (1u << 11));
    Assert.True(mem <= int.MaxValue);

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

  private static bool IsPpmd(byte[] methodId)
  {
    return methodId.Length == 3
        && methodId[0] == 0x03
        && methodId[1] == 0x04
        && methodId[2] == 0x01;
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
