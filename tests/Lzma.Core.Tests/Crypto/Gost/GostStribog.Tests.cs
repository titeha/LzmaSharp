using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.Tests.Crypto.Gost;

public sealed class GostStribogTests
{
  // Официальные тест-векторы RFC 6986 / ГОСТ Р 34.11-2012, Section 10.
  private const string M1 =
      "323130393837363534333231303938373635343332313039383736353433323130"
    + "393837363534333231303938373635343332313039383736353433323130";

  private const string M2 =
      "fbe2e5f0eee3c820fbeafaebef20fffbf0e1e0f0f520e0ed20e8ece0ebe5f0f2"
    + "f120fff0eeec20f120faf2fee5e2202ce8f6f3ede220e8e6eee1e8f0f2d1202c"
    + "e8f0f2e5e220e5d1";

  private const string H512M1 =
      "486f64c1917879417fef082b3381a4e211c324f074654c38823a7b76f830ad00"
    + "fa1fbae42b1285c0352f227524bc9ab16254288dd6863dccd5b9f54a1ad0541b";

  private const string H256M1 =
      "00557be5e584fd52a449b16b0251d05d27f94ab76cbaa6da890b59d8ef1e159d";

  private const string H512M2 =
      "28fbc9bada033b1460642bdcddb90c3fb3e56c497ccd0f62b8a2ad4935e85f03"
    + "7613966de4ee00531ae60f3b5a47f8dae06915d5f2f194996fcabf2622e6881e";

  private const string H256M2 =
      "508f7e553c06501d749a66fc28c6cac0b005746d97537fa85d9e40904efed29d";

  [Theory]
  [InlineData(M1, H512M1)]
  [InlineData(M2, H512M2)]
  public void Hash512_ПоОфициальномуТестВектору(string messageHex, string expectedHex)
  {
    byte[] message = Convert.FromHexString(messageHex);
    byte[] expected = Convert.FromHexString(expectedHex);

    Assert.Equal(expected, GostStribog.Hash512(message));
  }

  [Theory]
  [InlineData(M1, H256M1)]
  [InlineData(M2, H256M2)]
  public void Hash256_ПоОфициальномуТестВектору(string messageHex, string expectedHex)
  {
    byte[] message = Convert.FromHexString(messageHex);
    byte[] expected = Convert.FromHexString(expectedHex);

    Assert.Equal(expected, GostStribog.Hash256(message));
  }

  [Fact]
  public void Hash512_ПустоеСообщение_НеБросает()
  {
    // Граничный случай: пустой вход обрабатывается одним padding-блоком.
    byte[] hash = GostStribog.Hash512([]);
    Assert.Equal(64, hash.Length);
  }
}
