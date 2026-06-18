using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterEntriesTests
{
  [Fact]
  public void BuildArchive_БезФайловСоздаётПустойАрхив()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        Array.Empty<SevenZipArchiveWriterEntry>(),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void BuildArchive_ОдинПустойФайлСоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("empty.txt", [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal("empty.txt", fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void BuildArchive_ОдинНепустойФайлСоздаётCopyАрхивКоторыйЧитаетсяDecoderPath()
  {
    byte[] content = [1, 2, 3, 4, 5];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.bin", content)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal("file.bin", fileName);
    Assert.Equal(content, fileBytes);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void BuildArchive_НесколькоПустыхФайловСоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", []),
            new SevenZipArchiveWriterEntry("b.txt", []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("a.txt", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.False(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("b.txt", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.False(entry.IsDirectory);
        }]);
  }

  [Fact]
  public void BuildArchive_NullСписокВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        null!,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_NullContentВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.txt", null!),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ПовреждениеНепустогоCopyФайлаДаётInvalidData()
  {
    byte[] content = [1, 2, 3, 4, 5];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.bin", content),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    archive[SevenZipSignatureHeader.Size] ^= 0xFF;

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void BuildArchive_ПовреждениеFolderCrcВHeaderДаётInvalidData()
  {
    byte[] content = [1, 2, 3, 4, 5];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.bin", content),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    CorruptFolderCrcInNextHeaderAndRefreshHeaderCrc(
        archive,
        packedDataLength: content.Length);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Theory]
  [InlineData("")]
  [InlineData("dir\\file.txt")]
  [InlineData("bad\0name.txt")]
  public void BuildArchive_НекорректноеИмяНепустогоФайлаВозвращаетInvalidData(string fileName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(fileName, [1, 2, 3]),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_NullИмяНепустогоФайлаВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(null!, [1, 2, 3]),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_СмешанныйСценарийСПустымИНепустымФайломСоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    byte[] content = [1, 2, 3];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("empty.txt", []),
            new SevenZipArchiveWriterEntry("file.bin", content),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("empty.txt", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.False(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("file.bin", entry.Name);
          Assert.Equal(content, entry.Bytes);
          Assert.False(entry.IsDirectory);
        }]);
  }

  [Fact]
  public void BuildArchive_НесколькоПустыхФайловСНекорректнымPathВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", []),
            new SevenZipArchiveWriterEntry("dir//b.txt", []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ОднаПустаяДиректорияСоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    SevenZipDecodedEntry entry = Assert.Single(entries);

    Assert.Equal("dir", entry.Name);
    Assert.Empty(entry.Bytes);
    Assert.True(entry.IsDirectory);
  }

  [Fact]
  public void BuildArchive_ПустойФайлИДиректорияСоздаютАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", []),
            new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("a.txt", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.False(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("dir", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.True(entry.IsDirectory);
        }]);
  }

  [Fact]
  public void BuildArchive_ДиректорияСДаннымиВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("dir", [1, 2, 3], IsDirectory: true)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ДублирующиесяИменаВозвращаютInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("same.txt", []),
            new SevenZipArchiveWriterEntry("same.txt", []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ФайлИДиректорияСОдинаковымИменемВозвращаютInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("entry", []),
            new SevenZipArchiveWriterEntry("entry", [], IsDirectory: true),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Theory]
  [InlineData("file.txt", "FILE.txt")]
  [InlineData("File.txt", "file.txt")]
  [InlineData("FILE.TXT", "file.txt")]
  public void BuildArchive_ИменаОтличающиесяТолькоРегистромВозвращаютInvalidData(
    string firstName,
    string secondName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry(firstName, []),
            new SevenZipArchiveWriterEntry(secondName, []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ФайлИДиректорияСИменемОтличающимсяТолькоРегистромВозвращаютInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("entry", []),
            new SevenZipArchiveWriterEntry("ENTRY", [], IsDirectory: true),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Theory]
  [InlineData(" ")]
  [InlineData("\t")]
  [InlineData("\r\n")]
  public void BuildArchive_ИмяСостоящееТолькоИзПробельныхСимволовВозвращаетInvalidData(
    string entryName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryName, [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ДиректорияСИменемИзПробеловВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(" ", [], IsDirectory: true)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ИмяСПробеломВнутриРазрешено()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file name.txt", [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal("file name.txt", fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Theory]
  [InlineData("CON")]
  [InlineData("con.txt")]
  [InlineData("PRN")]
  [InlineData("prn.log")]
  [InlineData("AUX")]
  [InlineData("aux.data")]
  [InlineData("NUL")]
  [InlineData("nul.bin")]
  [InlineData("COM1")]
  [InlineData("com9.log")]
  [InlineData("LPT1")]
  [InlineData("lpt9.tmp")]
  public void BuildArchive_ЗарезервированноеWindowsИмяВозвращаетInvalidData(
    string entryName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryName, [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Theory]
  [InlineData("COM10.txt")]
  [InlineData("LPT10.txt")]
  [InlineData("CONSOLE.txt")]
  [InlineData("auxiliary.txt")]
  public void BuildArchive_ПохожиеНоНеЗарезервированныеИменаРазрешены(
    string entryName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryName, [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal(entryName, fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Theory]
  [InlineData("file.")]
  [InlineData("file.txt.")]
  [InlineData("file ")]
  [InlineData("file.txt ")]
  [InlineData("file\t")]
  public void BuildArchive_ИмяСНедопустимымЗавершающимСимволомВозвращаетInvalidData(
    string entryName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryName, [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Theory]
  [InlineData("dir.")]
  [InlineData("dir ")]
  public void BuildArchive_ДиректорияСНедопустимымЗавершающимСимволомВозвращаетInvalidData(
    string entryName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryName, [], IsDirectory: true)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Theory]
  [InlineData("file.name.txt")]
  [InlineData("file name.txt")]
  [InlineData(".config")]
  public void BuildArchive_ТочкаИПробелВнутриИмениРазрешены(string entryName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryName, [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal(entryName, fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Theory]
  [InlineData("file:name.txt")]
  [InlineData("file*name.txt")]
  [InlineData("file?name.txt")]
  [InlineData("file\"name.txt")]
  [InlineData("file<name.txt")]
  [InlineData("file>name.txt")]
  [InlineData("file|name.txt")]
  [InlineData("file\tname.txt")]
  [InlineData("file\rname.txt")]
  [InlineData("file\nname.txt")]
  public void BuildArchive_ИмяСНедопустимымWindowsСимволомВозвращаетInvalidData(
    string entryName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryName, [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Theory]
  [InlineData("dir:name")]
  [InlineData("dir|name")]
  [InlineData("dir\tname")]
  public void BuildArchive_ДиректорияСНедопустимымWindowsСимволомВозвращаетInvalidData(
    string entryName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryName, [], IsDirectory: true)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Theory]
  [InlineData("file-name_01.txt")]
  [InlineData("[draft] readme.md")]
  [InlineData("name.with.many.dots.txt")]
  public void BuildArchive_ДопустимыеWindowsСимволыВИмениРазрешены(string entryName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryName, [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal(entryName, fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void BuildArchive_НесколькоНепустыхCopyФайловСоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    byte[] firstContent = [1, 2, 3];
    byte[] secondContent = [4, 5, 6, 7];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.bin", firstContent),
            new SevenZipArchiveWriterEntry("b.bin", secondContent),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("a.bin", entry.Name);
          Assert.Equal(firstContent, entry.Bytes);
          Assert.False(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("b.bin", entry.Name);
          Assert.Equal(secondContent, entry.Bytes);
          Assert.False(entry.IsDirectory);
        }]);
  }

  [Fact]
  public void BuildArchive_ПустаяДиректорияИНепустойФайлСоздаютАрхивКоторыйЧитаетсяDecoderPath()
  {
    byte[] content = [4, 5, 6];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("file.bin", content),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("dir", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.True(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("file.bin", entry.Name);
          Assert.Equal(content, entry.Bytes);
          Assert.False(entry.IsDirectory);
        }]);
  }

  [Fact]
  public void BuildArchive_НесколькоEmptyEntriesИНесколькоCopyФайловСоздаютАрхивКоторыйЧитаетсяDecoderPath()
  {
    byte[] firstContent = [1, 2, 3];
    byte[] secondContent = [4, 5, 6, 7];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("empty.txt", []),
            new SevenZipArchiveWriterEntry("a.bin", firstContent),
            new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("b.bin", secondContent),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("empty.txt", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.False(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("a.bin", entry.Name);
          Assert.Equal(firstContent, entry.Bytes);
          Assert.False(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("dir", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.True(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("b.bin", entry.Name);
          Assert.Equal(secondContent, entry.Bytes);
          Assert.False(entry.IsDirectory);
        }]);
  }

  [Fact]
  public void BuildArchive_ВложенныйПустойФайлСоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("dir/empty.txt", [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    SevenZipDecodedEntry entry = Assert.Single(entries);

    Assert.Equal("dir/empty.txt", entry.Name);
    Assert.Empty(entry.Bytes);
    Assert.False(entry.IsDirectory);
  }

  [Fact]
  public void BuildArchive_ЯвнаяДиректорияИФайлВнутриНеёСоздаютАрхивКоторыйЧитаетсяDecoderPath()
  {
    byte[] content = [4, 5, 6];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("dir/file.bin", content),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("dir", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.True(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("dir/file.bin", entry.Name);
          Assert.Equal(content, entry.Bytes);
          Assert.False(entry.IsDirectory);
        }]);
  }

  [Theory]
  [InlineData("/file.txt")]
  [InlineData("dir/")]
  [InlineData("dir//file.txt")]
  [InlineData("./file.txt")]
  [InlineData("dir/./file.txt")]
  [InlineData("../file.txt")]
  [InlineData("dir/../file.txt")]
  [InlineData("dir\\file.txt")]
  public void BuildArchive_НекорректныйEntryPathВозвращаетInvalidData(string entryPath)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(entryPath, [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ВложенныйPathСЗарезервированнымСегментомВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("dir/con.txt", [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ФайлКакРодительВложенногоEntryВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("dir", []),
            new SevenZipArchiveWriterEntry("dir/file.txt", []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ДиректорияИВложенныйФайлСРодителемДругогоРегистраРазрешены()
  {
    byte[] content = [1, 2, 3];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("Dir", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("dir/file.txt", content),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("Dir", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.True(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("dir/file.txt", entry.Name);
          Assert.Equal(content, entry.Bytes);
          Assert.False(entry.IsDirectory);
        }]);
  }

  [Fact]
  public void BuildArchive_ФайлКакРодительВложенногоEntryДругогоРегистраВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("Dir", []),
            new SevenZipArchiveWriterEntry("dir/file.txt", []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ВложенныеPathОтличающиесяТолькоРегистромВозвращаютInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("dir/file.txt", []),
            new SevenZipArchiveWriterEntry("DIR/FILE.TXT", []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  private static void CorruptFolderCrcInNextHeaderAndRefreshHeaderCrc(
    byte[] archive,
    int packedDataLength)
  {
    int nextHeaderStart = SevenZipSignatureHeader.Size + packedDataLength;

    Span<byte> nextHeader = archive.AsSpan(nextHeaderStart);

    int crcDigestOffset = FindSingleFolderCrcDigestOffset(nextHeader);

    Assert.True(crcDigestOffset >= 0);

    // Портим первый байт 4-байтного folder-CRC.
    nextHeader[crcDigestOffset + 2] ^= 0xFF;

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var signatureHeader = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)packedDataLength,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    signatureHeader.Write(archive);
  }

  // Находит folder-CRC digest для архива из одного folder-а.
  // Это kCRC + AllAreDefined(0x01) + один 4-байтный CRC, после которого идёт
  // конец UnpackInfo, конец MainStreamsInfo и начало FilesInfo: 00 00 05.
  private static int FindSingleFolderCrcDigestOffset(ReadOnlySpan<byte> nextHeader)
  {
    for (int i = 0; i + 8 < nextHeader.Length; i++)
    {
      if (nextHeader[i] == SevenZipNid.Crc
          && nextHeader[i + 1] == 0x01
          && nextHeader[i + 6] == SevenZipNid.End
          && nextHeader[i + 7] == SevenZipNid.End
          && nextHeader[i + 8] == SevenZipNid.FilesInfo)
      {
        return i;
      }
    }

    return -1;
  }
}
