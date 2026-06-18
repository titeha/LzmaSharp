using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zDecodeSingleFileToArrayTests
{
  [Fact]
  public void DecodeSingleFileToArray_Real7z_BcjX86_PPMd_HeaderNotEncoded_Ok()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/bcj_x86_ppmd_mhc_off.7z");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal("x86_ppmd.bin", fileName.Replace('\\', '/'));
    Assert.Equal(BuildExpectedX86LikeBytes(4096), fileBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_Real7z_MultiFileArchive_NotSupported()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/solid_a_empty_b_lzma2_d1m_mhc_off.7z");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
  }

  private static byte[] BuildExpectedX86LikeBytes(int length)
  {
    var data = new byte[length];
    data.AsSpan().Fill(0x90);

    WriteRel32(data, pos: 0x00, opcode: 0xE8, target: 0x200);
    WriteRel32(data, pos: 0x40, opcode: 0xE9, target: 0x300);
    WriteRel32(data, pos: 0x80, opcode: 0xE8, target: 0x180);

    return data;
  }

  private static void WriteRel32(byte[] data, int pos, byte opcode, int target)
  {
    data[pos] = opcode;
    int rel = target - (pos + 5);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(pos + 1, 4), rel);
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
