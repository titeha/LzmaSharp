using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zTwoFoldersBcj2AndCopyTests
{
  [Fact]
  public void DecodeToArray_Real7z_TwoFolders_Bcj2AndCopy_RangesAndDecode_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/two_folders_bcj2_and_copy_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;

    Assert.NotNull(streamsInfo.PackInfo);
    Assert.NotNull(streamsInfo.UnpackInfo);

    SevenZipPackInfo packInfo = (streamsInfo.PackInfo ?? default)!;
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo!;

    Assert.Equal(2, unpackInfo.Folders.Length);

    // 1) Проверяем что есть folder с BCJ2 и folder с Copy
    bool hasBcj2 = false;
    bool hasCopy = false;

    for (int f = 0; f < unpackInfo.Folders.Length; f++)
    {
      SevenZipFolder folder = unpackInfo.Folders[f];

      bool folderHasBcj2 = false;
      bool folderHasCopy = false;

      for (int i = 0; i < folder.Coders.Length; i++)
      {
        if (IsBcj2(folder.Coders[i].MethodId))
          folderHasBcj2 = true;
        if (IsCopy(folder.Coders[i].MethodId))
          folderHasCopy = true;
      }

      if (folderHasBcj2)
        hasBcj2 = true;
      if (folderHasCopy)
        hasCopy = true;
    }

    Assert.True(hasBcj2);
    Assert.True(hasCopy);

    // 2) PackSizes.Length == суммарное количество packed streams по folder’ам
    int totalPackedStreams = 0;
    for (int f = 0; f < unpackInfo.Folders.Length; f++)
      totalPackedStreams += unpackInfo.Folders[f].PackedStreamIndices.Length;

    Assert.Equal(totalPackedStreams, packInfo.PackSizes.Length);

    // 3) Проверяем TryGetFolderPackedStreamRanges для folder 0/1 и соответствие PackPos+PackSizes
    // Предвычислим стартовые оффсеты каждого pack stream
    var packOffsets = new ulong[packInfo.PackSizes.Length];
    ulong cur = packInfo.PackPos;
    for (int i = 0; i < packInfo.PackSizes.Length; i++)
    {
      packOffsets[i] = cur;
      cur += packInfo.PackSizes[i];
    }

    for (int folderIndex = 0; folderIndex < unpackInfo.Folders.Length; folderIndex++)
    {
      SevenZipFolder folder = unpackInfo.Folders[folderIndex];

      Assert.Equal(SevenZipFolderDecodeResult.Ok,
        SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
          streamsInfo,
          reader.PackedStreams.Span,
          folderIndex,
          out SevenZipFolderPackedStreamRange[] ranges));

      Assert.Equal(folder.PackedStreamIndices.Length, ranges.Length);

      int basePackIndex = 0;
      for (int i = 0; i < folderIndex; i++)
        basePackIndex += unpackInfo.Folders[i].PackedStreamIndices.Length;

      for (int i = 0; i < ranges.Length; i++)
      {
        int globalPackIndex = basePackIndex + i;

        Assert.Equal((uint)globalPackIndex, ranges[i].PackStreamIndex);
        Assert.Equal(folder.PackedStreamIndices[i], ranges[i].FolderInIndex);

        Assert.True(packOffsets[globalPackIndex] <= int.MaxValue);
        Assert.True(packInfo.PackSizes[globalPackIndex] <= int.MaxValue);

        Assert.Equal((int)packOffsets[globalPackIndex], ranges[i].Offset);
        Assert.Equal((int)packInfo.PackSizes[globalPackIndex], ranges[i].Length);

        // диапазон обязан быть валиден
        ReadOnlySpan<byte> slice = reader.PackedStreams.Span.Slice(ranges[i].Offset, ranges[i].Length);
        Assert.Equal(ranges[i].Length, slice.Length);
      }
    }

    // 4) Реальный decode
    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(2, files.Length);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);
    foreach (var f in files)
      byName.Add(f.Name, f);

    Assert.Equal(BuildX86LikeBytes(4096), byName["a.exe"].Bytes);
    Assert.Equal(MakeBytes(6000, mul: 31, add: 7), byName["data.bin"].Bytes);
  }

  private static bool IsCopy(byte[] methodId) => methodId.Length == 1 && methodId[0] == 0x00;

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

  private static byte[] MakeBytes(int length, int mul, int add)
  {
    var bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));
    return bytes;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
