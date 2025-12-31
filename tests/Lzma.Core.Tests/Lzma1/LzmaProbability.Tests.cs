using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaProbabilityTests
{
  [Fact]
  public void Initial_РавенПоловинеBitModelTotal()
  {
    Assert.Equal((ushort)(LzmaConstants.BitModelTotal / 2), LzmaProbability.Initial);
  }

  [Fact]
  public void Reset_ЗаполняетМассивНачальнымиВероятностями()
  {
    var probs = new ushort[10];

    // Заполним не нулями, чтобы точно проверить перезапись.
    for (int i = 0; i < probs.Length; i++)
      probs[i] = 123;

    LzmaProbability.Reset(probs);

    Assert.All(probs, p => Assert.Equal(LzmaProbability.Initial, p));
  }
}
