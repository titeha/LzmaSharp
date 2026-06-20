using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.Tests.Crypto.Gost;

public sealed class GostKdfTests
{
  [Fact]
  public void Derive256_ПоОфициальномуТестВектору()
  {
    // RFC 7836: KDF_GOSTR3411_2012_256(K, label, seed) =
    // HMAC256(K, 0x01|label|0x00|seed|0x01|0x00). Вектор Appendix B соответствует
    // label = 26bdb878, seed = af21434145656378 (T = 01 26bdb878 00 af21434145656378 01 00).
    byte[] key = Convert.FromHexString(
        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
    byte[] label = Convert.FromHexString("26bdb878");
    byte[] seed = Convert.FromHexString("af21434145656378");

    byte[] expected = Convert.FromHexString(
        "a1aa5f7de402d7b3d323f2991c8d4534013137010a83754fd0af6d7cd4922ed9");

    Assert.Equal(expected, GostKdf.Derive256(key, label, seed));
  }
}
