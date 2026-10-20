using System.IO.Compression;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Потоковое чтение центрального каталога ZIP из <see cref="Stream"/> (без загрузки данных).
/// Сверяется с одноразовым span-<see cref="ZipReader"/> и с эталонным BCL <see cref="ZipArchive"/>.
/// </summary>
public sealed class ZipStreamReaderTests
{
  [Fact]
  public void Каталог_СовпадаетСоSpanReader_ФайлыИПапки()
  {
    byte[] a = Encoding.UTF8.GetBytes("first file contents, compressible compressible compressible");
    byte[] b = Encoding.UTF8.GetBytes(new string('x', 5000)); // хорошо жмётся → Deflate
    byte[] empty = [];

    ZipWriteResult wr = ZipWriter.Build(
    [
        new ZipWriterEntry("dir/", [], IsDirectory: true),
        new ZipWriterEntry("dir/a.txt", a),
        new ZipWriterEntry("b.bin", b),
        new ZipWriterEntry("empty.txt", empty),
    ], out byte[] archive);
    Assert.Equal(ZipWriteResult.Ok, wr);

    // span-reader (эталон: даёт распакованное содержимое)
    Assert.Equal(ZipReadResult.Ok, ZipReader.Read(archive, out ZipEntry[] spanEntries));

    // потоковый reader (только метаданные)
    using var ms = new MemoryStream(archive, writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] streamEntries));

    Assert.Equal(spanEntries.Length, streamEntries.Length);

    for (int i = 0; i < spanEntries.Length; i++)
    {
      Assert.Equal(spanEntries[i].Name, streamEntries[i].Name);
      Assert.Equal(spanEntries[i].IsDirectory, streamEntries[i].IsDirectory);

      if (!spanEntries[i].IsDirectory)
      {
        Assert.Equal(spanEntries[i].Bytes.Length, streamEntries[i].UncompressedSize);
        Assert.Equal(Crc32.Compute(spanEntries[i].Bytes), streamEntries[i].Crc);
      }
    }
  }

  [Fact]
  public void Каталог_ЧитаетАрхивBcl()
  {
    // Независимый эталон: ZIP, собранный BCL.
    byte[] archive = BuildBclZip(
        ("readme.txt", Encoding.UTF8.GetBytes("hello from bcl zip")),
        ("data/payload.bin", Encoding.UTF8.GetBytes(new string('q', 8000))));

    using var ms = new MemoryStream(archive, writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

    Assert.Equal(2, entries.Length);
    Assert.Contains(entries, e => e.Name == "readme.txt" && !e.IsDirectory && e.UncompressedSize == 18);
    Assert.Contains(entries, e => e.Name == "data/payload.bin" && e.UncompressedSize == 8000);
  }

  [Fact]
  public void НеSeekableПоток_InvalidData()
  {
    ZipWriter.Build([new ZipWriterEntry("a.txt", Encoding.UTF8.GetBytes("x"))], out byte[] archive);

    using var forward = new ForwardOnlyStream(archive);
    Assert.Equal(ZipReadResult.InvalidData, ZipStreamReader.ReadCentralDirectory(forward, out _));
  }

  [Fact]
  public void Мусор_InvalidData()
  {
    byte[] garbage = new byte[500];
    for (int i = 0; i < garbage.Length; i++)
      garbage[i] = (byte)(i * 7);

    using var ms = new MemoryStream(garbage, writable: false);
    Assert.Equal(ZipReadResult.InvalidData, ZipStreamReader.ReadCentralDirectory(ms, out _));
  }

  [Fact]
  public void ZIP64_МногоЗаписей_ЧитаетВесьКаталог()
  {
    // BCL пишет ZIP64, когда записей > 65535 (16-битный счётчик EOCD переполняется).
    const int count = 70_000;

    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
      for (int i = 0; i < count; i++)
        zip.CreateEntry($"f{i}.txt"); // пустые записи — архив компактный
    }

    ms.Position = 0;
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

    Assert.Equal(count, entries.Length);
    Assert.Equal("f0.txt", entries[0].Name);
    Assert.Equal($"f{count - 1}.txt", entries[count - 1].Name);
  }

  [Fact]
  public void ZIP64_ПоэлементныйExtra_РазмерыИСмещениеИзExtra()
  {
    byte[] content = Encoding.UTF8.GetBytes("zip64 per-entry extra field payload");
    uint crc = Crc32.Compute(content);
    byte[] name = Encoding.ASCII.GetBytes("z64.txt");

    var buf = new List<byte>();

    long localOffset = buf.Count; // 0
    WriteU32(buf, 0x04034b50);                 // local file header
    WriteU16(buf, 45);                         // version needed (ZIP64)
    WriteU16(buf, 0);                          // flags
    WriteU16(buf, 0);                          // method = store
    WriteU16(buf, 0);                          // mod time
    WriteU16(buf, 0x21);                       // mod date
    WriteU32(buf, crc);
    WriteU32(buf, (uint)content.Length);       // comp size (в локальном — реальные)
    WriteU32(buf, (uint)content.Length);       // uncomp size
    WriteU16(buf, (ushort)name.Length);
    WriteU16(buf, 0);                          // extra len
    buf.AddRange(name);
    buf.AddRange(content);

    long cdStart = buf.Count;
    WriteU32(buf, 0x02014b50);                 // central header
    WriteU16(buf, 45);                         // version made by
    WriteU16(buf, 45);                         // version needed
    WriteU16(buf, 0);                          // flags
    WriteU16(buf, 0);                          // method
    WriteU16(buf, 0);                          // mod time
    WriteU16(buf, 0x21);                       // mod date
    WriteU32(buf, crc);
    WriteU32(buf, 0xFFFFFFFF);                 // comp size = сентинел → в extra
    WriteU32(buf, 0xFFFFFFFF);                 // uncomp size = сентинел → в extra
    WriteU16(buf, (ushort)name.Length);
    WriteU16(buf, 28);                         // extra len (4 + 3×8)
    WriteU16(buf, 0);                          // comment len
    WriteU16(buf, 0);                          // disk start
    WriteU16(buf, 0);                          // internal attrs
    WriteU32(buf, 0);                          // external attrs
    WriteU32(buf, 0xFFFFFFFF);                 // local offset = сентинел → в extra
    buf.AddRange(name);
    WriteU16(buf, 0x0001);                     // ZIP64 extra id
    WriteU16(buf, 24);                         // размер данных extra
    WriteU64(buf, (ulong)content.Length);      // uncompressed (порядок по APPNOTE)
    WriteU64(buf, (ulong)content.Length);      // compressed
    WriteU64(buf, (ulong)localOffset);         // offset
    long cdSize = buf.Count - cdStart;

    long zip64EocdOffset = buf.Count;
    WriteU32(buf, 0x06064b50);                 // ZIP64 EOCD record
    WriteU64(buf, 44);                         // размер записи (56 - 12)
    WriteU16(buf, 45);
    WriteU16(buf, 45);
    WriteU32(buf, 0);                          // disk number
    WriteU32(buf, 0);                          // disk with CD
    WriteU64(buf, 1);                          // entries this disk
    WriteU64(buf, 1);                          // total entries
    WriteU64(buf, (ulong)cdSize);
    WriteU64(buf, (ulong)cdStart);

    WriteU32(buf, 0x07064b50);                 // ZIP64 EOCD locator
    WriteU32(buf, 0);                          // disk with ZIP64 EOCD
    WriteU64(buf, (ulong)zip64EocdOffset);
    WriteU32(buf, 1);                          // total disks

    WriteU32(buf, 0x06054b50);                 // EOCD
    WriteU16(buf, 0);
    WriteU16(buf, 0);
    WriteU16(buf, 0xFFFF);                     // entries this disk = сентинел
    WriteU16(buf, 0xFFFF);                     // total entries = сентинел
    WriteU32(buf, 0xFFFFFFFF);                 // CD size = сентинел
    WriteU32(buf, 0xFFFFFFFF);                 // CD offset = сентинел
    WriteU16(buf, 0);                          // comment len

    byte[] fixture = buf.ToArray();

    // Фикстура валидна, если её читает независимый BCL-ридер.
    using (var za = new ZipArchive(new MemoryStream(fixture, writable: false), ZipArchiveMode.Read))
    {
      ZipArchiveEntry e = Assert.Single(za.Entries);
      Assert.Equal("z64.txt", e.FullName);
      Assert.Equal(content.Length, e.Length);

      using Stream s = e.Open();
      using var read = new MemoryStream();
      s.CopyTo(read);
      Assert.Equal(content, read.ToArray());
    }

    // Наш потоковый ридер берёт размеры/смещение из ZIP64 extra.
    using var ms = new MemoryStream(fixture, writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

    ZipStreamEntry entry = Assert.Single(entries);
    Assert.Equal("z64.txt", entry.Name);
    Assert.Equal(content.Length, entry.UncompressedSize);
    Assert.Equal(content.Length, entry.CompressedSize);
    Assert.Equal(0, entry.LocalHeaderOffset);
    Assert.Equal(crc, entry.Crc);
    Assert.Equal(0, entry.Method);
  }

  // --- B4b: валидация отрицательных Zip64-размеров --------------------------------------------

  // Строит ZIP-фикстуру с Zip64 extra по образцу теста выше, но с параметризованными значениями
  // в extra-поле (uncomp, comp, offset). Используется для проверки отклонения значений ≥ 2^63.
  private static byte[] BuildZip64FixtureWithExtraValues(ulong uncompExtra, ulong compExtra, ulong offsetExtra)
  {
    byte[] content = Encoding.UTF8.GetBytes("zip64 sign validation payload");
    uint crc = Crc32.Compute(content);
    byte[] name = Encoding.ASCII.GetBytes("z64sign.txt");

    var buf = new List<byte>();

    long localOffset = buf.Count; // 0
    WriteU32(buf, 0x04034b50);                 // local file header
    WriteU16(buf, 45);                         // version needed (ZIP64)
    WriteU16(buf, 0);                          // flags
    WriteU16(buf, 0);                          // method = store
    WriteU16(buf, 0);                          // mod time
    WriteU16(buf, 0x21);                       // mod date
    WriteU32(buf, crc);
    WriteU32(buf, (uint)content.Length);       // comp size (в локальном — реальные)
    WriteU32(buf, (uint)content.Length);       // uncomp size
    WriteU16(buf, (ushort)name.Length);
    WriteU16(buf, 0);                          // extra len
    buf.AddRange(name);
    buf.AddRange(content);

    long cdStart = buf.Count;
    WriteU32(buf, 0x02014b50);                 // central header
    WriteU16(buf, 45);                         // version made by
    WriteU16(buf, 45);                         // version needed
    WriteU16(buf, 0);                          // flags
    WriteU16(buf, 0);                          // method
    WriteU16(buf, 0);                          // mod time
    WriteU16(buf, 0x21);                       // mod date
    WriteU32(buf, crc);
    WriteU32(buf, 0xFFFFFFFF);                 // comp size = сентинел → в extra
    WriteU32(buf, 0xFFFFFFFF);                 // uncomp size = сентинел → в extra
    WriteU16(buf, (ushort)name.Length);
    WriteU16(buf, 28);                         // extra len (4 + 3×8)
    WriteU16(buf, 0);                          // comment len
    WriteU16(buf, 0);                          // disk start
    WriteU16(buf, 0);                          // internal attrs
    WriteU32(buf, 0);                          // external attrs
    WriteU32(buf, 0xFFFFFFFF);                 // local offset = сентинел → в extra
    buf.AddRange(name);
    WriteU16(buf, 0x0001);                     // ZIP64 extra id
    WriteU16(buf, 24);                         // размер данных extra
    WriteU64(buf, uncompExtra);                // uncompressed (порядок по APPNOTE)
    WriteU64(buf, compExtra);                  // compressed
    WriteU64(buf, offsetExtra);                // offset
    long cdSize = buf.Count - cdStart;

    long zip64EocdOffset = buf.Count;
    WriteU32(buf, 0x06064b50);                 // ZIP64 EOCD record
    WriteU64(buf, 44);                         // размер записи (56 - 12)
    WriteU16(buf, 45);
    WriteU16(buf, 45);
    WriteU32(buf, 0);                          // disk number
    WriteU32(buf, 0);                          // disk with CD
    WriteU64(buf, 1);                          // entries this disk
    WriteU64(buf, 1);                          // total entries
    WriteU64(buf, (ulong)cdSize);
    WriteU64(buf, (ulong)cdStart);

    WriteU32(buf, 0x07064b50);                 // ZIP64 EOCD locator
    WriteU32(buf, 0);                          // disk with ZIP64 EOCD
    WriteU64(buf, (ulong)zip64EocdOffset);
    WriteU32(buf, 1);                          // total disks

    WriteU32(buf, 0x06054b50);                 // EOCD
    WriteU16(buf, 0);
    WriteU16(buf, 0);
    WriteU16(buf, 0xFFFF);                     // entries this disk = сентинел
    WriteU16(buf, 0xFFFF);                     // total entries = сентинел
    WriteU32(buf, 0xFFFFFFFF);                 // CD size = сентинел
    WriteU32(buf, 0xFFFFFFFF);                 // CD offset = сентинел
    WriteU16(buf, 0);                          // comment len

    return buf.ToArray();
  }

  [Fact]
  public void ZIP64_Extra_ОтрицательныйUncompressedSize_InvalidData()
  {
    byte[] fixture = BuildZip64FixtureWithExtraValues(
        uncompExtra: ulong.MaxValue,                  // ≥ 2^63 → отрицательный long
        compExtra: (ulong)29,                         // валидное
        offsetExtra: 0);                              // валидное

    using var ms = new MemoryStream(fixture, writable: false);
    ZipReadResult result = ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries);

    Assert.Equal(ZipReadResult.InvalidData, result);
    Assert.Empty(entries);
  }

  [Fact]
  public void ZIP64_Extra_ОтрицательныйCompressedSize_InvalidData()
  {
    byte[] fixture = BuildZip64FixtureWithExtraValues(
        uncompExtra: (ulong)29,                         // валидное
        compExtra: 1UL << 63,                           // ≥ 2^63 → отрицательный long
        offsetExtra: 0);                                // валидное

    using var ms = new MemoryStream(fixture, writable: false);
    ZipReadResult result = ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries);

    Assert.Equal(ZipReadResult.InvalidData, result);
    Assert.Empty(entries);
  }

  [Fact]
  public void ZIP64_Extra_ОтрицательныйLocalOffset_InvalidData()
  {
    byte[] fixture = BuildZip64FixtureWithExtraValues(
        uncompExtra: (ulong)29,                         // валидное
        compExtra: (ulong)29,                           // валидное
        offsetExtra: ulong.MaxValue);                   // ≥ 2^63 → отрицательный long

    using var ms = new MemoryStream(fixture, writable: false);
    ZipReadResult result = ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries);

    Assert.Equal(ZipReadResult.InvalidData, result);
    Assert.Empty(entries);
  }

  /// <summary>
  /// Позитивный контроль билдера фикстуры (MINOR ревью B4b): на валидных значениях extra
  /// каталог читается успешно и размеры/смещение берутся именно из extra. Без этого контроля
  /// поломка билдера оставила бы три теста на отклонение сентинелей «зелёными» по посторонней
  /// причине (InvalidData возвращался бы из-за испорченной фикстуры, а не из-за знака значения).
  /// </summary>
  [Fact]
  public void ZIP64_Extra_ВалидныеЗначения_КаталогЧитается()
  {
    byte[] fixture = BuildZip64FixtureWithExtraValues(
        uncompExtra: 29,                         // = content.Length
        compExtra: 29,                           // method = store
        offsetExtra: 0);                         // реальный local offset

    using var ms = new MemoryStream(fixture, writable: false);
    ZipReadResult result = ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries);

    Assert.Equal(ZipReadResult.Ok, result);
    ZipStreamEntry entry = Assert.Single(entries);
    Assert.Equal("z64sign.txt", entry.Name);
    Assert.Equal(29, entry.UncompressedSize);
    Assert.Equal(29, entry.CompressedSize);
    Assert.Equal(0, entry.LocalHeaderOffset);
  }

  /// <summary>
  /// Граница проверки знака (MINOR ревью B4b): <see cref="long.MaxValue"/> — наибольшее
  /// допустимое значение, оно принимается (отклоняются только значения ≥ 2^63, которые
  /// unchecked-приведение из <see cref="ulong"/> переинтерпретирует как отрицательные).
  /// Читатель каталога размеры не интерпретирует, поэтому результат — Ok с теми же значениями.
  /// </summary>
  [Fact]
  public void ZIP64_Extra_LongMaxValue_ПринимаетсяКакПоложительное()
  {
    byte[] fixture = BuildZip64FixtureWithExtraValues(
        uncompExtra: (ulong)long.MaxValue,
        compExtra: (ulong)long.MaxValue,
        offsetExtra: (ulong)long.MaxValue);

    using var ms = new MemoryStream(fixture, writable: false);
    ZipReadResult result = ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries);

    Assert.Equal(ZipReadResult.Ok, result);
    ZipStreamEntry entry = Assert.Single(entries);
    Assert.Equal(long.MaxValue, entry.UncompressedSize);
    Assert.Equal(long.MaxValue, entry.CompressedSize);
    Assert.Equal(long.MaxValue, entry.LocalHeaderOffset);
  }

  // --- B4c: overflow-safe guard смещения ZIP64 EOCD -------------------------------------------

  /// <summary>
  /// Locator ZIP64 EOCD со смещением, при котором <c>offset + Zip64EocdMinSize</c> переполняет
  /// <c>long</c>: guard обязан сравнивать без сложения недоверенных величин, иначе переполнение
  /// даёт отрицательное значение, проверка пропускается, а seek на огромное значение для
  /// <see cref="MemoryStream"/> бросает <see cref="ArgumentOutOfRangeException"/> вместо
  /// контролируемого <see cref="ZipReadResult.InvalidData"/>.
  /// </summary>
  [Fact]
  public void ZIP64_Locator_СмещениеСПереполнением_InvalidDataБезИсключения()
  {
    byte[] fixture = BuildZip64FixtureWithExtraValues(
        uncompExtra: (ulong)29,
        compExtra: (ulong)29,
        offsetExtra: 0);
    // long.MaxValue - 8 + 56 переполняет long и даёт отрицательное значение.
    PatchZip64LocatorOffset(fixture, (ulong)(long.MaxValue - 8));

    using var ms = new MemoryStream(fixture, writable: false);
    ZipReadResult result = ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries);

    Assert.Equal(ZipReadResult.InvalidData, result);
    Assert.Empty(entries);
  }

  /// <summary>Перезаписывает 8-байтовое смещение ZIP64 EOCD в locator-е (сигнатура 0x07064b50),
  /// поиск с конца — locator стоит перед EOCD.</summary>
  private static void PatchZip64LocatorOffset(byte[] fixture, ulong offset)
  {
    for (int i = fixture.Length - 20; i >= 0; i--)
    {
      if (fixture[i] == 0x50 && fixture[i + 1] == 0x4b && fixture[i + 2] == 0x06 && fixture[i + 3] == 0x07)
      {
        for (int b = 0; b < 8; b++)
          fixture[i + 8 + b] = (byte)(offset >> (b * 8));
        return;
      }
    }

    Assert.Fail("locator ZIP64 EOCD не найден в фикстуре");
  }

  private static void WriteU16(List<byte> buf, ushort v)
  {
    buf.Add((byte)v);
    buf.Add((byte)(v >> 8));
  }

  private static void WriteU32(List<byte> buf, uint v)
  {
    for (int i = 0; i < 4; i++)
      buf.Add((byte)(v >> (i * 8)));
  }

  private static void WriteU64(List<byte> buf, ulong v)
  {
    for (int i = 0; i < 8; i++)
      buf.Add((byte)(v >> (i * 8)));
  }

  private static byte[] BuildBclZip(params (string Name, byte[] Content)[] files)
  {
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach ((string name, byte[] content) in files)
      {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream s = entry.Open();
        s.Write(content, 0, content.Length);
      }
    }

    return ms.ToArray();
  }

  // Поток без произвольного доступа — для проверки требования seekable.
  private sealed class ForwardOnlyStream(byte[] data) : Stream
  {
    private readonly MemoryStream _inner = new(data, writable: false);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        _inner.Dispose();
      base.Dispose(disposing);
    }
  }
}
