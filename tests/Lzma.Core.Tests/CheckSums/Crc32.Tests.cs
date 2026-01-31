using Lzma.Core.Checksums;

namespace Lzma.Core.Tests.Checksums;

public sealed class Crc32Tests
{
  [Fact]
  public void Compute_123456789_ДаетЭталонноеЗначение()
  {
    // Самый распространённый тестовый вектор для CRC32.
    // CRC32("123456789") == 0xCBF43926
    byte[] data = [(byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9'];

    uint crc = Crc32.Compute(data);

    Assert.Equal(0xCBF43926u, crc);
  }

  [Fact]
  public void Update_РаботаетИнкрементально()
  {
    ReadOnlySpan<byte> part1 = [(byte)'1', (byte)'2', (byte)'3', (byte)'4'];
    ReadOnlySpan<byte> part2 = [(byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9'];

    uint state = Crc32.InitialState;
    state = Crc32.Update(state, part1);
    state = Crc32.Update(state, part2);

    uint crc = Crc32.Finalize(state);

    Assert.Equal(0xCBF43926u, crc);
  }
}
