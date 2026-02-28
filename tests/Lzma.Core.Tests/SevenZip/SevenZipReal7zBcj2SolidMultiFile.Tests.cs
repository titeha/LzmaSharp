using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcj2SolidMultiFileTests
{
  [Fact]
  public void DecodeToArray_Real7z_7Zip_Bcj2_Solid_EmptyFile_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj2_solid_a_empty_b_lzma2_d1m_mhc.7z");

    // Чтобы тест не стал “случайно зелёным” на не-BCJ2 архиве.
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];
    Assert.Contains(folder.Coders, c => IsBcj2(c.MethodId));

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(3, files.Length);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);
    foreach (var f in files)
      byName.Add(f.Name, f);

    Assert.Equal(BuildX86LikeA(4096), byName["a.bin"].Bytes);
    Assert.Empty(byName["empty.bin"].Bytes);
    Assert.Equal(BuildX86LikeB(5000), byName["b.bin"].Bytes);
  }

  private static bool IsBcj2(byte[] methodId)
  {
    return
      methodId.Length == 1 && methodId[0] == 0x1B ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x01 &&
      methodId[3] == 0x1B;
  }

  private static byte[] BuildX86LikeA(int length)
  {
    var data = new byte[length];
    for (int i = 0; i < data.Length; i++)
      data[i] = 0x90;

    WriteRel32(data, pos: 0x00, opcode: 0xE8, target: 0x200);
    WriteRel32(data, pos: 0x40, opcode: 0xE9, target: 0x300);
    WriteRel32(data, pos: 0x80, opcode: 0xE8, target: 0x180);
    return data;
  }

  private static byte[] BuildX86LikeB(int length)
  {
    var data = new byte[length];
    for (int i = 0; i < data.Length; i++)
      data[i] = 0x90;

    WriteRel32(data, pos: 0x10, opcode: 0xE8, target: 0x350);
    WriteRel32(data, pos: 0x120, opcode: 0xE9, target: 0x900);
    WriteRel32(data, pos: 0x220, opcode: 0xE8, target: 0x140);
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
