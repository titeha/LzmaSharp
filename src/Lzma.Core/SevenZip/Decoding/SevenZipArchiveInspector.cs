namespace Lzma.Core.SevenZip;

/// <summary>
/// Диагностика архива: перечисляет методы (кодеки/фильтры/шифрование), которые в нём
/// используются. Помогает понять, почему архив не распаковывается (какой именно метод
/// или комбинация не поддержаны), не выполняя саму распаковку.
/// </summary>
public static class SevenZipArchiveInspector
{
  /// <summary>
  /// Пытается перечислить методы архива. Возвращает <see langword="true"/>, если заголовок
  /// разобран и методы перечислены; иначе <see langword="false"/> и текст-причину (например,
  /// «заголовок не разобран» — типично для многотомных/обрезанных архивов или неверного пароля).
  /// </summary>
  public static bool TryDescribeMethods(
      ReadOnlySpan<byte> archive,
      string? password,
      out string description)
  {
    SevenZipPassword? sevenZipPassword = null;

    try
    {
      SevenZipDecodeOptions options = password is null
          ? SevenZipDecodeOptions.Default
          : SevenZipDecodeOptions.WithPassword(sevenZipPassword = SevenZipPassword.FromString(password));

      var reader = new SevenZipArchiveReader();
      SevenZipArchiveReadResult read = reader.Read(archive, options, out _);

      if (read != SevenZipArchiveReadResult.Ok)
      {
        description = $"заголовок не разобран ({read}) — возможно, многотомный/обрезанный архив";
        return false;
      }

      SevenZipHeader? header = reader.Header;
      SevenZipUnpackInfo? unpackInfo = header?.StreamsInfo?.UnpackInfo;

      if (unpackInfo is null || unpackInfo.Folders.Length == 0)
      {
        description = "без сжатых данных (пустой архив)";
        return true;
      }

      var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

      foreach (SevenZipFolder folder in unpackInfo.Folders)
        foreach (SevenZipCoderInfo coder in folder.Coders)
          names.Add(MethodName(coder.MethodId));

      description = string.Join(" + ", names);
      return true;
    }
    finally
    {
      sevenZipPassword?.Dispose();
    }
  }

  private static string MethodName(byte[] id)
  {
    if (id is null || id.Length == 0)
      return "?";

    if (SevenZipCoderMethodIds.IsSingleByteMethodId(id, 0x00)) return "Copy";
    if (SevenZipCoderMethodIds.IsSingleByteMethodId(id, 0x21)) return "LZMA2";
    if (Equals(id, 0x03, 0x01, 0x01)) return "LZMA";
    if (Equals(id, 0x03, 0x04, 0x01)) return "PPMd";
    if (SevenZipCoderMethodIds.IsSingleByteMethodId(id, 0x03)) return "Delta";
    if (SevenZipCoderMethodIds.IsBcj2MethodId(id)) return "BCJ2";
    if (SevenZipCoderMethodIds.IsBcjX86MethodId(id)) return "BCJ(x86)";
    if (SevenZipCoderMethodIds.IsBcjArmMethodId(id)) return "BCJ(ARM)";
    if (SevenZipCoderMethodIds.IsBcjArmtMethodId(id)) return "BCJ(ARMT)";
    if (SevenZipCoderMethodIds.IsBcjArm64MethodId(id)) return "BCJ(ARM64)";
    if (SevenZipCoderMethodIds.IsBcjPpcMethodId(id)) return "BCJ(PPC)";
    if (SevenZipCoderMethodIds.IsBcjSparcMethodId(id)) return "BCJ(SPARC)";
    if (SevenZipCoderMethodIds.IsBcjIa64MethodId(id)) return "BCJ(IA64)";
    if (SevenZipCoderMethodIds.IsSwap2MethodId(id)) return "Swap2";
    if (SevenZipCoderMethodIds.IsSwap4MethodId(id)) return "Swap4";
    if (SevenZipCoderMethodIds.IsDeflateMethodId(id)) return "Deflate";
    if (SevenZipCoderMethodIds.IsDeflate64MethodId(id)) return "Deflate64";
    if (SevenZipCoderMethodIds.IsBZip2MethodId(id)) return "BZip2";
    if (SevenZipAesCoder.IsAesMethodId(id)) return "AES-256";
    if (SevenZipGostCoder.IsKuznyechikMethodId(id)) return "ГОСТ-Кузнечик";
    if (SevenZipGostCoder.IsMagmaMethodId(id)) return "ГОСТ-Магма";

    return Convert.ToHexString(id);
  }

  private static bool Equals(byte[] id, params byte[] expected)
      => id.Length == expected.Length && id.AsSpan().SequenceEqual(expected);
}
