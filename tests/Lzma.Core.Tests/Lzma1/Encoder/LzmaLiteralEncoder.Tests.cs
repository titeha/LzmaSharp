using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaLiteralEncoderTests
{
  [Fact]
  public void EncodeDecode_NormalLiteral_ОдинБайт_ДаетТочныйРезультат()
  {
    var rangeEnc = new LzmaRangeEncoder();
    var litEnc = new LzmaLiteralEncoder(lc: 3, lp: 0);

    rangeEnc.Reset();
    litEnc.Reset();

    const long position = 0;
    const byte previous = 0;
    const byte literal = 0x41; // 'A'

    litEnc.EncodeNormal(ref rangeEnc, position, previous, literal);

    rangeEnc.Flush();
    byte[] encoded = rangeEnc.ToArray();

    var rangeDec = new LzmaRangeDecoder();
    int srcPos = 0;

    Assert.Equal(LzmaRangeInitResult.Ok, rangeDec.TryInitialize(encoded, ref srcPos));

    var litDec = new LzmaLiteralDecoder(lc: 3, lp: 0);

    Assert.Equal(
      LzmaRangeDecodeResult.Ok,
      litDec.TryDecodeNormal(ref rangeDec, encoded, ref srcPos, previous, position, out byte decoded));

    Assert.Equal(literal, decoded);
  }

  [Fact]
  public void EncodeDecode_NormalLiteral_НесколькоБайт_ДаетТочныйРезультат()
  {
    var rangeEnc = new LzmaRangeEncoder();
    var litEnc = new LzmaLiteralEncoder(lc: 3, lp: 0);

    rangeEnc.Reset();
    litEnc.Reset();

    byte[] plain = { 0x41, 0x42, 0x43, 0x00, 0xFF };

    long pos = 0;
    byte prev = 0;
    foreach (byte b in plain)
    {
      litEnc.EncodeNormal(ref rangeEnc, pos, prev, b);
      prev = b;
      pos++;
    }

    rangeEnc.Flush();
    byte[] encoded = rangeEnc.ToArray();

    var rangeDec = new LzmaRangeDecoder();
    int srcPos = 0;

    Assert.Equal(LzmaRangeInitResult.Ok, rangeDec.TryInitialize(encoded, ref srcPos));

    var litDec = new LzmaLiteralDecoder(lc: 3, lp: 0);

    byte[] decoded = new byte[plain.Length];
    pos = 0;
    prev = 0;

    for (int i = 0; i < decoded.Length; i++)
    {
      Assert.Equal(
        LzmaRangeDecodeResult.Ok,
        litDec.TryDecodeNormal(ref rangeDec, encoded, ref srcPos, prev, pos, out decoded[i]));

      prev = decoded[i];
      pos++;
    }

    Assert.Equal(plain, decoded);
  }

  [Fact]
  public void EncodeDecode_MatchedLiteral_ДаетТочныйРезультат()
  {
    var rangeEnc = new LzmaRangeEncoder();
    var litEnc = new LzmaLiteralEncoder(lc: 3, lp: 0);

    rangeEnc.Reset();
    litEnc.Reset();

    const long position = 0;
    const byte previous = 0;

    const byte matchByte = 0x41;  // 'A' = 0100 0001
    const byte literal = 0x42;    // 'B' = 0100 0010 (совпадает по префиксу, расходится ближе к концу)

    litEnc.EncodeMatched(ref rangeEnc, position, previous, matchByte, literal);

    rangeEnc.Flush();
    byte[] encoded = rangeEnc.ToArray();

    var rangeDec = new LzmaRangeDecoder();
    int srcPos = 0;

    Assert.Equal(Core.Lzma1.LzmaRangeInitResult.Ok, rangeDec.TryInitialize(encoded, ref srcPos));

    var litDec = new LzmaLiteralDecoder(lc: 3, lp: 0);

    Assert.Equal(
      LzmaRangeDecodeResult.Ok,
      litDec.TryDecodeWithMatchByte(ref rangeDec, encoded, ref srcPos, previous, position, matchByte, out byte decoded));

    Assert.Equal(literal, decoded);
  }
}
