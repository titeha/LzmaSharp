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
        || options.Password is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (entries.Count == 0)
      return BuildEmptyArchive(out archive);

    if (!TryValidateWriterEntries(entries))
      return SevenZipArchiveWriteResult.InvalidData;

    if (!TryGetGostCipherParameters(options.Cipher, out byte[] methodId, out int requiredIvSize))
      return SevenZipArchiveWriteResult.NotSupported;

    if (!SevenZipGostCoder.IsSupportedNumCyclesPower(options.NumCyclesPower))
      return SevenZipArchiveWriteResult.NotSupported;

    // Соль и IV: при отсутствии генерируем криптослучайно; явно заданные — как есть.
    byte[] salt = options.Salt ?? RandomNumberGenerator.GetBytes(SevenZipGostEncryptionOptions.DefaultSaltSize);
    byte[] initializationVector = options.InitializationVector ?? RandomNumberGenerator.GetBytes(requiredIvSize);

    if (salt.Length > SevenZipGostCoder.MaxSaltSize)
      return SevenZipArchiveWriteResult.InvalidData;

    if (initializationVector.Length != requiredIvSize)
      return SevenZipArchiveWriteResult.InvalidData;

    // Без файловых данных шифровать нечего: пишем архив только из empty entries.
    if (AllEntriesHaveNoContent(entries))
      return BuildEmptyEntriesArchive(entries, out archive);

    // Формирование ключа не зависит от IV (использует соль/пароль/numCyclesPower),
    // поэтому ключ выводим один раз; уникальность IV обеспечиваем на каждый поток.
    var keyProperties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: options.NumCyclesPower,
        salt: salt,
        initializationVector: initializationVector);

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
    bool directKey = options.NumCyclesPower == SevenZipGostCoder.DirectKeyNumCyclesPower;

    try
    {
      bool derived = directKey
          ? SevenZipGostKeyDerivation.TryDeriveDirectKey(keyProperties, options.Password, key)
          : SevenZipGostKeyDerivation.TryDeriveStribogKey(keyProperties, options.Password, key);

      if (!derived)
        return SevenZipArchiveWriteResult.InvalidData;

      if (options.CompressWithLzma2)
      {
        // Цепочка LZMA2 → ГОСТ пока только для одного непустого файла.
        if (CountNonEmptyFiles(entries) > 1)
          return SevenZipArchiveWriteResult.NotSupported;

        if (!SevenZipGostCoder.TrySerializeProperties(keyProperties, out byte[] lzma2GostPropertyBytes))
          return SevenZipArchiveWriteResult.InvalidData;

        byte[] lzma2GostCoderBytes = BuildGostCoderBytes(methodId, lzma2GostPropertyBytes);

        return BuildGostLzma2EntriesArchive(
            entries, methodId, keyProperties, lzma2GostCoderBytes, key, out archive);
      }

      return BuildGostMultiFileArchive(
          entries,
          methodId,
          numCyclesPower: options.NumCyclesPower,
          salt: salt,
          baseInitializationVector: initializationVector,
          key,
          out archive);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
    }
  }

  /// <summary>
  /// Строит архив, шифруя каждый непустой файл отдельным folder-ом с собственным ГОСТ-coder-ом.
  /// Ключ общий, а IV у каждого потока свой (база + индекс потока), что безопасно для CTR.
  /// </summary>
  /// <remarks>
  /// Замечание про Магму: при IV в 4 байта блок-счётчик занимает только младшие 4 байта,
  /// поэтому пространства гаммы соседних потоков (IV и IV+1) расходятся лишь на 2^32 блока
  /// (≈32 ГБ). Для одного файла больше этого размера потоки могли бы пересечься — на практике
  /// нереалистично, но это ограничение текущей схемы.
  /// </remarks>
  private static SevenZipArchiveWriteResult BuildGostMultiFileArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      byte[] methodId,
      byte numCyclesPower,
      byte[] salt,
      byte[] baseInitializationVector,
      ReadOnlySpan<byte> key,
      out byte[] archive)
  {
    archive = [];

    int count = CountNonEmptyFiles(entries);
    var packSizes = new int[count];
    var unpackSizes = new int[count];
    var unpackCrcs = new uint[count];
    var coderBytesPerFolder = new byte[count][];
    var encryptedStreams = new List<byte[]>(count);

    long totalLength = 0;
    int streamIndex = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!IsNonEmptyFile(entry))
        continue;

      if (!TryDeriveStreamInitializationVector(baseInitializationVector, streamIndex, out byte[] iv))
        return SevenZipArchiveWriteResult.NotSupported;

      var properties = new SevenZipGostProperties(
          version: SevenZipGostCoder.CurrentPropertiesVersion,
          flags: 0,
          numCyclesPower: numCyclesPower,
          salt: salt,
          initializationVector: iv);

      if (!SevenZipGostCoder.TrySerializeProperties(properties, out byte[] propertyBytes))
        return SevenZipArchiveWriteResult.InternalError;

      coderBytesPerFolder[streamIndex] = BuildGostCoderBytes(methodId, propertyBytes);

      SevenZipGostEncryptResult encryptResult = SevenZipGostPackedStreamEncryptor.TryEncrypt(
          methodId, properties, key, entry.Content, out byte[] ciphertext);

      if (encryptResult != SevenZipGostEncryptResult.Ok)
        return SevenZipArchiveWriteResult.InternalError;

      encryptedStreams.Add(ciphertext);
      packSizes[streamIndex] = ciphertext.Length;
      unpackSizes[streamIndex] = entry.Content.Length;
      unpackCrcs[streamIndex] = Crc32.Compute(entry.Content);

      totalLength += ciphertext.Length;
      if (totalLength > int.MaxValue)
        return SevenZipArchiveWriteResult.InternalError;

      streamIndex++;
    }

    byte[] packedData = new byte[(int)totalLength];
    int outputOffset = 0;
    for (int i = 0; i < encryptedStreams.Count; i++)
    {
      encryptedStreams[i].CopyTo(packedData.AsSpan(outputOffset));
      outputOffset += encryptedStreams[i].Length;
    }

    if (!TryBuildGostMultiFileNextHeader(
        entries, packSizes, unpackSizes, unpackCrcs, coderBytesPerFolder, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(packedData, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>Строит next header для multi-file ГОСТ-сценария (по folder-у на файл).</summary>
  private static bool TryBuildGostMultiFileNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      int[] packSizes,
      int[] unpackSizes,
      uint[] unpackCrcs,
      byte[][] coderBytesPerFolder,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(256)
    {
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
    };

    if (!TryWriteCompressedStreamsPackInfo(header, packSizes))
      return false;

    if (!TryWriteGostMultiFolderUnpackInfo(header, unpackSizes, unpackCrcs, coderBytesPerFolder))
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

  /// <summary>Пишет UnpackInfo, где у каждого folder-а собственный ГОСТ-coder (свой IV).</summary>
  private static bool TryWriteGostMultiFolderUnpackInfo(
      List<byte> header,
      int[] unpackSizes,
      uint[] unpackCrcs,
      byte[][] coderBytesPerFolder)
  {
    header.Add(SevenZipNid.UnpackInfo);

    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, (ulong)unpackSizes.Length))
      return false;

    header.Add(0x00);

    for (int i = 0; i < unpackSizes.Length; i++)
    {
      if (!TryWriteUInt64(header, 1)) // один coder на folder
        return false;

      header.AddRange(coderBytesPerFolder[i]);
    }

    header.Add(SevenZipNid.CodersUnpackSize);

    for (int i = 0; i < unpackSizes.Length; i++)
    {
      if (!TryWriteUInt64(header, (ulong)unpackSizes[i]))
        return false;
    }

    header.Add(SevenZipNid.Crc);
    WriteAllDefinedCrcDigests(header, unpackCrcs);

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Строит IV потока как (база + индекс) в big-endian. Возвращает <see langword="false"/>,
  /// если сложение выходит за разрядность поля IV (на практике недостижимо).
  /// </summary>
  private static bool TryDeriveStreamInitializationVector(
      byte[] baseInitializationVector,
      int streamIndex,
      out byte[] initializationVector)
  {
    initializationVector = (byte[])baseInitializationVector.Clone();

    ulong addend = (ulong)streamIndex;
    int position = initializationVector.Length - 1;

    while (addend != 0)
    {
      if (position < 0)
      {
        initializationVector = [];
        return false;
      }

      ulong sum = (ulong)initializationVector[position] + (addend & 0xFF);
      initializationVector[position] = (byte)(sum & 0xFF);
      addend = (addend >> 8) + (sum >> 8);
      position--;
    }

    return true;
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
