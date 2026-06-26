using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipAesPackedStreamEncryptorTests
{
  private static byte[] DeterministicBytes(int length, byte seed)
  {
    byte[] data = new byte[length];
    for (int i = 0; i < length; i++)
      data[i] = (byte)(i * 31 + seed);
    return data;
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(15)]
  [InlineData(16)]
  [InlineData(17)]
  [InlineData(1000)]
  public void Encrypt_ЗатемDecrypt_ВосстанавливаетОткрытыйТекст(int length)
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 4,
        salt: DeterministicBytes(16, 0x10),
        initializationVector: DeterministicBytes(16, 0x20));

    using SevenZipPassword password = SevenZipPassword.FromString("пароль-AES");

    byte[] plaintext = DeterministicBytes(length, 0x33);

    Assert.Equal(SevenZipAesDecryptResult.Ok, SevenZipAesPackedStreamEncryptor.TryEncrypt(
        properties, password, plaintext, out byte[] ciphertext));

    // Длина шифртекста кратна размеру блока AES.
    Assert.Equal(0, ciphertext.Length % SevenZipAesDecryptor.AesBlockSize);

    Assert.Equal(SevenZipAesDecryptResult.Ok, SevenZipAesPackedStreamDecryptor.TryDecrypt(
        properties, password, ciphertext, out byte[] decryptedPadded));

    // Декодер возвращает дополненный нулями блок; обрезаем до фактической длины.
    Assert.Equal(plaintext, decryptedPadded.AsSpan(0, length).ToArray());
  }

  [Fact]
  public void Encrypt_НеверныйПароль_ДаётДругойОткрытыйТекст()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 4,
        salt: DeterministicBytes(16, 0x40),
        initializationVector: DeterministicBytes(16, 0x50));

    using SevenZipPassword right = SevenZipPassword.FromString("верный");
    using SevenZipPassword wrong = SevenZipPassword.FromString("неверный");

    byte[] plaintext = DeterministicBytes(64, 0x77);

    Assert.Equal(SevenZipAesDecryptResult.Ok, SevenZipAesPackedStreamEncryptor.TryEncrypt(
        properties, right, plaintext, out byte[] ciphertext));

    Assert.Equal(SevenZipAesDecryptResult.Ok, SevenZipAesPackedStreamDecryptor.TryDecrypt(
        properties, wrong, ciphertext, out byte[] decrypted));

    Assert.NotEqual(plaintext, decrypted.AsSpan(0, plaintext.Length).ToArray());
  }
}
