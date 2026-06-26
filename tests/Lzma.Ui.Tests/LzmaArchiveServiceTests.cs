using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты шва операций <see cref="LzmaArchiveService"/> — в первую очередь round-trip
/// «создать → открыть» для разных методов сжатия.
/// </summary>
public sealed class LzmaArchiveServiceTests
{
  [Theory]
  [InlineData(SevenZipWriterCompressionMethod.Copy)]
  [InlineData(SevenZipWriterCompressionMethod.Lzma2)]
  [InlineData(SevenZipWriterCompressionMethod.Ppmd)]
  [InlineData(SevenZipWriterCompressionMethod.Auto)]
  public async Task CreateArchive_ЗатемOpen_СодержимоеСовпадает(SevenZipWriterCompressionMethod method)
  {
    var service = new LzmaArchiveService();

    byte[] first = Encoding.UTF8.GetBytes("первый файл — текст для проверки round-trip");
    byte[] second = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("повтор-", 500)));

    SevenZipArchiveWriterEntry[] entries =
    [
        new SevenZipArchiveWriterEntry("readme.txt", first),
        new SevenZipArchiveWriterEntry("docs/data.txt", second),
    ];

    ArchiveCreateOutcome created = await service.CreateArchiveAsync(entries, method);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, created.Result);
    Assert.NotEmpty(created.Archive);

    ArchiveOpenOutcome opened = await service.OpenAsync(created.Archive, password: null);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, opened.Result);

    SevenZipDecodedEntry readme = opened.Entries.Single(e => e.Name.Replace('\\', '/') == "readme.txt");
    SevenZipDecodedEntry data = opened.Entries.Single(e => e.Name.Replace('\\', '/') == "docs/data.txt");

    Assert.Equal(first, readme.Bytes);
    Assert.Equal(second, data.Bytes);
  }

  [Fact]
  public async Task CreateArchive_ПустойНабор_ДаётВалидныйПустойАрхив()
  {
    var service = new LzmaArchiveService();

    ArchiveCreateOutcome created = await service.CreateArchiveAsync([], SevenZipWriterCompressionMethod.Lzma2);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, created.Result);

    ArchiveOpenOutcome opened = await service.OpenAsync(created.Archive, password: null);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, opened.Result);
    Assert.Empty(opened.Entries);
  }
}
