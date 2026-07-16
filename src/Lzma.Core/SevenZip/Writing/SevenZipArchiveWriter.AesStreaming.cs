using System.Security.Cryptography;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

// Потоковый 7zAES-writer: пофайлово (folder на файл) сжимает LZMA2 и шифрует AES-256, не держа
// весь набор/архив в памяти (файл <= 2 ГиБ). Ключ общий на архив (KDF), IV у каждого файла свой
// (база + индекс) — как в in-memory BuildAesEncryptedArchive; совместимо с настоящим 7-Zip.
public static partial class SevenZipArchiveWriter
{
  /// <summary>
  /// ПОТОКОВОЕ создание 7zAES-архива: каждый непустой файл сжимается (LZMA2, если
  /// <see cref="SevenZipAesEncryptionOptions.CompressWithLzma2"/>) и шифруется отдельным folder-ом
  /// со своим IV. Ключ выводится один раз на архив.
  /// </summary>
  public static SevenZipArchiveWriteResult BuildAesArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      System.IO.Stream output,
      SevenZipAesEncryptionOptions options,
      int dictionarySize,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      IProgress<SevenZipCompressionFileProgress>? currentFile = null)
  {
    if (options is null || options.Password is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (!SevenZipAesCoder.IsSupportedNumCyclesPower(options.NumCyclesPower))
      return SevenZipArchiveWriteResult.NotSupported;

    byte[] salt = options.Salt ?? RandomNumberGenerator.GetBytes(SevenZipAesEncryptionOptions.DefaultSaltSize);
    byte[] baseIv = options.InitializationVector
        ?? RandomNumberGenerator.GetBytes(SevenZipAesEncryptionOptions.DefaultInitializationVectorSize);

    if (salt.Length > SevenZipAesCoder.MaxSaltSize)
      return SevenZipArchiveWriteResult.InvalidData;
    if (baseIv.Length != SevenZipAesEncryptionOptions.DefaultInitializationVectorSize)
      return SevenZipArchiveWriteResult.InvalidData;

    bool compress = options.CompressWithLzma2;

    byte lzma2PropertiesByte = 0;
    int effectiveDictionarySize = 0;
    LzmaProperties lzmaProperties = default;
    if (compress)
    {
      if (dictionarySize <= 0)
        return SevenZipArchiveWriteResult.InvalidData;
      if (!Lzma2Properties.TryCreateFromDictionarySize((uint)dictionarySize, out Lzma2Properties properties))
        return SevenZipArchiveWriteResult.InvalidData;
      if (!properties.TryGetDictionarySizeInt32(out effectiveDictionarySize))
        return SevenZipArchiveWriteResult.NotSupported;

      lzma2PropertiesByte = properties.DictionaryProp;
      lzmaProperties = new LzmaProperties(3, 0, 2);
    }

    // Ключ зависит от соли/пароля/numCyclesPower (не от IV) — выводим один раз на архив.
    var keyProperties = new SevenZipAesProperties(options.NumCyclesPower, salt, baseIv);
    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    try
    {
      if (!SevenZipAesKeyDerivation.TryDeriveKey(keyProperties, options.Password, key))
        return SevenZipArchiveWriteResult.InvalidData;

      int fileIndex = 0;
      return BuildPerFileStreamingArchiveToStream(entries, output, data =>
      {
        StreamingEncodedFile? encoded = EncodeAesStreaming(
            data, key, salt, options.NumCyclesPower, baseIv, fileIndex,
            compress, lzmaProperties, effectiveDictionarySize, lzma2PropertiesByte);
        fileIndex++;
        return encoded;
      }, progress, token, currentFile);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
    }
  }

  // Пофайловое AES-кодирование для потокового folder-а: (опц.) LZMA2 → AES. Packed = шифртекст;
  // тело folder-а [LZMA2→AES] или [AES]; размеры выходов coder-ов; IV = база + индекс файла.
  private static StreamingEncodedFile? EncodeAesStreaming(
      byte[] data,
      byte[] key,
      byte[] salt,
      byte numCyclesPower,
      byte[] baseIv,
      int fileIndex,
      bool compress,
      LzmaProperties lzmaProperties,
      int effectiveDictionarySize,
      byte lzma2PropertiesByte)
  {
    if (!TryDeriveStreamInitializationVector(baseIv, fileIndex, out byte[] iv))
      return null;

    var properties = new SevenZipAesProperties(numCyclesPower, salt, iv);
    if (!SevenZipAesCoder.TrySerializeProperties(properties, out byte[] propertyBytes))
      return null;

    byte[] aesCoderBytes = BuildGostCoderBytes(_aesMethodId, propertyBytes);

    byte[] toEncrypt;
    byte[] folderBody;
    ulong[] coderUnpackSizes;

    if (compress)
    {
      byte[] lzma2Packed = Lzma2LzmaEncoder.Encode(data, lzmaProperties, effectiveDictionarySize);
      toEncrypt = lzma2Packed;
      folderBody = BuildGostLzma2FolderBody(aesCoderBytes, lzma2PropertiesByte);
      coderUnpackSizes = [(ulong)lzma2Packed.Length, (ulong)data.Length];
    }
    else
    {
      toEncrypt = data;
      folderBody = BuildGostSingleCoderFolderBody(aesCoderBytes);
      coderUnpackSizes = [(ulong)data.Length];
    }

    if (SevenZipAesPackedStreamEncryptor.TryEncryptWithKey(key, iv, toEncrypt, out byte[] ciphertext) != SevenZipAesDecryptResult.Ok)
      return null;

    return new StreamingEncodedFile([ciphertext], folderBody, coderUnpackSizes, "AES");
  }
}
