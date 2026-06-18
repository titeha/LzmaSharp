namespace Lzma.Core.SevenZip;

/// <summary>
/// Предикаты распознавания method id coder-ов 7z.
/// </summary>
/// <remarks>
/// 7z допускает как «короткие», так и «длинные» идентификаторы для одного метода
/// (см. Methods.txt из 7-Zip). Эти хелперы инкапсулируют сравнение.
/// </remarks>
internal static class SevenZipCoderMethodIds
{
  public static bool IsBcj2MethodId(byte[] methodId)
  {
    // BCJ2 обычно 03 03 01 1B, иногда короткий 1B.
    return
      methodId.Length == 1 && methodId[0] == 0x1B ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x01 &&
      methodId[3] == 0x1B;
  }

  public static bool IsSingleByteMethodId(byte[] methodId, byte expected)
      => methodId.Length == 1 && methodId[0] == expected;

  public static bool IsSwap2MethodId(byte[] methodId)
  {
    return methodId.Length == 3
      && methodId[0] == 0x02
      && methodId[1] == 0x03
      && methodId[2] == 0x02;
  }

  public static bool IsSwap4MethodId(byte[] methodId)
  {
    return methodId.Length == 3
      && methodId[0] == 0x02
      && methodId[1] == 0x03
      && methodId[2] == 0x04;
  }

  public static bool IsBcjArmMethodId(byte[] methodId)
  {
    // Methods.txt:
    // 07 - ARM (little-endian)
    // 03 03 05 01 - 7z Branch Codecs / ARM (little-endian)
    return
      methodId.Length == 1 && methodId[0] == 0x07 ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x05 &&
      methodId[3] == 0x01;
  }

  public static bool IsBcjX86MethodId(byte[] methodId)
  {
    // В 7z часто используется "длинный" ID для BCJ: { 03 03 01 03 }.
    // Иногда может встретиться и короткий ID: { 04 }.
    return
      methodId.Length == 1 && methodId[0] == 0x04 ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x01 &&
      methodId[3] == 0x03;
  }

  public static bool IsBcjArmtMethodId(byte[] methodId)
  {
    // Methods.txt:
    // 08 - ARMT (little-endian)
    // 03 03 07 01 - 7z Branch Codecs / ARMT (little-endian)
    return
      (methodId.Length == 1 && methodId[0] == 0x08) ||
      (methodId.Length == 4 &&
       methodId[0] == 0x03 &&
       methodId[1] == 0x03 &&
       methodId[2] == 0x07 &&
       methodId[3] == 0x01);
  }

  public static bool IsBcjPpcMethodId(byte[] methodId)
  {
    // Methods.txt:
    // 05 - PPC (big-endian)
    // 03 03 02 05 - 7z Branch Codecs / PPC (big-endian)
    return
      methodId.Length == 1 && methodId[0] == 0x05 ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x02 &&
      methodId[3] == 0x05;
  }

  public static bool IsBcjSparcMethodId(byte[] methodId)
  {
    // Methods.txt:
    // 09 - SPARC
    // 03 03 08 05 - 7z Branch Codecs / SPARC
    return
      methodId.Length == 1 && methodId[0] == 0x09 ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x08 &&
      methodId[3] == 0x05;
  }

  public static bool IsBcjIa64MethodId(byte[] methodId)
  {
    // Methods.txt:
    // 06 - IA64
    // 03 03 04 01 - 7z Branch Codecs / IA64
    return
      (methodId.Length == 1 && methodId[0] == 0x06) ||
      (methodId.Length == 4 &&
       methodId[0] == 0x03 &&
       methodId[1] == 0x03 &&
       methodId[2] == 0x04 &&
       methodId[3] == 0x01);
  }

  public static bool IsBcjArm64MethodId(byte[] methodId)
  {
    // Methods.txt:
    // 0A - ARM64
    return methodId.Length == 1 && methodId[0] == 0x0A;
  }

  public static bool IsDeflateMethodId(byte[] methodId)
  {
    // Methods.txt: 04.. (Misc) / 01 (Zip) / 08 (Deflate) => { 04 01 08 }.
    return methodId.Length == 3 && methodId[0] == 0x04 && methodId[1] == 0x01 && methodId[2] == 0x08;
  }

  public static bool IsBZip2MethodId(byte[] methodId)
  {
    // Methods.txt: 04.. (Misc) / 02 (BZip2) / 02 => { 04 02 02 }.
    return methodId.Length == 3 && methodId[0] == 0x04 && methodId[1] == 0x02 && methodId[2] == 0x02;
  }

  public static bool IsDeflate64MethodId(byte[] methodId)
  {
    // Methods.txt: 04.. [Zip] / 01 / 09 => { 04 01 09 }.
    return methodId.Length == 3
        && methodId[0] == 0x04
        && methodId[1] == 0x01
        && methodId[2] == 0x09;
  }
}
