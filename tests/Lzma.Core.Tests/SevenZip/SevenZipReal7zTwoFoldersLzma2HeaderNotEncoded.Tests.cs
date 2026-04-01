using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zTwoFoldersLzma2HeaderNotEncodedTests
{
  [Fact]
  public void DecodeToArray_Real7z_TwoFolders_Lzma2_HeaderNotEncoded_RangesAndDecode_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/two_folders_a_b_lzma2_d1m_ms1f_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;

    Assert.NotNull(streamsInfo.PackInfo);
    Assert.NotNull(streamsInfo.UnpackInfo);

    SevenZipPackInfo packInfo = streamsInfo.PackInfo.Value;
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo!;

    Assert.Equal(2, unpackInfo.Folders.Length);
    Assert.Equal(2, packInfo.PackSizes.Length);

    // Проверяем адресацию packed streams для folder 0/1.
    Assert.Equal(
        SevenZipFolderDecodeResult.Ok,
        SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
            streamsInfo,
            reader.PackedStreams.Span,
            folderIndex: 0,
            out SevenZipFolderPackedStreamRange[] r0));

    Assert.Single(r0);

    Assert.Equal(
        SevenZipFolderDecodeResult.Ok,
        SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
            streamsInfo,
            reader.PackedStreams.Span,
            folderIndex: 1,
            out SevenZipFolderPackedStreamRange[] r1));

    Assert.Single(r1);

    ulong packPos = packInfo.PackPos;
    Assert.True(packPos <= int.MaxValue);
    Assert.True(packInfo.PackSizes[0] <= int.MaxValue);
    Assert.True(packInfo.PackSizes[1] <= int.MaxValue);

    int exp0Offset = (int)packPos;
    int exp0Len = (int)packInfo.PackSizes[0];

    ulong exp1OffsetU64 = packPos + packInfo.PackSizes[0];
    Assert.True(exp1OffsetU64 <= int.MaxValue);

    int exp1Offset = (int)exp1OffsetU64;
    int exp1Len = (int)packInfo.PackSizes[1];

    Assert.Equal(0u, r0[0].PackStreamIndex);
    Assert.Equal(exp0Offset, r0[0].Offset);
    Assert.Equal(exp0Len, r0[0].Length);

    Assert.Equal(1u, r1[0].PackStreamIndex);
    Assert.Equal(exp1Offset, r1[0].Offset);
    Assert.Equal(exp1Len, r1[0].Length);

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

    Assert.Equal(MakeBytes(4096, mul: 17, add: 3), byName["a.bin"].Bytes);
    Assert.Equal(MakeBytes(6000, mul: 31, add: 7), byName["b.bin"].Bytes);
  }

  private static byte[] MakeBytes(int length, int mul, int add)
  {
    var bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));
    return bytes;
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
