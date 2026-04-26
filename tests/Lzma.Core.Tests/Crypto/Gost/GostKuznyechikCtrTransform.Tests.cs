using System.Text;

using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.Tests.Crypto.Gost;

public sealed class GostKuznyechikCtrTransformTests
{
  [Fact]
  public void TryTransform_ПоОфициальномуТестВектору_ВозвращаетОжидаемыйCiphertext()
  {
    byte[] key = Convert.FromHexString(
        "8899AABBCCDDEEFF0011223344556677"
      + "FEDCBA98765432100123456789ABCDEF");

    byte[] iv = Convert.FromHexString(
        "1234567890ABCEF0");

    byte[] plaintext = Convert.FromHexString(
        "1122334455667700FFEEDDCCBBAA9988"
      + "00112233445566778899AABBCCEEFF0A"
      + "112233445566778899AABBCCEEFF0A00"
      + "2233445566778899AABBCCEEFF0A0011");

    byte[] expectedCiphertext = Convert.FromHexString(
        "F195D8BEC10ED1DBD57B5FA240BDA1B8"
      + "85EEE733F6A13E5DF33CE4B33C45DEE4"
      + "A5EAE88BE6356ED3D5E877F13564A3A5"
      + "CB91FAB1F20CBAB6D1C6D15820BDBA73");

    bool result = GostKuznyechikCtrTransform.TryTransform(
        key,
        iv,
        plaintext,
        out byte[] ciphertext);

    Assert.True(result);
    Assert.Equal(expectedCiphertext, ciphertext);
  }

  [Fact]
  public void TryTransform_ОфициальныйCiphertextОбратноДаетИсходныйPlaintext()
  {
    byte[] key = Convert.FromHexString(
        "8899AABBCCDDEEFF0011223344556677"
      + "FEDCBA98765432100123456789ABCDEF");

    byte[] iv = Convert.FromHexString(
        "1234567890ABCEF0");

    byte[] ciphertext = Convert.FromHexString(
        "F195D8BEC10ED1DBD57B5FA240BDA1B8"
      + "85EEE733F6A13E5DF33CE4B33C45DEE4"
      + "A5EAE88BE6356ED3D5E877F13564A3A5"
      + "CB91FAB1F20CBAB6D1C6D15820BDBA73");

    byte[] expectedPlaintext = Convert.FromHexString(
        "1122334455667700FFEEDDCCBBAA9988"
      + "00112233445566778899AABBCCEEFF0A"
      + "112233445566778899AABBCCEEFF0A00"
      + "2233445566778899AABBCCEEFF0A0011");

    bool result = GostKuznyechikCtrTransform.TryTransform(
        key,
        iv,
        ciphertext,
        out byte[] plaintext);

    Assert.True(result);
    Assert.Equal(expectedPlaintext, plaintext);
  }

  [Fact]
  public void TryTransform_ДляНекратнойБлокуДлины_КорректноДелаетRoundtrip()
  {
    byte[] key = Convert.FromHexString(
        "8899AABBCCDDEEFF0011223344556677"
      + "FEDCBA98765432100123456789ABCDEF");

    byte[] iv = Convert.FromHexString(
        "1234567890ABCEF0");

    byte[] plaintext = Encoding.UTF8.GetBytes("Gost Kuznyechik CTR partial block");

    Assert.True(GostKuznyechikCtrTransform.TryTransform(
        key,
        iv,
        plaintext,
        out byte[] ciphertext));

    Assert.True(GostKuznyechikCtrTransform.TryTransform(
        key,
        iv,
        ciphertext,
        out byte[] roundtrip));

    Assert.Equal(plaintext, roundtrip);
  }

  [Fact]
  public void TryTransform_ПустойВход_ВозвращаетПустойВыход()
  {
    byte[] key = new byte[GostKuznyechikCipher.KeySize];
    byte[] iv = new byte[GostKuznyechikCtrTransform.InitializationVectorSize];

    bool result = GostKuznyechikCtrTransform.TryTransform(
        key,
        iv,
        [],
        out byte[] output);

    Assert.True(result);
    Assert.Empty(output);
  }

  [Fact]
  public void TryTransform_ПриНекорректнойДлинеКлюча_ВозвращаетFalse()
  {
    byte[] key = new byte[GostKuznyechikCipher.KeySize - 1];
    byte[] iv = new byte[GostKuznyechikCtrTransform.InitializationVectorSize];
    byte[] input = new byte[17];

    bool result = GostKuznyechikCtrTransform.TryTransform(
        key,
        iv,
        input,
        out byte[] output);

    Assert.False(result);
    Assert.Empty(output);
  }

  [Fact]
  public void TryTransform_ПриНекорректнойДлинеIv_ВозвращаетFalse()
  {
    byte[] key = new byte[GostKuznyechikCipher.KeySize];
    byte[] iv = new byte[GostKuznyechikCtrTransform.InitializationVectorSize - 1];
    byte[] input = new byte[17];

    bool result = GostKuznyechikCtrTransform.TryTransform(
        key,
        iv,
        input,
        out byte[] output);

    Assert.False(result);
    Assert.Empty(output);
  }

  [Fact]
  public void TryTransform_ПриМаломВыходномБуфере_ВозвращаетFalse()
  {
    byte[] key = new byte[GostKuznyechikCipher.KeySize];
    byte[] iv = new byte[GostKuznyechikCtrTransform.InitializationVectorSize];
    byte[] input = new byte[17];
    byte[] output = new byte[16];

    bool result = GostKuznyechikCtrTransform.TryTransform(
        key,
        iv,
        input,
        output);

    Assert.False(result);
  }
}
