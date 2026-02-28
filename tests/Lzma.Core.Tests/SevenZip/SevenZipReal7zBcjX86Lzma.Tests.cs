using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcjX86LzmaTests
{
  [Fact]
  public void DecodeToArray_Real7z_7Zip_BcjX86_Lzma_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj_x86_lzma_d1m_mhc.7z");

    // Проверяем, что в folder реально есть BCJ(x86) и LZMA(03 01 01), и нет LZMA2.
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Contains(folder.Coders, c => IsBcjX86(c.MethodId));
    Assert.Contains(folder.Coders, c => IsLzma(c.MethodId));
    Assert.DoesNotContain(folder.Coders, c => IsLzma2(c.MethodId));

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.EndsWith("x86.bin", files[0].Name, StringComparison.Ordinal);

    Assert.Equal(BuildX86LikeBytes(4096), files[0].Bytes);
  }

  private static bool IsLzma(byte[] methodId) =>
    methodId.Length == 3 && methodId[0] == 0x03 && methodId[1] == 0x01 && methodId[2] == 0x01;

  private static bool IsLzma2(byte[] methodId) =>
    methodId.Length == 1 && methodId[0] == 0x21;

  private static bool IsBcjX86(byte[] methodId)
  {
    // 7z может писать короткий {04} или длинный {03 03 01 03}.
    if (methodId.Length == 1)
      return methodId[0] == 0x04;

    return methodId.Length == 4
      && methodId[0] == 0x03
      && methodId[1] == 0x03
      && methodId[2] == 0x01
      && methodId[3] == 0x03;
  }

  private static byte[] BuildX86LikeBytes(int length)
  {
    var data = new byte[length];
    for (int i = 0; i < data.Length; i++)
      data[i] = 0x90;

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

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
