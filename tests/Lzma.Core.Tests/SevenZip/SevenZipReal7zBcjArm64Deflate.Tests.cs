using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcjArm64DeflateTests
{
  [Fact]
  public void DecodeToArray_Real7z_7Zip_BcjArm64_Deflate_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj_arm64_deflate_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    // Для маленьких архивов не фиксируем жёстко тип NextHeader.
    Assert.True(
        reader.NextHeaderKind == SevenZipNextHeaderKind.Header ||
        reader.NextHeaderKind == SevenZipNextHeaderKind.EncodedHeader);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipFolder folder = header.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Equal(2, folder.Coders.Length);
    Assert.Single(folder.BindPairs);
    Assert.Single(folder.PackedStreamIndices);

    Assert.Contains(folder.Coders, c => IsBcjArm64(c.MethodId));
    Assert.Contains(folder.Coders, c => IsDeflate(c.MethodId));

    // unbound InIndex должен совпасть с PackedStreamIndices[0].
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
    Assert.EndsWith("arm64.bin", files[0].Name, StringComparison.Ordinal);

    byte[] expected = BuildExpectedArm64LikeBytes(4096);
    Assert.Equal(expected, files[0].Bytes);
  }

  private static bool IsDeflate(byte[] methodId)
      => methodId.Length == 3
         && methodId[0] == 0x04
         && methodId[1] == 0x01
         && methodId[2] == 0x08;

  private static bool IsBcjArm64(byte[] methodId)
      => methodId.Length == 1
         && methodId[0] == 0x0A;

  private static byte[] BuildExpectedArm64LikeBytes(int length)
  {
    if ((length & 3) != 0)
      throw new ArgumentOutOfRangeException(nameof(length));

    ArgumentOutOfRangeException.ThrowIfLessThan(length, 0x400);

    var data = new byte[length];

    for (int i = 0; i + 4 <= data.Length; i += 4)
      BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i, 4), 0xD503201Fu);

    WriteArm64Bl(data, pos: 0x00, target: 0x200);
    WriteArm64Bl(data, pos: 0x40, target: 0x300);
    WriteArm64Bl(data, pos: 0x80, target: 0x180);

    return data;
  }

  private static void WriteArm64Bl(byte[] data, int pos, int target)
  {
    if ((pos & 3) != 0)
      throw new ArgumentException("Позиция ARM64-инструкции должна быть кратна 4.", nameof(pos));

    if ((target & 3) != 0)
      throw new ArgumentException("Цель ARM64 branch должна быть кратна 4.", nameof(target));

    if ((uint)(pos + 4) > (uint)data.Length)
      throw new ArgumentOutOfRangeException(nameof(pos));

    int diff = target - pos;

    if ((diff & 3) != 0)
      throw new ArgumentException("Смещение ARM64 BL должно делиться на 4.");

    int imm26 = diff >> 2;
    uint instruction = 0x94000000u | ((uint)imm26 & 0x03FFFFFFu);

    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(pos, 4), instruction);
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
