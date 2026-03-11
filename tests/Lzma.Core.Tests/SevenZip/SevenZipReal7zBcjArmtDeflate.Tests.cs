using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcjArmtDeflateTests
{
  [Fact]
  public void DecodeToArray_Real7z_7Zip_BcjArmt_Deflate_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj_armt_deflate_mhc.7z");

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

    Assert.Contains(folder.Coders, c => IsBcjArmt(c.MethodId));
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
    Assert.EndsWith("armt.bin", files[0].Name, StringComparison.Ordinal);

    byte[] expected = BuildExpectedArmtLikeBytes(4096);
    Assert.Equal(expected, files[0].Bytes);
  }

  private static bool IsDeflate(byte[] methodId)
      => methodId.Length == 3
         && methodId[0] == 0x04
         && methodId[1] == 0x01
         && methodId[2] == 0x08;

  private static bool IsBcjArmt(byte[] methodId)
  {
    if (methodId.Length == 1)
      return methodId[0] == 0x08;

    return methodId.Length == 4
           && methodId[0] == 0x03
           && methodId[1] == 0x03
           && methodId[2] == 0x07
           && methodId[3] == 0x01;
  }

  private static byte[] BuildExpectedArmtLikeBytes(int length)
  {
    if ((length & 1) != 0)
      throw new ArgumentOutOfRangeException(nameof(length));

    if (length < 0x400)
      throw new ArgumentOutOfRangeException(nameof(length));

    var data = new byte[length];

    for (int i = 0; i + 2 <= data.Length; i += 2)
      BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i, 2), 0x46C0);

    WriteThumbBl(data, pos: 0x00, target: 0x200);
    WriteThumbBl(data, pos: 0x40, target: 0x300);
    WriteThumbBl(data, pos: 0x80, target: 0x180);

    return data;
  }

  private static void WriteThumbBl(byte[] data, int pos, int target)
  {
    if ((pos & 1) != 0)
      throw new ArgumentException("Позиция Thumb-инструкции должна быть кратна 2.", nameof(pos));

    if ((target & 1) != 0)
      throw new ArgumentException("Цель Thumb branch должна быть кратна 2.", nameof(target));

    if ((uint)(pos + 4) > (uint)data.Length)
      throw new ArgumentOutOfRangeException(nameof(pos));

    int pc = pos + 4;
    int diff = target - pc;

    if ((diff & 1) != 0)
      throw new ArgumentException("Смещение Thumb BL должно делиться на 2.");

    int v = diff >> 1;

    ushort hi = (ushort)(0xF000 | ((v >> 11) & 0x07FF));
    ushort lo = (ushort)(0xF800 | (v & 0x07FF));

    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos, 2), hi);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(pos + 2, 2), lo);
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
