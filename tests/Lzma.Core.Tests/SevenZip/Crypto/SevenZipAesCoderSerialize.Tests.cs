using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipAesCoderSerializeTests
{
  private static byte[] Bytes(int length, byte seed)
  {
    byte[] data = new byte[length];
    for (int i = 0; i < length; i++)
      data[i] = (byte)(i + seed);
    return data;
  }

  [Theory]
  [InlineData(19, 16, 16)]
  [InlineData(4, 1, 3)]
  [InlineData(0, 0, 0)]
  [InlineData(10, 0, 16)]
  [InlineData(10, 16, 0)]
  [InlineData(SevenZipAesCoder.DirectKeyNumCyclesPower, 8, 8)]
  public void Serialize_ЗатемParse_СвойстваСовпадают(int numCyclesPower, int saltSize, int ivSize)
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: (byte)numCyclesPower,
        salt: Bytes(saltSize, 0x41),
        initializationVector: Bytes(ivSize, 0x61));

    Assert.True(SevenZipAesCoder.TrySerializeProperties(properties, out byte[] serialized));
    Assert.True(SevenZipAesCoder.TryParseProperties(serialized, out SevenZipAesProperties? parsed));

    Assert.NotNull(parsed);
    Assert.Equal(properties.NumCyclesPower, parsed!.NumCyclesPower);
    Assert.Equal(properties.Salt, parsed.Salt);
    Assert.Equal(properties.InitializationVector, parsed.InitializationVector);
  }

  [Fact]
  public void Serialize_БезSaltИIv_ОдинБайт()
  {
    var properties = new SevenZipAesProperties(numCyclesPower: 12, salt: [], initializationVector: []);

    Assert.True(SevenZipAesCoder.TrySerializeProperties(properties, out byte[] serialized));
    Assert.Single(serialized);
    Assert.Equal(12, serialized[0]);
  }
}
