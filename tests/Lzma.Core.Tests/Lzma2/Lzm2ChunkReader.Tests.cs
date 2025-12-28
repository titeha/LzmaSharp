using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2ChunkReaderTests
{
  [Fact]
  public void EndMarker_0x00_ЧитаетсяКакЧанкБезPayload()
  {
    ReadOnlySpan<byte> input = [0x00];

    Lzma2ReadChunkResult result = Lzma2ChunkReader.TryReadChunk(
        input,
        out Lzma2ChunkHeader header,
        out ReadOnlySpan<byte> payload,
        out int consumed);

    Assert.Equal(Lzma2ReadChunkResult.Ok, result);
    Assert.Equal(Lzma2ChunkKind.End, header.Kind);
    Assert.Equal(1, header.HeaderSize);
    Assert.Equal(0, header.PayloadSize);
    Assert.Equal(1, consumed);
    Assert.True(payload.IsEmpty);
  }

  [Fact]
  public void CopyChunk_0x01_ЧитаетсяИВозвращаетPayload()
  {
    // COPY-чанк: control=0x01, unpackSize=1 (хранится как size-1 => 0x0000), затем 1 байт payload.
    byte[] data = [0x01, 0x00, 0x00, 0xAB];

    Lzma2ReadChunkResult result = Lzma2ChunkReader.TryReadChunk(
        data,
        out Lzma2ChunkHeader header,
        out ReadOnlySpan<byte> payload,
        out int consumed);

    Assert.Equal(Lzma2ReadChunkResult.Ok, result);
    Assert.Equal(Lzma2ChunkKind.Copy, header.Kind);
    Assert.Equal(1, header.UnpackSize);
    Assert.Equal(3, header.HeaderSize);
    Assert.Equal(1, header.PayloadSize);

    Assert.Equal(4, consumed);
    Assert.Equal(1, payload.Length);
    Assert.Equal(0xAB, payload[0]);
  }

  [Fact]
  public void LzmaChunk_0x80_ЧитаетсяИВозвращаетPayload()
  {
    // LZMA-чанк минимального размера (в реальности payload не является валидным LZMA-потоком,
    // но для этого шага это не важно — мы проверяем лишь границы чанка).
    // control = 0x80
    // unpackSize = 1 -> хранится как 0 (21 бит: 5 бит в control и 16 бит в следующих 2 байтах)
    // packSize = 1 -> хранится как 0 (16 бит)
    byte[] data = [0x80, 0x00, 0x00, 0x00, 0x00, 0xCC];

    Lzma2ReadChunkResult result = Lzma2ChunkReader.TryReadChunk(
        data,
        out Lzma2ChunkHeader header,
        out ReadOnlySpan<byte> payload,
        out int consumed);

    Assert.Equal(Lzma2ReadChunkResult.Ok, result);
    Assert.Equal(Lzma2ChunkKind.Lzma, header.Kind);
    Assert.Equal(1, header.UnpackSize);
    Assert.Equal(1, header.PackSize);
    Assert.Equal(5, header.HeaderSize);
    Assert.Equal(1, header.PayloadSize);

    Assert.Equal(6, consumed);
    Assert.Equal(1, payload.Length);
    Assert.Equal(0xCC, payload[0]);
  }

  [Fact]
  public void CopyChunk_ЗаголовокЕстьАPayloadНеХватает_ReturnsNeedMoreInput_ИНеПотребляетБайты()
  {
    // control=0x01, unpackSize=1, но payload (1 байт) отсутствует.
    byte[] data = [0x01, 0x00, 0x00];

    Lzma2ReadChunkResult result = Lzma2ChunkReader.TryReadChunk(
        data,
        out Lzma2ChunkHeader header,
        out ReadOnlySpan<byte> payload,
        out int consumed);

    Assert.Equal(Lzma2ReadChunkResult.NeedMoreInput, result);
    Assert.Equal(0, consumed);
    Assert.True(payload.IsEmpty);
    Assert.Equal(default, header);
  }

  [Fact]
  public void ПоследовательностьЧанков_МожноЧитатьПоОдному()
  {
    // COPY(1 байт) + END
    byte[] data = [0x01, 0x00, 0x00, 0x11, 0x00];

    int pos = 0;

    // 1) COPY
    {
      ReadOnlySpan<byte> slice = data.AsSpan(pos);
      Lzma2ReadChunkResult result = Lzma2ChunkReader.TryReadChunk(
          slice,
          out Lzma2ChunkHeader header,
          out ReadOnlySpan<byte> payload,
          out int consumed);

      Assert.Equal(Lzma2ReadChunkResult.Ok, result);
      Assert.Equal(Lzma2ChunkKind.Copy, header.Kind);
      Assert.Equal(1, payload.Length);
      Assert.Equal(0x11, payload[0]);

      pos += consumed;
    }

    // 2) END
    {
      ReadOnlySpan<byte> slice = data.AsSpan(pos);
      Lzma2ReadChunkResult result = Lzma2ChunkReader.TryReadChunk(
          slice,
          out Lzma2ChunkHeader header,
          out ReadOnlySpan<byte> payload,
          out int consumed);

      Assert.Equal(Lzma2ReadChunkResult.Ok, result);
      Assert.Equal(Lzma2ChunkKind.End, header.Kind);
      Assert.True(payload.IsEmpty);

      pos += consumed;
    }

    Assert.Equal(data.Length, pos);
  }
}
