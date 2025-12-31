using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaStateTests
{
  [Fact]
  public void Конструктор_ПриЗначенииВнеДиапазона_КидаетИсключение()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new LzmaState((byte)LzmaConstants.NumStates));
  }

  [Fact]
  public void Reset_СбрасываетВСостояние0()
  {
    var s = new LzmaState(11);
    s.Reset();
    Assert.Equal((byte)0, s.Value);
  }

  [Fact]
  public void IsLiteralState_ИстинаТолькоДляСостояний0До6()
  {
    for (byte i = 0; i < LzmaConstants.NumStates; i++)
    {
      var s = new LzmaState(i);
      bool expected = i < LzmaConstants.NumLitStates;
      Assert.Equal(expected, s.IsLiteralState);
    }
  }

  [Fact]
  public void UpdateLiteral_СоответствуетФормулеИзLzmaSdk()
  {
    for (byte i = 0; i < LzmaConstants.NumStates; i++)
    {
      var s = new LzmaState(i);
      s.UpdateLiteral();

      byte expected = i < 4 ? (byte)0 : (i < 10 ? (byte)(i - 3) : (byte)(i - 6));
      Assert.Equal(expected, s.Value);
    }
  }

  [Fact]
  public void UpdateMatch_СоответствуетФормулеИзLzmaSdk()
  {
    for (byte i = 0; i < LzmaConstants.NumStates; i++)
    {
      var s = new LzmaState(i);
      s.UpdateMatch();

      byte expected = i < 7 ? (byte)7 : (byte)10;
      Assert.Equal(expected, s.Value);
    }
  }

  [Fact]
  public void UpdateRep_СоответствуетФормулеИзLzmaSdk()
  {
    for (byte i = 0; i < LzmaConstants.NumStates; i++)
    {
      var s = new LzmaState(i);
      s.UpdateRep();

      byte expected = i < 7 ? (byte)8 : (byte)11;
      Assert.Equal(expected, s.Value);
    }
  }

  [Fact]
  public void UpdateShortRep_СоответствуетФормулеИзLzmaSdk()
  {
    for (byte i = 0; i < LzmaConstants.NumStates; i++)
    {
      var s = new LzmaState(i);
      s.UpdateShortRep();

      byte expected = i < 7 ? (byte)9 : (byte)11;
      Assert.Equal(expected, s.Value);
    }
  }
}
