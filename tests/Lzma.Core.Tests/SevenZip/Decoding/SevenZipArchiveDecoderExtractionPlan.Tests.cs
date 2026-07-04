using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Cross-check плана извлечения (<see cref="SevenZipArchiveDecoder.TryBuildExtractionPlan"/>) против
/// фактического вывода <see cref="SevenZipArchiveDecoder.DecodeToEntries"/>: план, построенный из
/// header без декодирования, должен совпадать по порядку, именам, виду записей и размерам данных.
/// </summary>
public sealed class SevenZipArchiveDecoderExtractionPlanTests
{
  private static byte[] BuildRichArchive()
  {
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("План извлечения 0123456789 ", 6000)));

    SevenZipArchiveWriterEntry[] entries =
    [
        new("dir", [], IsDirectory: true),
        new("a.txt", Encoding.UTF8.GetBytes("привет")),
        new("empty.txt", []),                 // пустой файл (kEmptyStream + kEmptyFile)
        new("dir/big.bin", big),              // многочанковый файл во вложенной папке
        new("b.txt", Encoding.UTF8.GetBytes("мир")),
    ];

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));

    return archive;
  }

  [Fact]
  public void ПланСовпадаетС_DecodeToEntries()
  {
    byte[] archive = BuildRichArchive();

    // Эталон — фактический декод со всеми данными.
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

    // План — из header, без декодирования данных.
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out _));
    Assert.True(reader.Header.HasValue);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.TryBuildExtractionPlan(reader.Header.Value, out var plan, out int folderCount));

    Assert.Equal(entries.Length, plan.Length);

    for (int i = 0; i < entries.Length; i++)
    {
      Assert.Equal(entries[i].Name, plan[i].Name);

      if (entries[i].IsDirectory)
      {
        Assert.Equal(SevenZipArchiveDecoder.ExtractEntryKind.Directory, plan[i].Kind);
      }
      else if (entries[i].Bytes.Length == 0)
      {
        Assert.Equal(SevenZipArchiveDecoder.ExtractEntryKind.EmptyFile, plan[i].Kind);
      }
      else
      {
        Assert.Equal(SevenZipArchiveDecoder.ExtractEntryKind.DataFile, plan[i].Kind);
        Assert.Equal(entries[i].Bytes.LongLength, plan[i].Size);
      }
    }

    // Есть непустые файлы → должен быть хотя бы один folder.
    Assert.True(folderCount >= 1);

    // Каждая DataFile-запись ссылается на валидный folder.
    foreach (var e in plan)
      if (e.Kind == SevenZipArchiveDecoder.ExtractEntryKind.DataFile)
        Assert.InRange(e.FolderIndex, 0, folderCount - 1);
  }

  [Fact]
  public void ПланДляВсехПустых_БезFolder()
  {
    SevenZipArchiveWriterEntry[] entries =
    [
        new("dir", [], IsDirectory: true),
        new("empty1.txt", []),
        new("empty2.txt", []),
    ];

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out _));
    Assert.True(reader.Header.HasValue);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.TryBuildExtractionPlan(reader.Header.Value, out var plan, out int folderCount));

    Assert.Equal(3, plan.Length);
    Assert.Equal(0, folderCount);
    Assert.Equal(SevenZipArchiveDecoder.ExtractEntryKind.Directory, plan[0].Kind);
    Assert.Equal(SevenZipArchiveDecoder.ExtractEntryKind.EmptyFile, plan[1].Kind);
    Assert.Equal(SevenZipArchiveDecoder.ExtractEntryKind.EmptyFile, plan[2].Kind);
  }
}
