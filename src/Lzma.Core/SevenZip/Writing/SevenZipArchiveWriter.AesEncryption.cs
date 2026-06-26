using System.Security.Cryptography;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

// 7zAES-writer: шифрует каждый непустой файл отдельным folder-ом (AES-256-CBC, SHA-256 KDF),
// опционально предварительно сжимая LZMA2. Один folder = один файл; ключ общий, IV у каждого
// потока свой (база + индекс). Совместимо с настоящим 7-Zip.
public static partial class SevenZipArchiveWriter
{
  // Method id 7zAES: 06 F1 07 01.
  private static readonly byte[] _aesMethodId = [0x06, 0xF1, 0x07, 0x01];

  /// <summary>
  /// Строит 7z-архив, зашифрованный 7zAES (AES-256, SHA-256 KDF), опционально со сжатием LZMA2.
  /// Каждый непустой файл шифруется отдельным folder-ом со своим IV.
  /// </summary>
  public static SevenZipArchiveWriteResult BuildAesEncryptedArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      SevenZipAesEncryptionOptions options,
      out byte[] archive)
  {
    archive = [];

    if (entries is null || options is null || options.Password is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (entries.Count == 0)
      return BuildEmptyArchive(out archive);

    if (!TryValidateWriterEntries(entries))
      return SevenZipArchiveWriteResult.InvalidData;

    if (!SevenZipAesCoder.IsSupportedNumCyclesPower(options.NumCyclesPower))
      return SevenZipArchiveWriteResult.NotSupported;

    byte[] salt = options.Salt ?? RandomNumberGenerator.GetBytes(SevenZipAesEncryptionOptions.DefaultSaltSize);
    byte[] baseInitializationVector = options.InitializationVector
        ?? RandomNumberGenerator.GetBytes(SevenZipAesEncryptionOptions.DefaultInitializationVectorSize);

    if (salt.Length > SevenZipAesCoder.MaxSaltSize)
      return SevenZipArchiveWriteResult.InvalidData;

    // Требуем полный 16-байтовый базовый IV: его же кладём в properties и используем для CBC.
    if (baseInitializationVector.Length != SevenZipAesEncryptionOptions.DefaultInitializationVectorSize)
      return SevenZipArchiveWriteResult.InvalidData;

    if (AllEntriesHaveNoContent(entries))
      return BuildEmptyEntriesArchive(entries, out archive);

    // Ключ зависит от соли/пароля/numCyclesPower (не от IV) — выводим один раз на архив.
    var keyProperties = new SevenZipAesProperties(options.NumCyclesPower, salt, baseInitializationVector);

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    try
    {
      if (!SevenZipAesKeyDerivation.TryDeriveKey(keyProperties, options.Password, key))
        return SevenZipArchiveWriteResult.InvalidData;

      return BuildAesFoldersArchive(
          entries,
          options.NumCyclesPower,
          salt,
          baseInitializationVector,
          options.CompressWithLzma2,
          key,
          out archive);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
    }
  }

  private static SevenZipArchiveWriteResult BuildAesFoldersArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      byte numCyclesPower,
      byte[] salt,
      byte[] baseInitializationVector,
      bool compressWithLzma2,
      ReadOnlySpan<byte> key,
      out byte[] archive)
  {
    archive = [];

    int count = CountNonEmptyFiles(entries);
    var packSizes = new int[count];
    var finalCrcs = new uint[count];
    var folderBodies = new byte[count][];
    var coderUnpackSizes = new int[count][];
    var packedStreams = new List<byte[]>(count);

    byte lzma2PropertiesByte = 0;
    LzmaProperties lzmaProperties = default;

    if (compressWithLzma2)
    {
      if (!Lzma2Properties.TryEncode(Lzma2DictionarySize, out lzma2PropertiesByte))
        return SevenZipArchiveWriteResult.InternalError;

      lzmaProperties = new LzmaProperties(3, 0, 2);
    }

    long totalLength = 0;
    int streamIndex = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!IsNonEmptyFile(entry))
        continue;

      if (!TryDeriveStreamInitializationVector(baseInitializationVector, streamIndex, out byte[] iv))
        return SevenZipArchiveWriteResult.NotSupported;

      var properties = new SevenZipAesProperties(numCyclesPower, salt, iv);

      if (!SevenZipAesCoder.TrySerializeProperties(properties, out byte[] propertyBytes))
        return SevenZipArchiveWriteResult.InternalError;

      byte[] aesCoderBytes = BuildGostCoderBytes(_aesMethodId, propertyBytes); // generic (idSize | 0x20)

      byte[] toEncrypt;

      if (compressWithLzma2)
      {
        byte[] lzma2Packed = Lzma2LzmaEncoder.Encode(entry.Content, lzmaProperties, Lzma2DictionarySize);
        toEncrypt = lzma2Packed;
        folderBodies[streamIndex] = BuildGostLzma2FolderBody(aesCoderBytes, lzma2PropertiesByte);
        coderUnpackSizes[streamIndex] = [lzma2Packed.Length, entry.Content.Length];
      }
      else
      {
        toEncrypt = entry.Content;
        folderBodies[streamIndex] = BuildGostSingleCoderFolderBody(aesCoderBytes);
        coderUnpackSizes[streamIndex] = [entry.Content.Length];
      }

      SevenZipAesDecryptResult encryptResult =
          SevenZipAesPackedStreamEncryptor.TryEncryptWithKey(key, iv, toEncrypt, out byte[] ciphertext);

      if (encryptResult != SevenZipAesDecryptResult.Ok)
        return SevenZipArchiveWriteResult.InternalError;

      packedStreams.Add(ciphertext);
      packSizes[streamIndex] = ciphertext.Length;
      finalCrcs[streamIndex] = Crc32.Compute(entry.Content);

      totalLength += ciphertext.Length;
      if (totalLength > int.MaxValue)
        return SevenZipArchiveWriteResult.InternalError;

      streamIndex++;
    }

    byte[] packedData = new byte[(int)totalLength];
    int outputOffset = 0;
    for (int i = 0; i < packedStreams.Count; i++)
    {
      packedStreams[i].CopyTo(packedData.AsSpan(outputOffset));
      outputOffset += packedStreams[i].Length;
    }

    if (!TryBuildGostFoldersNextHeader(
            entries, packSizes, folderBodies, coderUnpackSizes, finalCrcs, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(packedData, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }
}
