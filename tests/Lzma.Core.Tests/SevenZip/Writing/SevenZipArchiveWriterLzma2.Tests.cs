using System.Diagnostics;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterLzma2Tests
{
  [Fact]
  public void BuildArchive_Lzma2_ОдинНепустойФайл_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes("Hello LZMA2 inside a 7z archive! Hello LZMA2 inside a 7z archive!");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.txt", content)],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal("file.txt", fileName);
    Assert.Equal(content, fileBytes);
  }

  [Fact]
  public void BuildArchive_Lzma2_НесколькоФайлов_RoundTrip()
  {
    byte[] first = Encoding.UTF8.GetBytes("first file content, repeated repeated repeated");
    byte[] second = new byte[5000]; // нули — хорошо сжимаются
    byte[] third = Encoding.UTF8.GetBytes("third");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", first),
            new SevenZipArchiveWriterEntry("b.bin", second),
            new SevenZipArchiveWriterEntry("c.txt", third),
        ],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(3, entries.Length);

    Assert.Equal("a.txt", entries[0].Name);
    Assert.Equal(first, entries[0].Bytes);
    Assert.Equal("b.bin", entries[1].Name);
    Assert.Equal(second, entries[1].Bytes);
    Assert.Equal("c.txt", entries[2].Name);
    Assert.Equal(third, entries[2].Bytes);
  }

  [Fact]
  public void BuildArchive_Lzma2_СмешанныйСценарий_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes("nested compressed content nested compressed content");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("empty.txt", []),
            new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("dir/file.bin", content),
        ],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(3, entries.Length);

    Assert.Equal("empty.txt", entries[0].Name);
    Assert.False(entries[0].IsDirectory);
    Assert.Empty(entries[0].Bytes);

    Assert.Equal("dir", entries[1].Name);
    Assert.True(entries[1].IsDirectory);

    Assert.Equal("dir/file.bin", entries[2].Name);
    Assert.False(entries[2].IsDirectory);
    Assert.Equal(content, entries[2].Bytes);
  }

  [Fact]
  public void BuildArchive_Lzma2_ПовторяющиесяДанные_СжимаютсяМеньшеОригинала()
  {
    byte[] content = new byte[100_000];
    Array.Fill(content, (byte)'X');

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("big.bin", content)],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    Assert.True(
        archive.Length < content.Length,
        $"Ожидалось сжатие: archive={archive.Length}, content={content.Length}.");

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal("big.bin", fileName);
    Assert.Equal(content, fileBytes);
  }

  [Fact]
  public void BuildArchive_Lzma2_ФормируетLzma2CoderСProperties()
  {
    byte[] content = Encoding.UTF8.GetBytes("structural check structural check structural check");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.bin", content)],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out _));
    Assert.True(reader.Header.HasValue);

    SevenZipUnpackInfo unpackInfo = reader.Header.Value.StreamsInfo.UnpackInfo!;
    SevenZipFolder folder = Assert.Single(unpackInfo.Folders);
    SevenZipCoderInfo coder = Assert.Single(folder.Coders);

    Assert.Equal([0x21], coder.MethodId);

    // properties LZMA2 — один байт размера словаря; для 1<<16 это 8.
    byte propertyByte = Assert.Single(coder.Properties);
    Assert.Equal(8, propertyByte);

    // unpack-размер folder-а — исходная длина файла.
    ulong[] unpackSizes = Assert.Single(unpackInfo.FolderUnpackSizes);
    Assert.Equal((ulong)content.Length, Assert.Single(unpackSizes));
  }

  [Fact]
  public void BuildArchive_МетодПоУмолчанию_ОстаётсяCopy()
  {
    byte[] content = [1, 2, 3, 4, 5];

    SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.bin", content)],
        out byte[] archive);

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out _));
    Assert.True(reader.Header.HasValue);

    SevenZipFolder folder = Assert.Single(reader.Header.Value.StreamsInfo.UnpackInfo!.Folders);
    SevenZipCoderInfo coder = Assert.Single(folder.Coders);

    Assert.Equal([0x00], coder.MethodId);
  }

  /// <summary>
  /// Живая проверка: собираем LZMA2-архив нашим writer-ом на данных больше одного чанка
  /// (несущий словарь => первый чанк 0xE0, последующие 0x80) и распаковываем настоящим
  /// 7-Zip, сравнивая байт в байт.
  /// </summary>
  [Fact]
  public void BuildArchive_Lzma2_РаспаковываетсяНастоящим7Zip()
  {
    const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
    if (!File.Exists(sevenZip))
      return; // Настоящий 7-Zip недоступен в этом окружении.

    // ~300 КБ текста => несколько LZMA2-чанков, кросс-чанковые матчи и перенос состояния.
    byte[] content = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat(
            "Несущий словарь LZMA2 ↔ настоящий 7-Zip. The quick brown fox jumps over the lazy dog. 0123456789. ", 3000)));

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("payload.bin", content)],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    string dir = Path.Combine(Path.GetTempPath(), "lzma2live_" + Guid.NewGuid().ToString("N"));
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
