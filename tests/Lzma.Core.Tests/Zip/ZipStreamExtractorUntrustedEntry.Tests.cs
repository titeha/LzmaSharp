using System.Buffers.Binary;

using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Публичный API потокового извлечения принимает <see cref="IReadOnlyList{T}"/> записей
/// <see cref="ZipStreamEntry"/>, поэтому потребитель может передать значения, собранные вручную
/// (не прочитанные из каталога архива). Отрицательные и запредельные <c>LocalHeaderOffset</c> /
/// <c>CompressedSize</c> / <c>UncompressedSize</c> обязаны давать контролируемый
/// <see cref="ZipExtractResult.InvalidData"/>, а не исключение на seek или на аллокации
/// (B4c, hardening по MINOR-замечаниям ревью B4b).
/// <para>
/// На архивный маршрут это не влияет: после чтения центрального каталога отрицательные значения
/// не проходят (проверки знака в <c>ZipStreamReader</c>), поэтому здесь проверяется именно
/// граница публичного API.
/// </para>
/// </summary>
public sealed class ZipStreamExtractorUntrustedEntryTests
{
  private const uint LocalFileSignature = 0x04034b50;

  /// <summary>Корректный локальный заголовок (30 байт, имя и extra пустые) — чтобы проверка
  /// сигнатуры пропустила запись дальше, к проверяемому полю.</summary>
  private static byte[] ValidLocalHeader()
  {
    byte[] header = new byte[30];
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), LocalFileSignature);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), 20);   // version needed
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), 0);    // flags
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8, 2), 8);    // method = Deflate
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(14, 4), 0);   // crc
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18, 4), 0);   // compressed size
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22, 4), 0);   // uncompressed size
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26, 2), 0);   // name length
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28, 2), 0);   // extra length
    return header;
  }

  private static string NewDest()
  {
    string dest = Path.Combine(Path.GetTempPath(), "lzs-untrusted-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dest);
    return dest;
  }

  private static void TryDelete(string dest)
  {
    try
    {
      if (Directory.Exists(dest))
        Directory.Delete(dest, recursive: true);
    }
    catch (IOException) { /* best-effort */ }
    catch (UnauthorizedAccessException) { /* best-effort */ }
  }

  private static ZipStreamEntry Entry(
      long localHeaderOffset = 0,
      long compressedSize = 0,
      long uncompressedSize = 0,
      bool isEncrypted = false)
      => new("a.bin", 8, 0, compressedSize, uncompressedSize, localHeaderOffset,
             IsDirectory: false, Flags: 0, IsEncrypted: isEncrypted);

  /// <summary>
  /// Отрицательное смещение локального заголовка: <c>Stream.Position</c> с отрицательным значением
  /// бросает <see cref="ArgumentOutOfRangeException"/>, поэтому поле проверяется до seek.
  /// </summary>
  [Fact]
  public void ExtractToDirectory_ОтрицательноеСмещениеЗаголовка_ВозвращаетInvalidData()
  {
    using var archive = new MemoryStream(ValidLocalHeader());
    string dest = NewDest();

    try
    {
      ZipExtractResult result = ZipStreamExtractor.ExtractToDirectory(
          archive, [Entry(localHeaderOffset: -1)], dest);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      Assert.Empty(Directory.GetFiles(dest));
    }
    finally
    {
      TryDelete(dest);
    }
  }

  /// <summary>
  /// Запредельное смещение: сумма <c>offset + 30 + nameLen + extraLen</c> переполняет <c>long</c>,
  /// поэтому граница сравниванием с длиной потока должна считаться без сложения недоверенных величин.
  /// </summary>
  [Fact]
  public void ExtractToDirectory_ЗапредельноеСмещениеЗаголовка_ВозвращаетInvalidData()
  {
    using var archive = new MemoryStream(ValidLocalHeader());
    string dest = NewDest();

    try
    {
      ZipExtractResult result = ZipStreamExtractor.ExtractToDirectory(
          archive, [Entry(localHeaderOffset: long.MaxValue)], dest);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      Assert.Empty(Directory.GetFiles(dest));
    }
    finally
    {
      TryDelete(dest);
    }
  }

  /// <summary>Отрицательный размер сжатых данных: проверка «данные помещаются в поток» не должна
  /// пропускать отрицательную длину, иначе копия/инфлейт читают поток с отрицательным остатком.</summary>
  [Fact]
  public void ExtractToDirectory_ОтрицательныйРазмерСжатыхДанных_ВозвращаетInvalidData()
  {
    using var archive = new MemoryStream(ValidLocalHeader());
    string dest = NewDest();

    try
    {
      ZipExtractResult result = ZipStreamExtractor.ExtractToDirectory(
          archive, [Entry(compressedSize: -1, uncompressedSize: -1)], dest);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      Assert.Empty(Directory.GetFiles(dest));
    }
    finally
    {
      TryDelete(dest);
    }
  }

  /// <summary>
  /// Отрицательный размер зашифрованного члена: ветвь AES проверяет только
  /// <c>CompressedSize &gt; int.MaxValue</c>, поэтому отрицательное значение доходило до
  /// <c>new byte[CompressedSize]</c> и давало <see cref="OverflowException"/>.
  /// </summary>
  [Fact]
  public void ExtractToDirectory_ЗашифрованныйЧленСОтрицательнымРазмером_ВозвращаетInvalidData()
  {
    using var archive = new MemoryStream(ValidLocalHeader());
    string dest = NewDest();

    try
    {
      ZipExtractResult result = ZipStreamExtractor.ExtractToDirectory(
          archive, [Entry(compressedSize: -1, isEncrypted: true)], dest, password: [1, 2, 3]);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      Assert.Empty(Directory.GetFiles(dest));
    }
    finally
    {
      TryDelete(dest);
    }
  }

  /// <summary>
  /// Позитивный контроль: настоящие записи, прочитанные из каталога построенного архива,
  /// извлекаются успешно — охранные проверки не ломают обычный маршрут.
  /// </summary>
  [Fact]
  public void ExtractToDirectory_ЗаписиИзКаталогаРеальногоАрхива_ИзвлекаютсяУспешно()
  {
    byte[] payload = System.Text.Encoding.UTF8.GetBytes("данные для позитивного контроля");
    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build([new ZipWriterEntry("a.bin", payload)], out byte[] archiveBytes));

    using var archive = new MemoryStream(archiveBytes, writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(archive, out ZipStreamEntry[] entries));
    Assert.Single(entries);

    string dest = NewDest();
    try
    {
      ZipExtractResult result = ZipStreamExtractor.ExtractToDirectory(archive, entries, dest);

      Assert.Equal(ZipExtractResult.Ok, result);
      Assert.Equal(payload, File.ReadAllBytes(Path.Combine(dest, "a.bin")));
    }
    finally
    {
      TryDelete(dest);
    }
  }
}
