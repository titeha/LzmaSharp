using System.Security.Cryptography;

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
