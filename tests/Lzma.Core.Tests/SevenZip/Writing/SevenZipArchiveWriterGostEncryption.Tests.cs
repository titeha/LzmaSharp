using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip.Writing;

public sealed class SevenZipArchiveWriterGostEncryptionTests
{
  private static SevenZipGostEncryptionOptions KuznyechikDirectKey(SevenZipPassword password)
      => new()
      {
        Cipher = SevenZipGostCipher.Kuznyechik,
        Password = password,
        NumCyclesPower = SevenZipGostCoder.DirectKeyNumCyclesPower,
        Salt = [0xA1, 0xA2],
        InitializationVector = [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0],
      };

  private static SevenZipGostEncryptionOptions MagmaStribogKdf(SevenZipPassword password)
      => new()
      {
        Cipher = SevenZipGostCipher.Magma,
        Password = password,
        NumCyclesPower = 5,
        Salt = [0xB1, 0xB2, 0xB3],
        InitializationVector = [0x10, 0x32, 0x54, 0x76],
      };

  [Fact]
  public void BuildGostEncryptedArchive_КузнечикDirectKey_ОдинФайл_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes("LzmaSharp GOST writer Kuznyechik direct-key round-trip");
    const string fileName = "secret.txt";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry(fileName, content)],
        KuznyechikDirectKey(password),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(content, fileBytes);
  }

  [Fact]
  public void BuildGostEncryptedArchive_МагмаStribogKdf_ОдинФайл_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes("LzmaSharp GOST writer Magma Stribog KDF round-trip\r\n");
    const string fileName = "data.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("пароль");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry(fileName, content)],
        MagmaStribogKdf(password),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(content, fileBytes);
  }

  [Fact]
  public void BuildGostEncryptedArchive_ШифртекстНеСовпадаетСИсходными()
  {
    byte[] content = Encoding.UTF8.GetBytes("plaintext must not appear in the archive body");

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("secret.txt", content)],
        KuznyechikDirectKey(password),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    // Тело архива не должно содержать исходный текст в открытом виде.
    Assert.False(ContainsSubsequence(archive, content));
  }

  [Fact]
  public void BuildGostEncryptedArchive_СНевернымПаролем_ДекодерВозвращаетInvalidData()
  {
    byte[] content = Encoding.UTF8.GetBytes("wrong password should fail CRC after decrypt");

    using SevenZipPassword writePassword = SevenZipPassword.FromString("ab");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("secret.txt", content)],
        KuznyechikDirectKey(writePassword),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("zz");

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        fileBytes: out byte[] fileBytes,
        fileName: out _,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, decodeResult);
    Assert.Empty(fileBytes);
  }

  [Fact]
  public void BuildGostEncryptedArchive_БезПароля_ДекодерВозвращаетNotSupported()
  {
    byte[] content = Encoding.UTF8.GetBytes("needs a password to decode");

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("secret.txt", content)],
        KuznyechikDirectKey(password),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out _,
        fileName: out _,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, decodeResult);
  }

  [Fact]
  public void BuildGostEncryptedArchive_НесколькоФайлов_КузнечикDirectKey_RoundTrip()
  {
    byte[] a = Encoding.UTF8.GetBytes("first file contents");
    byte[] b = Encoding.UTF8.GetBytes("second file — другое содержимое");
    byte[] c = Encoding.UTF8.GetBytes("third");

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [
          new SevenZipArchiveWriterEntry("a.txt", a),
          new SevenZipArchiveWriterEntry("b.txt", b),
          new SevenZipArchiveWriterEntry("c.txt", c),
        ],
        MagmaStribogKdf(password),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(3, files.Length);

    Dictionary<string, byte[]> byName = files.ToDictionary(f => f.Name, f => f.Bytes);
    Assert.Equal(a, byName["a.txt"]);
    Assert.Equal(b, byName["b.txt"]);
    Assert.Equal(c, byName["c.txt"]);
  }

  [Fact]
  public void BuildGostEncryptedArchive_НесколькоФайловСОдинаковымСодержимым_ШифртекстРазный()
  {
    // Одинаковый открытый текст в двух файлах: при своём IV на поток шифртекст должен
    // отличаться (иначе была бы катастрофическая повторная гамма CTR).
    byte[] same = Encoding.UTF8.GetBytes("IDENTICAL CONTENT IN BOTH FILES, MUST ENCRYPT DIFFERENTLY");

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [
          new SevenZipArchiveWriterEntry("a.txt", same),
          new SevenZipArchiveWriterEntry("b.txt", same),
        ],
        KuznyechikDirectKey(password),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    // Два зашифрованных потока идут подряд после сигнатурного заголовка.
    // Их длины равны длине открытого текста (CTR), сравним два соседних блока.
    int start = SevenZipSignatureHeader.TotalSize;
    byte[] stream0 = archive.AsSpan(start, same.Length).ToArray();
    byte[] stream1 = archive.AsSpan(start + same.Length, same.Length).ToArray();
    Assert.NotEqual(stream0, stream1);

    // И при этом оба корректно расшифровываются обратно.
    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(2, files.Length);
    Assert.All(files, f => Assert.Equal(same, f.Bytes));
  }

  [Fact]
  public void BuildGostEncryptedArchive_НесколькоФайловСLzma2_ПокаВозвращаетNotSupported()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    var options = KuznyechikDirectKey(password) with { CompressWithLzma2 = true };

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [
          new SevenZipArchiveWriterEntry("a.txt", Encoding.UTF8.GetBytes("first")),
          new SevenZipArchiveWriterEntry("b.txt", Encoding.UTF8.GetBytes("second")),
        ],
        options,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.NotSupported, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildGostEncryptedArchive_НекорректнаяДлинаIvДляКузнечика_ВозвращаетInvalidData()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    var options = new SevenZipGostEncryptionOptions
    {
      Cipher = SevenZipGostCipher.Kuznyechik,
      Password = password,
      NumCyclesPower = SevenZipGostCoder.DirectKeyNumCyclesPower,
      Salt = [0xA1],
      InitializationVector = [0x12, 0x34, 0x56, 0x78], // 4 байта — для Магмы, не Кузнечика
    };

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("secret.txt", Encoding.UTF8.GetBytes("x"))],
        options,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildGostEncryptedArchive_ТолькоПустыеEntries_RoundTripБезШифрования()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [
          new SevenZipArchiveWriterEntry("empty.txt", []),
          new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
        ],
        KuznyechikDirectKey(password),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(2, entries.Length);
  }

  // ---- Криптослучайные соль/IV (соль/IV не заданы) ----

  [Fact]
  public void BuildGostEncryptedArchive_БезЯвныхСолиИIv_ГенерируетСлучайные_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes("random salt and iv are generated by the writer");
    const string fileName = "secret.txt";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    // Соль и IV не заданы — writer должен сгенерировать их сам (парольный KDF).
    var options = new SevenZipGostEncryptionOptions
    {
      Cipher = SevenZipGostCipher.Kuznyechik,
      Password = password,
      NumCyclesPower = 4,
    };

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry(fileName, content)],
        options,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedName,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(content, fileBytes);
  }

  [Fact]
  public void BuildGostEncryptedArchive_БезЯвныхСолиИIv_ДваВызоваДаютРазныеАрхивы()
  {
    byte[] content = Encoding.UTF8.GetBytes("same input, but random salt/iv must differ between archives");

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    var options = new SevenZipGostEncryptionOptions
    {
      Cipher = SevenZipGostCipher.Kuznyechik,
      Password = password,
      NumCyclesPower = 4,
    };

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("a.txt", content)], options, out byte[] archive1));
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("a.txt", content)], options, out byte[] archive2));

    // Случайные соль/IV → архивы должны различаться (и тело, и заголовок со свойствами).
    Assert.NotEqual(archive1, archive2);

    // Оба корректно расшифровываются.
    foreach (byte[] archive in new[] { archive1, archive2 })
    {
      SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
          archiveBytes: archive,
          options: SevenZipDecodeOptions.WithPassword(password),
          fileBytes: out byte[] fileBytes,
          fileName: out _,
          bytesConsumed: out _);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
      Assert.Equal(content, fileBytes);
    }
  }

  [Fact]
  public void BuildGostEncryptedArchive_БезЯвногоIvНесколькоФайлов_RoundTrip()
  {
    byte[] a = Encoding.UTF8.GetBytes("first");
    byte[] b = Encoding.UTF8.GetBytes("second");

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    var options = new SevenZipGostEncryptionOptions
    {
      Cipher = SevenZipGostCipher.Magma,
      Password = password,
      NumCyclesPower = 5,
    };

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [
          new SevenZipArchiveWriterEntry("a.txt", a),
          new SevenZipArchiveWriterEntry("b.txt", b),
        ],
        options,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Dictionary<string, byte[]> byName = files.ToDictionary(f => f.Name, f => f.Bytes);
    Assert.Equal(a, byName["a.txt"]);
    Assert.Equal(b, byName["b.txt"]);
  }

  // ---- LZMA2 → ГОСТ (сжатие + шифрование) ----

  [Fact]
  public void BuildGostEncryptedArchive_КузнечикDirectKeyСLzma2_ОдинФайл_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("LzmaSharp GOST+LZMA2 compress-then-encrypt round-trip\r\n", 40)));
    const string fileName = "secret.txt";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    var options = KuznyechikDirectKey(password) with { CompressWithLzma2 = true };

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry(fileName, content)],
        options,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(content, fileBytes);

    // Содержимое сильно повторяющееся — после сжатия архив заметно меньше исходных данных,
    // значит LZMA2 действительно отработал (а не просто скопировал).
    Assert.True(archive.Length < content.Length);
  }

  [Fact]
  public void BuildGostEncryptedArchive_МагмаStribogKdfСLzma2_ОдинФайл_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("ГОСТ Магма + LZMA2: сжать, потом зашифровать.\r\n", 30)));
    const string fileName = "данные.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("пароль");

    var options = MagmaStribogKdf(password) with { CompressWithLzma2 = true };

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry(fileName, content)],
        options,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedName,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(content, fileBytes);
  }

  [Fact]
  public void BuildGostEncryptedArchive_Lzma2СНевернымПаролем_ДекодерВозвращаетInvalidData()
  {
    byte[] content = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("wrong password fails CRC after decrypt+decompress\r\n", 20)));

    using SevenZipPassword writePassword = SevenZipPassword.FromString("ab");

    var options = KuznyechikDirectKey(writePassword) with { CompressWithLzma2 = true };

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("secret.txt", content)],
        options,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("zz");

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        fileBytes: out byte[] fileBytes,
        fileName: out _,
        bytesConsumed: out _);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, decodeResult);
    Assert.Empty(fileBytes);
  }

  [Fact]
  public void BuildGostEncryptedArchive_Lzma2_ШифртекстНеСодержитИсходныйТекст()
  {
    byte[] content = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("MARKER plaintext must not survive compress+encrypt\r\n", 10)));

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    var options = KuznyechikDirectKey(password) with { CompressWithLzma2 = true };

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("secret.txt", content)],
        options,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.False(ContainsSubsequence(archive, Encoding.UTF8.GetBytes("MARKER plaintext")));
  }

  // Проверяет, встречается ли needle как непрерывная подпоследовательность в haystack.
  private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
  {
    if (needle.Length == 0 || haystack.Length < needle.Length)
      return false;

    for (int i = 0; i + needle.Length <= haystack.Length; i++)
    {
      if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
        return true;
    }

    return false;
  }
}
