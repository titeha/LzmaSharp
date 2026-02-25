using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcjX86Lzma2Tests
{
  [Fact]
  public void DecodeToArray_Real7z_7Zip_BcjX86_Lzma2_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj_x86_lzma2_d1m_mhc.7z");

    // 1) Проверяем, что в header реально есть BCJ + LZMA2 (а не "случайно без фильтра").
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipFolder folder = header.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Equal(2, folder.Coders.Length);
    Assert.Single(folder.BindPairs);
    Assert.Single(folder.PackedStreamIndices);

    Assert.Contains(folder.Coders, c => IsBcjX86(c.MethodId));
    Assert.Contains(folder.Coders, c => IsLzma2(c.MethodId));

    // unbound InIndex должен совпасть с PackedStreamIndices[0]
    bool[] inUsed = new bool[2];
    foreach (var bp in folder.BindPairs)
      inUsed[(int)bp.InIndex] = true;

    int unbound = inUsed[0] ? 1 : 0;
    Assert.Equal((ulong)unbound, folder.PackedStreamIndices[0]);

    // 2) Реальный decode
    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.EndsWith("x86.bin", files[0].Name, StringComparison.Ordinal);

    byte[] expected = BuildExpectedX86LikeBytes(4096);
    Assert.Equal(expected, files[0].Bytes);
  }

  private static bool IsLzma2(byte[] methodId) => methodId.Length == 1 && methodId[0] == 0x21;

  private static bool IsBcjX86(byte[] methodId)
  {
    // 7z может писать короткий {04} или “длинный” {03 03 01 03}; мы поддерживаем оба.
    if (methodId.Length == 1)
      return methodId[0] == 0x04;

    return methodId.Length == 4
      && methodId[0] == 0x03
      && methodId[1] == 0x03
      && methodId[2] == 0x01
      && methodId[3] == 0x03;
  }

  private static byte[] BuildExpectedX86LikeBytes(int length)
  {
    var data = new byte[length];
    for (int i = 0; i < data.Length; i++)
      data[i] = 0x90;

    WriteRel32(data, pos: 0, opcode: 0xE8, target: 0x200);
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

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
