using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipNextHeaderReaderLimitsAndResetTests
{
  [Fact]
  public void SignatureHeader_GetterBeforeSuccessfulRead_ThrowsInvalidOperationException()
  {
    var reader = new SevenZipNextHeaderReader();

    Assert.False(reader.HasSignatureHeader);
    Assert.Throws<InvalidOperationException>(() => _ = reader.SignatureHeader);
  }

  [Fact]
  public void Read_ReturnsNotSupported_IfPackedStreamsSizeExceedsIntLimit()
  {
    // Лимит буфера packed-области = int.MaxValue (in-memory путь ограничен 2 ГиБ; для больших —
    // потоковый путь). Смещение next-header больше int.MaxValue открыть in-memory нельзя.
    byte[] data = BuildSignatureHeaderOnly(
      nextHeaderOffset: (ulong)int.MaxValue + 1UL,
      nextHeaderSize: 0,
      nextHeaderCrc: 0);

    var reader = new SevenZipNextHeaderReader();

    var res = reader.Read(data, out int consumed);

    Assert.Equal(SevenZipNextHeaderReadResult.NotSupported, res);
    Assert.Equal(data.Length, consumed);
    Assert.True(reader.HasSignatureHeader);
    Assert.Equal((ulong)int.MaxValue + 1UL, reader.SignatureHeader.NextHeaderOffset);

    var res2 = reader.Read(data, out int consumed2);

    Assert.Equal(SevenZipNextHeaderReadResult.NotSupported, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void Read_ReturnsNotSupported_IfNextHeaderSizeExceedsIntMaxValue()
  {
    byte[] data = BuildSignatureHeaderOnly(
      nextHeaderOffset: 0,
      nextHeaderSize: (ulong)int.MaxValue + 1UL,
      nextHeaderCrc: 0);

    var reader = new SevenZipNextHeaderReader();

    var res = reader.Read(data, out int consumed);

    Assert.Equal(SevenZipNextHeaderReadResult.NotSupported, res);
    Assert.Equal(data.Length, consumed);
    Assert.True(reader.HasSignatureHeader);
    Assert.Equal((ulong)int.MaxValue + 1UL, reader.SignatureHeader.NextHeaderSize);

    var res2 = reader.Read(data, out int consumed2);

    Assert.Equal(SevenZipNextHeaderReadResult.NotSupported, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void Read_AfterOk_ReturnsSameResult_AndConsumesNothing()
  {
    byte[] nextHeader = [0x01, 0x02, 0x03];
    byte[] file = Build7zFile(
      nextHeaderOffset: 0,
      nextHeader);

    var reader = new SevenZipNextHeaderReader();

    var res1 = reader.Read(file, out int consumed1);
    Assert.Equal(SevenZipNextHeaderReadResult.Ok, res1);
    Assert.Equal(file.Length, consumed1);

    var res2 = reader.Read(file, out int consumed2);
    Assert.Equal(SevenZipNextHeaderReadResult.Ok, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void Reset_AfterInvalidData_AllowsReadingValidFileAgain()
  {
    byte[] invalidData = BuildSignatureHeaderOnly(
      nextHeaderOffset: 0,
      nextHeaderSize: 0,
      nextHeaderCrc: 0);

    invalidData[8] ^= 0xFF;

    var reader = new SevenZipNextHeaderReader();

    var badRes = reader.Read(invalidData, out int badConsumed);
    Assert.Equal(SevenZipNextHeaderReadResult.InvalidData, badRes);
    Assert.Equal(invalidData.Length, badConsumed);

    reader.Reset();

    byte[] nextHeader = [0x10, 0x20, 0x30, 0x40];
    byte[] validFile = Build7zFile(
      nextHeaderOffset: 0,
      nextHeader);

    var goodRes = reader.Read(validFile, out int goodConsumed);

    Assert.Equal(SevenZipNextHeaderReadResult.Ok, goodRes);
    Assert.Equal(validFile.Length, goodConsumed);
    Assert.True(reader.HasSignatureHeader);
    Assert.Equal(nextHeader, reader.NextHeader.ToArray());
    Assert.Empty(reader.PackedStreams.ToArray());
  }

  // SEC-013: оба теста измержают дельту аллокаций вокруг single Read. До правки физический
  // буфер выделялся целиком по недоверенному значению заголовка (до 2 ГиБ) → OutOfMemoryException
  // или ~2 ГиБ аллокации. После правки буфер не выделяется, дельта много меньше 1 МиБ.

  [Fact]
  public void Read_ОгромныйNextHeaderOffset_НужныДанные_АлокацияОграничена()
  {
    // Вход — только 32-байтный стартовый заголовок, дальше данных нет.
    byte[] data = BuildSignatureHeaderOnly(
      nextHeaderOffset: 0x7FFFFFFFUL,
      nextHeaderSize: 0,
      nextHeaderCrc: 0);

    var reader = new SevenZipNextHeaderReader();

    long before = GC.GetAllocatedBytesForCurrentThread();
    var res = reader.Read(data, out int consumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipNextHeaderReadResult.NeedMoreInput, res);
    Assert.Equal(data.Length, consumed);
    Assert.True(
      after - before < 1024 * 1024,
      $"Алокация превысила 1 МиБ: {(after - before)} байт");
  }

  [Fact]
  public void Read_ОгромныйNextHeaderSize_НужныДанные_АлокацияОграничена()
  {
    // Вход — только 32-байтный стартовый заголовок, дальше данных нет.
    byte[] data = BuildSignatureHeaderOnly(
      nextHeaderOffset: 0,
      nextHeaderSize: 0x7FFFFFFFUL,
      nextHeaderCrc: 0);

    var reader = new SevenZipNextHeaderReader();

    long before = GC.GetAllocatedBytesForCurrentThread();
    var res = reader.Read(data, out int consumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipNextHeaderReadResult.NeedMoreInput, res);
    Assert.Equal(data.Length, consumed);
    Assert.True(
      after - before < 1024 * 1024,
      $"Алокация превысила 1 МиБ: {(after - before)} байт");
  }

  // SEC-013: ленивый рост не должен ломать инкрементальный сценарий — чтение кусками по 7 байт
  // даёт те же результаты и то же содержимое буферов, что и чтение всего входа одним вызовом.

  [Fact]
  public void Read_КускамиПо7Байт_ДаетТеРезультаты_ЧтоЦеликом()
  {
    byte[] nextHeader = new byte[500];
    for (int i = 0; i < nextHeader.Length; i++)
      nextHeader[i] = (byte)(i * 3 + 1);
    byte[] file = Build7zFile(nextHeaderOffset: 250, nextHeader);

    // Эталон: чтение всего входа одним вызовом.
    var reference = new SevenZipNextHeaderReader();
    var oneShotRes = reference.Read(file, out int oneShotConsumed);
    Assert.Equal(SevenZipNextHeaderReadResult.Ok, oneShotRes);
    Assert.Equal(file.Length, oneShotConsumed);

    // Тестируемый: чтение кусками по 7 байт.
    var chunked = new SevenZipNextHeaderReader();
    int pos = 0;
    SevenZipNextHeaderReadResult chunkedRes = SevenZipNextHeaderReadResult.NeedMoreInput;
    while (true)
    {
      int chunkSize = Math.Min(7, file.Length - pos);
      ReadOnlySpan<byte> chunk = file.AsSpan(pos, chunkSize);

      chunkedRes = chunked.Read(chunk, out int consumed);
      Assert.True(consumed > 0, "Декодер не продвинулся: не потребил ввод.");

      pos += consumed;
      if (chunkedRes == SevenZipNextHeaderReadResult.Ok)
        break;

      Assert.Equal(SevenZipNextHeaderReadResult.NeedMoreInput, chunkedRes);
      Assert.True(pos < file.Length, "Вход закончился раньше, чем завершилось чтение.");
    }

    Assert.Equal(file.Length, pos);
    Assert.Equal(SevenZipNextHeaderReadResult.Ok, chunkedRes);

    // Длина NextHeader совпадает с заяванным размером.
    Assert.Equal(nextHeader.Length, chunked.NextHeader.Length);

    // Содержимое буферов совпадает с эталоном.
    Assert.Equal(reference.NextHeader.ToArray(), chunked.NextHeader.ToArray());
    Assert.Equal(reference.PackedStreams.ToArray(), chunked.PackedStreams.ToArray());
  }

  // SEC-013: для легитимного входа, поданного целиком, аллокация не должна превышать
  // размер данных с запасом на служебные объекты. Защищает от квадратичного/избыточного роста.

  [Fact]
  public void Read_Целиком_АлокацияНеПревышаетРазмерДанных_СЗапасом()
  {
    byte[] nextHeader = new byte[4000];
    for (int i = 0; i < nextHeader.Length; i++)
      nextHeader[i] = (byte)(i % 251);
    ulong nextHeaderOffset = 2000;
    byte[] file = Build7zFile(nextHeaderOffset, nextHeader);

    var reader = new SevenZipNextHeaderReader();

    long before = GC.GetAllocatedBytesForCurrentThread();
    var res = reader.Read(file, out int consumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipNextHeaderReadResult.Ok, res);
    Assert.Equal(file.Length, consumed);

    long limit = (long)nextHeaderOffset + nextHeader.Length + 128 * 1024;
    Assert.True(
      after - before <= limit,
      $"Алокация {(after - before)} байт превышила лимит {limit}: возможный избыточный рост");
  }

  private static byte[] BuildSignatureHeaderOnly(
    ulong nextHeaderOffset,
    ulong nextHeaderSize,
    uint nextHeaderCrc)
  {
    var header = new SevenZipSignatureHeader(
      nextHeaderOffset,
      nextHeaderSize,
      nextHeaderCrc);

    byte[] data = new byte[SevenZipSignatureHeader.Size];
    header.Write(data);
    return data;
  }

  private static byte[] Build7zFile(
    ulong nextHeaderOffset,
    byte[] nextHeader)
  {
    ArgumentNullException.ThrowIfNull(nextHeader);

    var header = new SevenZipSignatureHeader(
      nextHeaderOffset,
      (ulong)nextHeader.Length,
      Crc32.Compute(nextHeader));

    byte[] file = new byte[SevenZipSignatureHeader.Size + (int)nextHeaderOffset + nextHeader.Length];
    header.Write(file.AsSpan(0, SevenZipSignatureHeader.Size));

    for (int i = 0; i < (int)nextHeaderOffset; i++)
      file[SevenZipSignatureHeader.Size + i] = 0xCC;

    nextHeader.CopyTo(file, SevenZipSignatureHeader.Size + (int)nextHeaderOffset);
    return file;
  }
}
