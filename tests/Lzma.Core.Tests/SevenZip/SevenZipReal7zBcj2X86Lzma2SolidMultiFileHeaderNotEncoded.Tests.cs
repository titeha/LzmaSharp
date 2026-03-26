using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcj2X86Lzma2SolidMultiFileHeaderNotEncodedTests
{
  [Fact]
  public void DecodeToArray_Real7z_7Zip_Bcj2_X86_Lzma2_Solid_HeaderNotEncoded_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj2_solid_a_empty_b_lzma2_d1m_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);

    SevenZipHeader header = reader.Header!.Value;

    Assert.Single(header.StreamsInfo.UnpackInfo!.Folders);

    SevenZipFolder folder = header.StreamsInfo.UnpackInfo.Folders[0];

    Assert.Equal(4, folder.Coders.Length);
    Assert.Equal(3, folder.BindPairs.Length);
    Assert.Equal(4, folder.PackedStreamIndices.Length);

    Assert.Contains(folder.Coders, c => IsBcj2(c.MethodId));

    int lzma2CoderCount = 0;
    foreach (SevenZipCoderInfo coder in folder.Coders)
    {
      if (IsLzma2(coder.MethodId))
        lzma2CoderCount++;
    }

    Assert.Equal(3, lzma2CoderCount);

    Assert.Equal(
        SevenZipFolderDecodeResult.Ok,
        SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
            header.StreamsInfo,
            reader.PackedStreams.Span,
            folderIndex: 0,
            out SevenZipFolderPackedStreamRange[] ranges));

    Assert.Equal(4, ranges.Length);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(3, files.Length);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);
    foreach (SevenZipDecodedFile f in files)
      byName.Add(f.Name.Replace('\\', '/'), f);

    Assert.Equal(
        BuildExpectedX86LikeBytes(
            length: 4096,
            fill: 0x90,
            target1: 0x200,
            target2: 0x300,
            target3: 0x180),
        byName["a.bin"].Bytes);

    Assert.Empty(byName["empty.bin"].Bytes);

    Assert.Equal(
        BuildExpectedX86LikeBytes(
            length: 6000,
            fill: 0xCC,
            target1: 0x280,
            target2: 0x340,
            target3: 0x1C0),
        byName["b.bin"].Bytes);
  }

  private static bool IsBcj2(byte[] methodId)
  {
    return methodId.Length == 4
        && methodId[0] == 0x03
        && methodId[1] == 0x03
        && methodId[2] == 0x01
        && methodId[3] == 0x1B;
  }

  private static bool IsLzma2(byte[] methodId)
  {
    return methodId.Length == 1
        && methodId[0] == 0x21;
  }

  private static byte[] BuildExpectedX86LikeBytes(int length, byte fill, int target1, int target2, int target3)
  {
    var data = new byte[length];
    data.AsSpan().Fill(fill);

    WriteRel32(data, pos: 0x00, opcode: 0xE8, target: target1);
    WriteRel32(data, pos: 0x40, opcode: 0xE9, target: target2);
    WriteRel32(data, pos: 0x80, opcode: 0xE8, target: target3);

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
