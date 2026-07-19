using System.Security.Cryptography;
using Lzma.Core.Zip;
using Xunit;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Потоковый WinZip-AES write-through (CTR+HMAC на лету): шифртекст и authCode БАЙТ-В-БАЙТ совпадают с
/// одноразовыми WinZipAes.CtrTransform/ComputeAuthenticationCode — доказывает инкрементальную корректность
/// (перенос keystream между кусками произвольного размера).
/// </summary>
public sealed class WinZipAesEncryptWriteStreamTests
{
  private static (byte[] aesKey, byte[] macKey) Keys()
  {
    WinZipAes.DeriveKeys("пароль-тест"u8, new byte[16], WinZipAes.Strength.Aes256, out byte[] aesKey, out byte[] macKey, out _);
    return (aesKey, macKey);
  }

  private static byte[] StreamEncrypt(byte[] plain, int[] chunkSizes, byte[] aesKey, byte[] macKey, out byte[] authCode)
  {
    using var output = new MemoryStream();
    using (var enc = new WinZipAesEncryptWriteStream(output, aesKey, macKey))
    {
      int pos = 0, ci = 0;
      while (pos < plain.Length)
      {
        int size = Math.Min(chunkSizes[ci++ % chunkSizes.Length], plain.Length - pos);
        enc.Write(plain, pos, size);
        pos += size;
      }

      authCode = enc.GetAuthenticationCode();
    }

    return output.ToArray();
  }

  private static void AssertMatches(byte[] plain, int[] chunkSizes)
  {
    (byte[] aesKey, byte[] macKey) = Keys();

    byte[] streamedCipher = StreamEncrypt(plain, chunkSizes, aesKey, macKey, out byte[] streamedAuth);

    byte[] expectedCipher = (byte[])plain.Clone();
    WinZipAes.CtrTransform(aesKey, expectedCipher);
    byte[] expectedAuth = WinZipAes.ComputeAuthenticationCode(macKey, expectedCipher);

    Assert.Equal(expectedCipher, streamedCipher);
    Assert.Equal(expectedAuth, streamedAuth);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(15)]
  [InlineData(16)]
  [InlineData(17)]
  [InlineData(1000)]
  [InlineData(70000)] // > рабочего буфера 64 КБ
  public void ЦелымиКусками(int n)
  {
    var rnd = new Random(n + 1);
    byte[] plain = new byte[n];
    rnd.NextBytes(plain);
    AssertMatches(plain, [n == 0 ? 1 : n]);
  }

  [Fact]
  public void РазнымиКусками_ПереносKeystream()
  {
    var rnd = new Random(99);
    byte[] plain = new byte[200_000];
    rnd.NextBytes(plain);
    // Куски НЕ кратны 16 — стресс переноса неполного блока keystream через границы.
    AssertMatches(plain, [1, 15, 16, 17, 3, 31, 100, 65537]);
  }

  [Fact]
  public void ПотоковыйЧленРаспаковываетсяTryDecrypt()
  {
    var rnd = new Random(7);
    byte[] compressed = new byte[50_000];
    rnd.NextBytes(compressed);

    byte[] salt = new byte[WinZipAes.SaltSize(WinZipAes.Strength.Aes256)];
    rnd.NextBytes(salt);
    WinZipAes.DeriveKeys("s3cret"u8, salt, WinZipAes.Strength.Aes256, out byte[] aesKey, out byte[] macKey, out byte[] pwVerify);

    // Собираем член вручную из потокового шифртекста: [salt][pwVerify][ciphertext][authCode].
    byte[] cipher = StreamEncrypt(compressed, [4096], aesKey, macKey, out byte[] auth);
    byte[] member = [.. salt, .. pwVerify, .. cipher, .. auth];

    Assert.Equal(WinZipAesDecryptResult.Ok,
        WinZipAesMember.TryDecrypt(member, "s3cret"u8, WinZipAes.Strength.Aes256, out byte[] roundTrip));
    Assert.Equal(compressed, roundTrip);
  }
}
