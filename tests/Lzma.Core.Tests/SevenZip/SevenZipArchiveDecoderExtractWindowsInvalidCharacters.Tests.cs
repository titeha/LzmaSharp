using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderExtractWindowsInvalidCharactersTests
{
  public static IEnumerable<object[]> InvalidWindowsEntryNames()
  {
    yield return ["bad<.bin"];
    yield return ["bad>.bin"];
    yield return ["bad\".bin"];
    yield return ["bad|.bin"];
    yield return ["bad?.bin"];
    yield return ["bad*.bin"];

    yield return ["dir/bad<.bin"];
    yield return ["dir/bad?.bin"];

    yield return ["bad\u0001.bin"];
    yield return ["dir/bad\u001F.bin"];
  }

  [Theory]
  [MemberData(nameof(InvalidWindowsEntryNames))]
  public void ExtractToDirectory_WindowsInvalidCharacters_InvalidData(string entryName)
  {
    if (!OperatingSystem.IsWindows())
      return;

    byte[] plain = MakePattern(128, mul: 31, add: 7);
    byte[] archive = BuildArchiveSingleFileCopy(plain, entryName);

    // Сам архив корректный; ошибка должна быть именно на этапе безопасного извлечения.
    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r1);
    Assert.Equal(archive.Length, consumed1);

    Assert.Single(entries);
    Assert.Equal(entryName, entries[0].Name);
    Assert.False(entries[0].IsDirectory);
    Assert.Equal(plain, entries[0].Bytes);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderExtractWindowsInvalidCharactersTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int consumed2);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r2);
      Assert.Equal(archive.Length, consumed2);

      // Корневая папка может быть создана заранее, но внутрь ничего попасть не должно.
      Assert.True(Directory.Exists(root));
      Assert.Empty(Directory.GetFileSystemEntries(root));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static byte[] BuildArchiveSingleFileCopy(byte[] plain, string fileName)
  {
    byte[] nextHeader = BuildNextHeaderSingleFileCopy(
        packSize: plain.Length,
        unpackSize: plain.Length,
        fileName: fileName);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)plain.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + plain.Length + nextHeader.Length];

    sig.Write(archive);
    Buffer.BlockCopy(plain, 0, archive, SevenZipSignatureHeader.Size, plain.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + plain.Length, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildNextHeaderSingleFileCopy(int packSize, int unpackSize, string fileName)
  {
    List<byte> h =
    [
        SevenZipNid.Header,
            SevenZipNid.MainStreamsInfo,

            // PackInfo
            SevenZipNid.PackInfo,
        ];

    WriteU64(h, 0); // PackPos
    WriteU64(h, 1); // NumPackStreams

    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize);

    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1);   // NumFolders
    h.Add(0x00);      // External = 0
    WriteU64(h, 1);   // NumCoders

    // Copy coder: MethodId = { 00 }, без props.
    h.Add(0x01);      // mainByte: idSize=1, простой coder
    h.Add(0x00);      // methodId = Copy

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End StreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1); // NumFiles

    h.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
  }

  private static byte[] MakePattern(int length, int mul, int add)
  {
    var bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));

    return bytes;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (!Directory.Exists(root))
        return;

      foreach (string filePath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        File.SetAttributes(filePath, FileAttributes.Normal);

      string[] dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
      Array.Sort(dirs, static (a, b) => b.Length.CompareTo(a.Length));

      foreach (string dirPath in dirs)
        File.SetAttributes(dirPath, FileAttributes.Directory);

      File.SetAttributes(root, FileAttributes.Directory);
    }
    catch
    {
    }

    try
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }
    catch
    {
    }
  }
}
