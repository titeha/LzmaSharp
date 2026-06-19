using System.Diagnostics;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterPpmdTests
{
  [Fact]
  public void BuildArchive_Ppmd_ОдинНепустойФайл_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("Hello PPMd inside a 7z archive! ", 50)));

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.txt", content)],
        SevenZipWriterCompressionMethod.Ppmd,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive, out byte[] fileBytes, out string fileName);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal("file.txt", fileName);
    Assert.Equal(content, fileBytes);
  }

  [Fact]
  public void BuildArchive_Ppmd_НесколькоФайлов_RoundTrip()
  {
    byte[] first = Encoding.UTF8.GetBytes("first file content, repeated repeated repeated");
    byte[] second = new byte[5000];
    byte[] third = Encoding.UTF8.GetBytes("third");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", first),
            new SevenZipArchiveWriterEntry("b.bin", second),
            new SevenZipArchiveWriterEntry("c.txt", third),
        ],
        SevenZipWriterCompressionMethod.Ppmd,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive, out SevenZipDecodedEntry[] entries);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(3, entries.Length);
    Assert.Equal(first, entries[0].Bytes);
    Assert.Equal(second, entries[1].Bytes);
    Assert.Equal(third, entries[2].Bytes);
  }

  [Fact]
  public void BuildArchive_Ppmd_ФормируетPpmdCoderСProperties()
  {
    byte[] content = Encoding.UTF8.GetBytes("structural check structural check structural check");

    SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.bin", content)],
        SevenZipWriterCompressionMethod.Ppmd,
        out byte[] archive);

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out _));
    Assert.True(reader.Header.HasValue);

    SevenZipFolder folder = Assert.Single(reader.Header.Value.StreamsInfo.UnpackInfo!.Folders);
    SevenZipCoderInfo coder = Assert.Single(folder.Coders);

    // PPMd method id = 03 04 01.
    Assert.Equal([0x03, 0x04, 0x01], coder.MethodId);

    // Properties = 5 байт: order(6) + memSize(16 МБ = 0x01000000) LE.
    Assert.Equal(5, coder.Properties.Length);
    Assert.Equal(6, coder.Properties[0]);
    Assert.Equal([0x00, 0x00, 0x00, 0x01], coder.Properties[1..]);
  }

  /// <summary>
  /// Живая проверка: PPMd-архив нашего writer-а распаковывается настоящим 7-Zip байт в байт.
  /// </summary>
  [Fact]
  public void BuildArchive_Ppmd_РаспаковываетсяНастоящим7Zip()
  {
    const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
    if (!File.Exists(sevenZip))
      return;

    byte[] content = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat(
            "PPMd writer ↔ настоящий 7-Zip. The quick brown fox. 0123456789. ", 2000)));

    SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("payload.bin", content)],
        SevenZipWriterCompressionMethod.Ppmd,
        out byte[] archive);

    string dir = Path.Combine(Path.GetTempPath(), "ppmd7zw_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string archivePath = Path.Combine(dir, "out.7z");
      File.WriteAllBytes(archivePath, archive);

      Assert.Equal(0, Run(sevenZip, $"t \"{archivePath}\""));
      Assert.Equal(0, Run(sevenZip, $"e \"{archivePath}\" -o\"{dir}\" -y"));

      byte[] extracted = File.ReadAllBytes(Path.Combine(dir, "payload.bin"));
      Assert.Equal(content, extracted);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  private static int Run(string exe, string args)
  {
    var psi = new ProcessStartInfo(exe, args)
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    using var p = Process.Start(psi)!;
    p.StandardOutput.ReadToEnd();
    p.StandardError.ReadToEnd();
    p.WaitForExit();
    return p.ExitCode;
  }
}
