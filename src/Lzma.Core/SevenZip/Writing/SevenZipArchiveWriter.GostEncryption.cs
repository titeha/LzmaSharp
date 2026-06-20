using System.Security.Cryptography;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

// Экспериментальный ГОСТ-writer: шифрует каждый непустой файл отдельным folder-ом
// Кузнечиком или Магмой в режиме CTR, опционально предварительно сжимая LZMA2.
// Один folder = один файл; ключ общий, IV у каждого потока свой (база + индекс).
public static partial class SevenZipArchiveWriter
{
  /// <summary>
  /// Строит 7z-архив, зашифрованный экспериментальным ГОСТ-кодером (опционально со сжатием
  /// LZMA2). Каждый непустой файл шифруется отдельным folder-ом со своим IV.
  /// </summary>
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

      return BuildGostFoldersArchive(
          entries,
          methodId,
          numCyclesPower: options.NumCyclesPower,
          salt: salt,
          baseInitializationVector: initializationVector,
          compressWithLzma2: options.CompressWithLzma2,
          key,
          out archive);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
    }
  }

  /// <summary>
  /// Строит архив, в котором каждый непустой файл — отдельный folder с собственным
  /// ГОСТ-coder-ом (и, при сжатии, дополнительным LZMA2-coder-ом). Ключ общий, IV у каждого
  /// потока свой (база + индекс потока), что безопасно для режима CTR.
  /// </summary>
  /// <remarks>
  /// Замечание про Магму: при IV в 4 байта блок-счётчик занимает только младшие 4 байта,
  /// поэтому пространства гаммы соседних потоков (IV и IV+1) расходятся лишь на 2^32 блока
  /// (≈32 ГБ). Для одного файла больше этого размера потоки могли бы пересечься — на практике
  /// нереалистично, но это ограничение текущей схемы.
  /// </remarks>
  private static SevenZipArchiveWriteResult BuildGostFoldersArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      byte[] methodId,
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

      var properties = new SevenZipGostProperties(
          version: SevenZipGostCoder.CurrentPropertiesVersion,
          flags: 0,
          numCyclesPower: numCyclesPower,
          salt: salt,
          initializationVector: iv);

      if (!SevenZipGostCoder.TrySerializeProperties(properties, out byte[] propertyBytes))
        return SevenZipArchiveWriteResult.InternalError;

      byte[] gostCoderBytes = BuildGostCoderBytes(methodId, propertyBytes);

      byte[] toEncrypt;

      if (compressWithLzma2)
      {
        byte[] lzma2Packed = Lzma2LzmaEncoder.Encode(entry.Content, lzmaProperties, Lzma2DictionarySize);
        toEncrypt = lzma2Packed;
        folderBodies[streamIndex] = BuildGostLzma2FolderBody(gostCoderBytes, lzma2PropertiesByte);
        coderUnpackSizes[streamIndex] = [lzma2Packed.Length, entry.Content.Length];
      }
      else
      {
        toEncrypt = entry.Content;
        folderBodies[streamIndex] = BuildGostSingleCoderFolderBody(gostCoderBytes);
        coderUnpackSizes[streamIndex] = [entry.Content.Length];
      }

      SevenZipGostEncryptResult encryptResult = SevenZipGostPackedStreamEncryptor.TryEncrypt(
          methodId, properties, key, toEncrypt, out byte[] ciphertext);

      if (encryptResult != SevenZipGostEncryptResult.Ok)
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

  /// <summary>Строит next header для ГОСТ-сценария: по folder-у на каждый непустой файл.</summary>
  private static bool TryBuildGostFoldersNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      int[] packSizes,
      byte[][] folderBodies,
      int[][] coderUnpackSizes,
      uint[] finalCrcs,
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

    if (!TryWriteGostFoldersUnpackInfo(header, folderBodies, coderUnpackSizes, finalCrcs))
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
  /// Пишет UnpackInfo, где у каждого folder-а собственное тело (один или два coder-а) и свои
  /// размеры выходов coder-ов; CRC задаётся по одному на folder (для финального выхода).
  /// </summary>
  private static bool TryWriteGostFoldersUnpackInfo(
      List<byte> header,
      byte[][] folderBodies,
      int[][] coderUnpackSizes,
      uint[] finalCrcs)
  {
    header.Add(SevenZipNid.UnpackInfo);

    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, (ulong)folderBodies.Length))
      return false;

    header.Add(0x00); // external = 0, folder-ы заданы по месту

    for (int i = 0; i < folderBodies.Length; i++)
      header.AddRange(folderBodies[i]);

    header.Add(SevenZipNid.CodersUnpackSize);

    for (int i = 0; i < coderUnpackSizes.Length; i++)
    {
      for (int j = 0; j < coderUnpackSizes[i].Length; j++)
      {
        if (!TryWriteUInt64(header, (ulong)coderUnpackSizes[i][j]))
          return false;
      }
    }

    header.Add(SevenZipNid.Crc);
    WriteAllDefinedCrcDigests(header, finalCrcs);

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>Строит тело folder-а из одного ГОСТ-coder-а (numCoders + сам coder).</summary>
  private static byte[] BuildGostSingleCoderFolderBody(byte[] gostCoderBytes)
  {
    List<byte> body = new(1 + gostCoderBytes.Length);

    TryWriteUInt64(body, 1); // один coder
    body.AddRange(gostCoderBytes);

    return [.. body];
  }

  /// <summary>
  /// Строит тело folder-а из двух coder-ов: ГОСТ (coder 0) и LZMA2 (coder 1), связанных
  /// bind pair-ом ГОСТ.out0 → LZMA2.in1. Порядок раскодирования: packed → ГОСТ
  /// расшифровывает → LZMA2 распаковывает → финальный выход.
  /// </summary>
  private static byte[] BuildGostLzma2FolderBody(byte[] gostCoderBytes, byte lzma2PropertiesByte)
  {
    List<byte> body = new(8 + gostCoderBytes.Length);

    TryWriteUInt64(body, 2); // два coder-а

    // coder 0: ГОСТ.
    body.AddRange(gostCoderBytes);

    // coder 1: LZMA2 — flags 0x21 (idSize 1 | attributes 0x20), method id 0x21,
    // размер properties = 1, properties = байт размера словаря.
    body.Add(0x21);
    body.Add(Lzma2MethodId);
    body.Add(0x01);
    body.Add(lzma2PropertiesByte);

    // Bind pair (число = coders - 1 = 1, не пишется): InIndex = 1 (вход LZMA2),
    // OutIndex = 0 (выход ГОСТ). Единственный packed stream идёт на свободный вход
    // ГОСТ (in0) — индексы packed stream-ов при одном потоке не пишутся.
    TryWriteUInt64(body, 1);
    TryWriteUInt64(body, 0);

    return [.. body];
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
