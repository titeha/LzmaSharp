using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

using Xunit;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcj2MergeTests
{
  [Fact]
  public void TryDecodeBcj2ToArray_Real7z_Bcj2_ProducesExpectedBytes()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj2_x86_lzma2_d1m_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int consumed));
    Assert.Equal(archive.Length, consumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;

    SevenZipFolderDecodeResult r1 = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
      streamsInfo,
      reader.PackedStreams.Span,
      folderIndex: 0,
      out byte[][] inputs);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, r1);
    Assert.Equal(4, inputs.Length);

    byte[] expected = BuildX86LikeBytes(4096);

    SevenZipFolderDecodeResult r2 = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
      buf0: inputs[0],
      buf1: inputs[1],
      buf2: inputs[2],
      buf3: inputs[3],
      outSize: expected.Length,
      output: out byte[] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, r2);
    Assert.Equal(expected, decoded);
  }

  private static byte[] BuildX86LikeBytes(int length)
  {
    var data = new byte[length];
    for (int i = 0; i < data.Length; i++)
      data[i] = 0x90;

    WriteRel32(data, pos: 0x00, opcode: 0xE8, target: 0x200);
    WriteRel32(data, pos: 0x40, opcode: 0xE9, target: 0x300);
    WriteRel32(data, pos: 0x80, opcode: 0xE8, target: 0x180);

    return data;
  }

  private static void WriteRel32(byte[] data, int pos, byte opcode, int target)
  {
    data[pos] = opcode;
    int rel = target - (pos + 5);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(pos + 1, 4), rel);
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
