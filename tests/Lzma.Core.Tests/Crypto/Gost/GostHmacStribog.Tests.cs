using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.Tests.Crypto.Gost;

public sealed class GostHmacStribogTests
{
  // Официальные тест-векторы RFC 7836, Appendix B.
  private const string KeyHex =
      "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
  private const string MessageHex = "0126bdb87800af214341456563780100";

  [Fact]
  public void Compute256_ПоОфициальномуТестВектору()
  {
    byte[] key = Convert.FromHexString(KeyHex);
    byte[] message = Convert.FromHexString(MessageHex);
    byte[] expected = Convert.FromHexString(
        "a1aa5f7de402d7b3d323f2991c8d4534013137010a83754fd0af6d7cd4922ed9");

    Assert.Equal(expected, GostHmacStribog.Compute256(key, message));
  }

  [Fact]
  public void Compute512_ПоОфициальномуТестВектору()
  {
    byte[] key = Convert.FromHexString(KeyHex);
    byte[] message = Convert.FromHexString(MessageHex);
    byte[] expected = Convert.FromHexString(
        "a59bab22ecae19c65fbde6e5f4e9f5d8549d31f037f9df9b905500e171923a77"
      + "3d5f1530f2ed7e964cb2eedc29e9ad2f3afe93b2814f79f5000ffc0366c251e6");

    Assert.Equal(expected, GostHmacStribog.Compute512(key, message));
  }
}
