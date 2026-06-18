using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderTerminalStateTests
{
  [Fact]
  public void Read_AfterNotSupported_ReturnsSameResult_AndConsumedZero()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/hello_copy_7zaes_mhe_on_mhc_off.7z");

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult r1 = reader.Read(archive, out int consumed1);
    Assert.Equal(SevenZipArchiveReadResult.NotSupported, r1);
    Assert.Equal(archive.Length, consumed1);

    SevenZipArchiveReadResult r2 = reader.Read(archive, out int consumed2);
    Assert.Equal(SevenZipArchiveReadResult.NotSupported, r2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void Read_AfterInvalidData_ReturnsSameResult_AndConsumedZero()
  {
    byte[] archive = CreateEmptyArchiveWithWrongNextHeaderCrc();

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult r1 = reader.Read(archive, out int consumed1);
    Assert.Equal(SevenZipArchiveReadResult.InvalidData, r1);
    Assert.Equal(archive.Length, consumed1);

    SevenZipArchiveReadResult r2 = reader.Read(archive, out int consumed2);
    Assert.Equal(SevenZipArchiveReadResult.InvalidData, r2);
    Assert.Equal(0, consumed2);
  }

  private static byte[] CreateEmptyArchiveWithWrongNextHeaderCrc()
  {
    byte[] nextHeaderBytes =
    [
        SevenZipNid.Header,
            SevenZipNid.End,
        ];

    byte[] archive = new byte[SevenZipSignatureHeader.Size + nextHeaderBytes.Length];

    const ulong nextHeaderOffset = 0;
    const uint wrongNextHeaderCrc = 0x11223344u; // специально неверный CRC

    WriteSignatureHeader(archive, nextHeaderOffset, (ulong)nextHeaderBytes.Length, wrongNextHeaderCrc);
    nextHeaderBytes.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));

    return archive;
  }

  private static void WriteSignatureHeader(Span<byte> file, ulong nextHeaderOffset, ulong nextHeaderSize, uint nextHeaderCrc)
  {
    SevenZipSignatureHeader.Signature.CopyTo(file);
    file[6] = SevenZipSignatureHeader.MajorVersion;
    file[7] = SevenZipSignatureHeader.MinorVersion;

    Span<byte> startHeader = stackalloc byte[20];
    BinaryPrimitives.WriteUInt64LittleEndian(startHeader.Slice(0, 8), nextHeaderOffset);
    BinaryPrimitives.WriteUInt64LittleEndian(startHeader.Slice(8, 8), nextHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(startHeader.Slice(16, 4), nextHeaderCrc);

    uint startHeaderCrc = Crc32.Compute(startHeader);
    BinaryPrimitives.WriteUInt32LittleEndian(file.Slice(8, 4), startHeaderCrc);

    startHeader.CopyTo(file.Slice(12, 20));
  }

  private static byte[] ReadTestDataBytes(
      string relativePathFromSevenZipFolder,
      [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
