using System.Security.Cryptography;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

// Экспериментальный ГОСТ-writer: шифрует упакованный поток Кузнечиком или Магмой в режиме
// CTR (один folder = один GOST coder). Контейнерная обвязка переиспользует общий сжатый
// путь (SevenZipArchiveWriter.Compressed.cs): "encode" здесь — это шифрование, а не сжатие.
public static partial class SevenZipArchiveWriter
{
  /// <summary>
  /// Строит 7z-архив, зашифрованный экспериментальным ГОСТ-кодером (без сжатия).
  /// </summary>
  /// <remarks>
  /// Сейчас поддержан не более чем один непустой файл на архив: режим CTR требует
  /// уникального IV на поток, а текущий контейнерный путь использует общие байты coder-а
  /// (значит, общий IV) для всех folder-ов. Несколько непустых файлов вернут
  /// <see cref="SevenZipArchiveWriteResult.NotSupported"/>.
  /// </remarks>
  /// <param name="entries">Элементы архива.</param>
  /// <param name="options">Параметры ГОСТ-шифрования.</param>
  /// <param name="archive">Построенный архив при успешном результате.</param>
  /// <returns>Результат построения архива.</returns>
  public static SevenZipArchiveWriteResult BuildGostEncryptedArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      SevenZipGostEncryptionOptions options,
      out byte[] archive)
  {
    archive = [];

    if (entries is null
        || options is null
        || options.Password is null
        || options.Salt is null
        || options.InitializationVector is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (entries.Count == 0)
      return BuildEmptyArchive(out archive);

    if (!TryValidateWriterEntries(entries))
      return SevenZipArchiveWriteResult.InvalidData;

    if (!TryGetGostCipherParameters(options.Cipher, out byte[] methodId, out int requiredIvSize))
      return SevenZipArchiveWriteResult.NotSupported;

    if (!SevenZipGostCoder.IsSupportedNumCyclesPower(options.NumCyclesPower))
      return SevenZipArchiveWriteResult.NotSupported;

    if (options.Salt.Length > SevenZipGostCoder.MaxSaltSize)
      return SevenZipArchiveWriteResult.InvalidData;

    if (options.InitializationVector.Length != requiredIvSize)
      return SevenZipArchiveWriteResult.InvalidData;

    // Без файловых данных шифровать нечего: пишем архив только из empty entries.
    if (AllEntriesHaveNoContent(entries))
      return BuildEmptyEntriesArchive(entries, out archive);

    // Несколько непустых файлов потребовали бы свой IV на поток — пока не поддержано.
    if (CountNonEmptyFiles(entries) > 1)
      return SevenZipArchiveWriteResult.NotSupported;

    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: options.NumCyclesPower,
        salt: options.Salt,
        initializationVector: options.InitializationVector);

    if (!SevenZipGostCoder.TrySerializeProperties(properties, out byte[] propertyBytes))
      return SevenZipArchiveWriteResult.InvalidData;

    byte[] coderBytes = BuildGostCoderBytes(methodId, propertyBytes);

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
    bool directKey = options.NumCyclesPower == SevenZipGostCoder.DirectKeyNumCyclesPower;

    try
    {
      bool derived = directKey
          ? SevenZipGostKeyDerivation.TryDeriveDirectKey(properties, options.Password, key)
          : SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, options.Password, key);

      if (!derived)
        return SevenZipArchiveWriteResult.InvalidData;

      if (options.CompressWithLzma2)
        return BuildGostLzma2EntriesArchive(entries, methodId, properties, coderBytes, key, out archive);

      bool encryptFailed = false;

      byte[] Encrypt(byte[] content)
      {
        SevenZipGostEncryptResult result = SevenZipGostPackedStreamEncryptor.TryEncrypt(
            methodId, properties, key, content, out byte[] ciphertext);

        if (result != SevenZipGostEncryptResult.Ok)
        {
          encryptFailed = true;
          return [];
        }

        return ciphertext;
      }

      SevenZipArchiveWriteResult writeResult = BuildCompressedEntriesArchive(
          entries, Encrypt, coderBytes, out archive);

      if (encryptFailed)
      {
        archive = [];
        return SevenZipArchiveWriteResult.InternalError;
      }

      return writeResult;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
    }
  }

  /// <summary>
  /// Строит архив из одного непустого файла, сжатого LZMA2 и затем зашифрованного ГОСТ-ом
  /// (folder из двух coder-ов: GOST → LZMA2 в порядке декодирования).
  /// </summary>
  private static SevenZipArchiveWriteResult BuildGostLzma2EntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      byte[] gostMethodId,
      SevenZipGostProperties properties,
      byte[] gostCoderBytes,
      ReadOnlySpan<byte> key,
      out byte[] archive)
  {
    archive = [];

    byte[]? content = null;

    for (int i = 0; i < entries.Count; i++)
    {
      if (IsNonEmptyFile(entries[i]))
      {
        content = entries[i].Content;
        break;
      }
    }

    if (content is null)
      return SevenZipArchiveWriteResult.InternalError;

    if (!Lzma2Properties.TryEncode(Lzma2DictionarySize, out byte lzma2PropertiesByte))
      return SevenZipArchiveWriteResult.InternalError;

    var lzmaProperties = new LzmaProperties(3, 0, 2);
    byte[] lzma2Packed = Lzma2LzmaEncoder.Encode(content, lzmaProperties, Lzma2DictionarySize);

    SevenZipGostEncryptResult encryptResult = SevenZipGostPackedStreamEncryptor.TryEncrypt(
        gostMethodId, properties, key, lzma2Packed, out byte[] encrypted);

    if (encryptResult != SevenZipGostEncryptResult.Ok)
      return SevenZipArchiveWriteResult.InternalError;

    uint contentCrc = Crc32.Compute(content);

    if (!TryBuildGostLzma2NextHeader(
        entries,
        packSize: encrypted.Length,
        gostUnpackSize: lzma2Packed.Length,
        finalUnpackSize: content.Length,
        finalCrc: contentCrc,
        gostCoderBytes: gostCoderBytes,
        lzma2PropertiesByte: lzma2PropertiesByte,
        out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(encrypted, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>Строит next header для сценария LZMA2 → ГОСТ (folder из двух coder-ов).</summary>
  private static bool TryBuildGostLzma2NextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      int packSize,
      int gostUnpackSize,
      int finalUnpackSize,
      uint finalCrc,
      byte[] gostCoderBytes,
      byte lzma2PropertiesByte,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(256)
    {
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
    };

    if (!TryWriteCompressedStreamsPackInfo(header, [packSize]))
      return false;

    if (!TryWriteGostLzma2FolderUnpackInfo(
        header, gostCoderBytes, lzma2PropertiesByte, gostUnpackSize, finalUnpackSize, finalCrc))
      return false;

    header.Add(SevenZipNid.End);

    if (AllEntriesAreNonEmptyFiles(entries))
    {
      if (!TryWriteAllNonEmptyCopyEntriesFilesInfo(header, entries))
        return false;
    }
    else if (!TryWriteMixedCopyEntriesFilesInfo(header, entries))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Пишет UnpackInfo для одного folder-а из двух coder-ов: GOST (coder 0) и LZMA2 (coder 1),
  /// связанных bind pair-ом GOST.out0 → LZMA2.in1. Порядок раскодирования: packed →
  /// GOST расшифровывает → LZMA2 распаковывает → финальный выход.
  /// </summary>
  private static bool TryWriteGostLzma2FolderUnpackInfo(
      List<byte> header,
      byte[] gostCoderBytes,
      byte lzma2PropertiesByte,
      int gostUnpackSize,
      int finalUnpackSize,
      uint finalCrc)
  {
    header.Add(SevenZipNid.UnpackInfo);

    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, 1)) // один folder
      return false;

    header.Add(0x00); // external = 0, folder-ы заданы по месту

    if (!TryWriteUInt64(header, 2)) // два coder-а
      return false;

    // coder 0: ГОСТ (id size | hasProperties) + method id + размер + properties.
    header.AddRange(gostCoderBytes);

    // coder 1: LZMA2 — flags 0x21 (idSize 1 | attributes 0x20), method id 0x21,
    // размер properties = 1, properties = байт размера словаря.
    header.Add(0x21);
    header.Add(Lzma2MethodId);
    header.Add(0x01);
    header.Add(lzma2PropertiesByte);

    // Bind pair (число = coders - 1 = 1, не пишется): InIndex = 1 (вход LZMA2),
    // OutIndex = 0 (выход ГОСТ). Единственный packed stream идёт на свободный вход
    // ГОСТ (in0) — индексы packed stream-ов при одном потоке не пишутся.
    if (!TryWriteUInt64(header, 1))
      return false;

    if (!TryWriteUInt64(header, 0))
      return false;

    header.Add(SevenZipNid.CodersUnpackSize);

    if (!TryWriteUInt64(header, (ulong)gostUnpackSize)) // выход coder 0 (ГОСТ)
      return false;

    if (!TryWriteUInt64(header, (ulong)finalUnpackSize)) // выход coder 1 (LZMA2)
      return false;

    header.Add(SevenZipNid.Crc);
    WriteAllDefinedCrcDigests(header, [finalCrc]);

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Возвращает method id и требуемый размер IV для выбранного ГОСТ-шифра.
  /// </summary>
  private static bool TryGetGostCipherParameters(
      SevenZipGostCipher cipher,
      out byte[] methodId,
      out int requiredIvSize)
  {
    switch (cipher)
    {
      case SevenZipGostCipher.Kuznyechik:
        methodId = SevenZipGostCoder.KuznyechikMethodId.ToArray();
        requiredIvSize = SevenZipGostInitializationVector.KuznyechikCtrInitializationVectorSize;
        return true;

      case SevenZipGostCipher.Magma:
        methodId = SevenZipGostCoder.MagmaMethodId.ToArray();
        requiredIvSize = SevenZipGostInitializationVector.MagmaCtrInitializationVectorSize;
        return true;

      default:
        methodId = [];
        requiredIvSize = 0;
        return false;
    }
  }

  /// <summary>
  /// Строит байты ГОСТ-coder-а: flags (idSize | hasProperties) + method id + размер
  /// properties + properties.
  /// </summary>
  private static byte[] BuildGostCoderBytes(byte[] methodId, byte[] propertyBytes)
  {
    // Простой coder (1 вход/1 выход), есть properties → бит 0x20; idSize в младших 4 битах.
    byte mainByte = (byte)((methodId.Length & 0x0F) | 0x20);

    List<byte> coder = new(2 + methodId.Length + propertyBytes.Length)
    {
        mainByte,
    };

    coder.AddRange(methodId);
    TryWriteUInt64(coder, (ulong)propertyBytes.Length);
    coder.AddRange(propertyBytes);

    return [.. coder];
  }
}
