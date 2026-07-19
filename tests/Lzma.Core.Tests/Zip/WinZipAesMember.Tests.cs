using System.Text;
using Lzma.Core.Zip;
using Xunit;

namespace Lzma.Core.Tests.Zip;

public sealed class WinZipAesMemberTests
{
  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(1000)]
  public void EncryptDecrypt_RoundTrip(int length)
  {
    var rnd = new Random(length + 1);
    byte[] compressed = new byte[length]; rnd.NextBytes(compressed);
    byte[] pw = Encoding.UTF8.GetBytes("s3cret-пароль");

    byte[] member = WinZipAesMember.Encrypt(compressed, pw, WinZipAes.Strength.Aes256);

    var r = WinZipAesMember.TryDecrypt(member, pw, WinZipAes.Strength.Aes256, out byte[] back);
    Assert.Equal(WinZipAesDecryptResult.Ok, r);
    Assert.Equal(compressed, back);
  }

  [Fact]
  public void TryDecrypt_НеверныйПароль()
  {
    byte[] compressed = Encoding.UTF8.GetBytes("данные");
    byte[] member = WinZipAesMember.Encrypt(compressed, Encoding.UTF8.GetBytes("right"), WinZipAes.Strength.Aes256);

    var r = WinZipAesMember.TryDecrypt(member, Encoding.UTF8.GetBytes("wrong"), WinZipAes.Strength.Aes256, out _);
    Assert.Equal(WinZipAesDecryptResult.WrongPassword, r);
  }

  [Fact]
  public void TryDecrypt_ПорченыйШифртекст_Corrupt()
  {
    byte[] compressed = new byte[64]; new Random(2).NextBytes(compressed);
    byte[] pw = Encoding.UTF8.GetBytes("pw");
    byte[] member = WinZipAesMember.Encrypt(compressed, pw, WinZipAes.Strength.Aes256);

    // Портим байт в шифртексте (после соли+pwVerify=18, до auth-кода).
    member[25] ^= 0xFF;

    var r = WinZipAesMember.TryDecrypt(member, pw, WinZipAes.Strength.Aes256, out _);
    Assert.Equal(WinZipAesDecryptResult.Corrupt, r);
  }

  [Fact]
  public void ExtraField_RoundTrip()
  {
    byte[] data = WinZipAesMember.BuildExtraFieldData(WinZipAesMember.VersionAe1, WinZipAes.Strength.Aes256, actualMethod: 8);
    Assert.True(WinZipAesMember.TryParseExtraFieldData(data, out ushort v, out var s, out ushort method));
    Assert.Equal(WinZipAesMember.VersionAe1, v);
    Assert.Equal(WinZipAes.Strength.Aes256, s);
    Assert.Equal(8, method);
  }
}
