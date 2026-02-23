using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipBcjIa64FilterChainedCodersIntegrationTests
{
  [Fact]
  public void DecodeSingleFileToArray_BcjIa64ThenLzma2_Ok()
  {
    const string fileName = "ia64.bin";
    const int dictionarySize = 1 << 20;

    // Нужно минимум 2 bundle’а (2 * 16), чтобы i!=0 и конвертер реально изменил данные.
    byte[] plain = new byte[40];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 17 + 3);

    // Bundle #0: гарантируем, что он не будет обработан (m=0).
    plain[0] = 0x00;

    // Bundle #1 (offset 16): подбираем байты так, чтобы IA64_Convert нашёл “branch-like” паттерн
    // и сделал реальную правку.
    plain[16] = 0x10; // (data[i] & 0x1E) == 16 => m=3 => после m++ обрабатывается slot с m=4

    // Для m=4 p = i + 12 => 28
    plain[27] = 0x00; // p[-1]
    plain[28] = 0x20; // p[0] (важно: младшие 3 бита = 0)
    plain[29] = 0x11;
    plain[30] = 0x22;
    plain[31] = 0x50; // p[3]: (0x50 >> 4) & 0xF == 5

    byte[] encoded = Ia64EncodeTransform(plain, startOffset: 0);

    // Проверяем, что конвертер реально изменил payload.
    Assert.False(encoded.AsSpan().SequenceEqual(plain));

    // Packed stream: LZMA2(COPY) от encoded.
    byte[] packedStream = Lzma2CopyEncoder.Encode(encoded, dictionarySize, out byte lzma2PropsByte);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_Ia64ThenLzma2(
      packedStream: packedStream,
      unpackSize: plain.Length,
      fileName: fileName,
      lzma2PropsByte: lzma2PropsByte);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive,
      out byte[] decodedBytes,
      out string decodedName,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(plain, decodedBytes);
  }

  private static byte[] Ia64EncodeTransform(byte[] src, uint startOffset)
  {
    // Инверсия к decode: IA64_Convert(..., encoding=1) из BraIA64.c.
    byte[] dst = (byte[])src.Clone();
    Ia64ConvertInPlace(dst.AsSpan(), startOffset, encoding: true);
    return dst;
  }

  private static void Ia64ConvertInPlace(Span<byte> data, uint startOffset, bool encoding)
  {
    if (data.Length < 16)
      return;

    int lastBundleStart = data.Length - 16;

    for (int i = 0; i <= lastBundleStart; i += 16)
    {
      int m = (int)((0x334B0000u >> (data[i] & 0x1E)) & 3u);
      if (m == 0)
        continue;

      for (++m; m <= 4; m++)
      {
        int p = i + m * 5 - 8;

        if (((data[p + 3] >> m) & 0xF) != 5)
          continue;

        uint t = (uint)(data[p - 1] | (data[p] << 8));
        if (((t >> m) & 0x70u) != 0)
          continue;

        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p, 4));

        uint v = raw >> m;
        v = (v & 0xFFFFFu) | ((v & (1u << 23)) >> 3);
        v <<= 4;

        uint add = unchecked(startOffset + (uint)i);
        v = encoding ? unchecked(v + add) : unchecked(v - add);

        v >>= 4;
        v &= 0x1FFFFFu;
        v = unchecked(v + 0x700000u);
        v &= 0x8FFFFFu;

        raw &= ~((0x8FFFFFu) << m);
        raw |= (v << m);

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(p, 4), raw);
      }
    }
  }

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_Ia64ThenLzma2(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_Ia64ThenLzma2(
      packSize: packedStream.Length,
      unpackSize: unpackSize,
      fileName: fileName,
      lzma2PropsByte: lzma2PropsByte);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)packedStream.Length,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packedStream.Length + nextHeader.Length];
    sig.Write(archive);

    packedStream.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));
    nextHeader.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size + packedStream.Length));

    return archive;
  }

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_Ia64ThenLzma2(
    int packSize,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte)
  {
    List<byte> h = new(512)
    {
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,

      // PackInfo
      SevenZipNid.PackInfo
    };
    WriteU64(h, 0); // PackPos
    WriteU64(h, 1); // NumPackStreams
    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1); // NumFolders
    h.Add(0x00);    // External=0

    // Folder: 2 coders (filter + compression)
    WriteU64(h, 2);

    // coder0: IA64 (Branch codec) methodId = {03 03 04 01}
    h.Add(0x04); // mainByte: idSize=4, без props
    h.Add(0x03);
    h.Add(0x03);
    h.Add(0x04);
    h.Add(0x01);

    // coder1: LZMA2
    h.Add(0x21);
    h.Add(0x21);
    WriteU64(h, 1);
    h.Add(lzma2PropsByte);

    // BindPairs: InIndex(0) <- OutIndex(1)
    WriteU64(h, 0);
    WriteU64(h, 1);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize); // out0 (IA64 final)
    WriteU64(h, (ulong)unpackSize); // out1 (LZMA2 intermediate)

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1);

    h.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00);
    h.AddRange(nameBytes);

    h.Add(SevenZipNid.End);
    h.Add(SevenZipNid.End);

    return [.. h];
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
