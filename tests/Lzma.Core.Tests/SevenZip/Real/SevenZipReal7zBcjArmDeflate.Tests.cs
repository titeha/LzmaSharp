using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcjArmDeflateTests
{
  [Fact]
  public void DecodeToArray_Real7z_7Zip_BcjArm_Deflate_Ok()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/bcj_arm_deflate_mhc.7z");

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

    Assert.Contains(folder.Coders, c => IsBcjArm(c.MethodId));
    Assert.Contains(folder.Coders, c => IsDeflate(c.MethodId));

    // unbound InIndex должен совпасть с PackedStreamIndices[0]
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
    Assert.EndsWith("arm.bin", files[0].Name, StringComparison.Ordinal);

    byte[] expected = BuildExpectedArmLikeBytes(4096);
    Assert.Equal(expected, files[0].Bytes);
  }

  private static bool IsDeflate(byte[] methodId)
      => methodId.Length == 3
         && methodId[0] == 0x04
         && methodId[1] == 0x01
         && methodId[2] == 0x08;

  private static bool IsBcjArm(byte[] methodId)
  {
    if (methodId.Length == 1)
      return methodId[0] == 0x07;

    return methodId.Length == 4
           && methodId[0] == 0x03
           && methodId[1] == 0x03
           && methodId[2] == 0x05
           && methodId[3] == 0x01;
  }

  private static byte[] BuildExpectedArmLikeBytes(int length)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(length, 0x400);

    var data = new byte[length];

    for (int i = 0; i + 4 <= data.Length; i += 4)
      BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i, 4), 0xE1A00000u);

    WriteArmBranch(data, pos: 0x00, link: true, target: 0x200);
    WriteArmBranch(data, pos: 0x40, link: false, target: 0x300);
    WriteArmBranch(data, pos: 0x80, link: true, target: 0x180);

    return data;
  }

  private static void WriteArmBranch(byte[] data, int pos, bool link, int target)
  {
    if ((pos & 3) != 0)
      throw new ArgumentException("Позиция ARM-инструкции должна быть кратна 4.", nameof(pos));

    if ((target & 3) != 0)
      throw new ArgumentException("Цель ARM branch должна быть кратна 4.", nameof(target));

    if ((uint)(pos + 4) > (uint)data.Length)
      throw new ArgumentOutOfRangeException(nameof(pos));

    int pc = pos + 8;
    int diff = target - pc;

    if ((diff & 3) != 0)
      throw new ArgumentException("Смещение ARM branch должно делиться на 4.");

    int imm24 = diff >> 2;
    uint instruction = (link ? 0xEB000000u : 0xEA000000u) | ((uint)imm24 & 0x00FFFFFFu);

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
