using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcjX86Deflate64HeaderNotEncodedTests
{
  [Fact]
  public void DecodeToArray_Real7z_BcjX86_Deflate64_HeaderNotEncoded_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj_x86_deflate64_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Equal(2, folder.Coders.Length);
    Assert.Single(folder.BindPairs);
    Assert.Single(folder.PackedStreamIndices);

    Assert.Contains(folder.Coders, c => IsBcjX86(c.MethodId));
    Assert.Contains(folder.Coders, c => IsDeflate64(c.MethodId));

    bool[] inUsed = new bool[2];
    foreach (var bp in folder.BindPairs)
      inUsed[(int)bp.InIndex] = true;

    int unbound = inUsed[0] ? 1 : 0;
    Assert.Equal((ulong)unbound, folder.PackedStreamIndices[0]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.Equal("x86_deflate64.bin", files[0].Name.Replace('\\', '/'));

    byte[] expected = BuildExpectedX86LikeBytes(4096);
    Assert.Equal(expected, files[0].Bytes);
  }

  private static bool IsBcjX86(byte[] methodId)
  {
    if (methodId.Length == 1)
      return methodId[0] == 0x04;

    return methodId.Length == 4
           && methodId[0] == 0x03
           && methodId[1] == 0x03
           && methodId[2] == 0x01
           && methodId[3] == 0x03;
  }

  private static bool IsDeflate64(byte[] methodId)
  {
    return methodId.Length == 3
        && methodId[0] == 0x04
        && methodId[1] == 0x01
        && methodId[2] == 0x09;
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
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(pos + 1, 4), rel);
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
