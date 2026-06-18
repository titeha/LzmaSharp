using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Crypto.Gost;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderGostEncryptedHeaderTests
{
  [Fact]
  public void Read_GostKuznyechikEncryptedHeader_СПаролем_ВозвращаетHeaderИФайлДекодируется()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password,
        expectedDecodedHeader: out byte[] expectedDecodedHeader);

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult result = reader.Read(
        input: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
    Assert.Equal(expectedDecodedHeader, reader.DecodedHeaderBytes);

    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;
    Assert.Equal(1UL, header.FilesInfo.FileCount);
    Assert.True(header.FilesInfo.HasNames);
    Assert.Equal(fileName, header.FilesInfo.Names![0]);

    SevenZipFolderDecodeResult decodeResult = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: header.StreamsInfo,
        packedStreams: reader.PackedStreams.Span,
        folderIndex: 0,
        output: out byte[] decodedFile);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, decodeResult);
    Assert.Equal(plain, decodedFile);
  }

  [Fact]
  public void Read_GostKuznyechikEncryptedHeader_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-file.bin",
        password: password,
        expectedDecodedHeader: out _);

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult result = reader.Read(
        input: archive,
        options: SevenZipDecodeOptions.Default,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
    Assert.False(reader.Header.HasValue);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);
  }

  [Fact]
  public void Read_GostKuznyechikEncryptedHeader_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-file.bin",
        password: correctPassword,
        expectedDecodedHeader: out _);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult result = reader.Read(
        input: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
    Assert.False(reader.Header.HasValue);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikEncryptedHeader_СПаролем_ВозвращаетФайл()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password,
        expectedDecodedHeader: out _);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(fileName, decodedFileName);
    Assert.Equal(plain, fileBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikEncryptedHeader_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-file.bin",
        password: password,
        expectedDecodedHeader: out _);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, decodedFileName);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikEncryptedHeader_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-file.bin",
        password: correctPassword,
        expectedDecodedHeader: out _);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, decodedFileName);
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikEncryptedHeader_СПаролем_ЗаписываетФайл()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password,
        expectedDecodedHeader: out _);

    string root = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(password),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
      Assert.Equal(archive.Length, bytesConsumed);

      string filePath = Path.Combine(root, fileName);
      Assert.True(File.Exists(filePath));
      Assert.Equal(plain, File.ReadAllBytes(filePath));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikEncryptedHeader_БезПароля_ВозвращаетNotSupportedИНичегоНеПишет()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password,
        expectedDecodedHeader: out _);

    string root = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
      Assert.Equal(archive.Length, bytesConsumed);
      AssertDestinationIsEmptyOrMissing(root, fileName);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikEncryptedHeader_СНевернымПаролем_ВозвращаетInvalidDataИНичегоНеПишет()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-file.bin";

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: correctPassword,
        expectedDecodedHeader: out _);

    string root = CreateTempRoot();

    try
    {
      using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(wrongPassword),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
      Assert.Equal(archive.Length, bytesConsumed);
      AssertDestinationIsEmptyOrMissing(root, fileName);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void DecodeToArray_GostKuznyechikEncryptedHeader_СПаролем_ВозвращаетФайл()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password,
        expectedDecodedHeader: out _);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);

    SevenZipDecodedFile file = Assert.Single(files);
    Assert.Equal(fileName, file.Name);
    Assert.Equal(plain, file.Bytes);
  }

  [Fact]
  public void DecodeToArray_GostKuznyechikEncryptedHeader_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-file.bin",
        password: password,
        expectedDecodedHeader: out _);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToArray_GostKuznyechikEncryptedHeader_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-file.bin",
        password: correctPassword,
        expectedDecodedHeader: out _);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToEntries_GostKuznyechikEncryptedHeader_СПаролем_ВозвращаетФайловыйEntry()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password,
        expectedDecodedHeader: out _);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);

    SevenZipDecodedEntry entry = Assert.Single(entries);
    Assert.Equal(fileName, entry.Name);
    Assert.False(entry.IsDirectory);
    Assert.Equal(plain, entry.Bytes);
  }

  [Fact]
  public void DecodeToEntries_GostKuznyechikEncryptedHeader_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-file.bin",
        password: password,
        expectedDecodedHeader: out _);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void DecodeToEntries_GostKuznyechikEncryptedHeader_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-file.bin",
        password: correctPassword,
        expectedDecodedHeader: out _);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikEncryptedHeaderAndFile_СПаролем_ВозвращаетФайл()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-and-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(fileName, decodedFileName);
    Assert.Equal(plain, fileBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikEncryptedHeaderAndFile_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-and-file.bin",
        password: password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, decodedFileName);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikEncryptedHeaderAndFile_СНевернымПаролемДляЗаголовка_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-and-file.bin",
        password: correctPassword);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, decodedFileName);
  }

  [Fact]
  public void DecodeSingleFileToArray_GostKuznyechikEncryptedHeaderAndFile_СВернымПаролемДляЗаголовкаИНевернымДляФайла_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword headerPassword = SevenZipPassword.FromString("header");
    using SevenZipPassword filePassword = SevenZipPassword.FromString("file");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-and-file.bin",
        headerPassword: headerPassword,
        filePassword: filePassword);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(headerPassword),
        fileBytes: out byte[] fileBytes,
        fileName: out string decodedFileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, decodedFileName);
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikEncryptedHeaderAndFile_СПаролем_ЗаписываетФайл()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-and-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password);

    string root = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(password),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
      Assert.Equal(archive.Length, bytesConsumed);

      string filePath = Path.Combine(root, fileName);
      Assert.True(File.Exists(filePath));
      Assert.Equal(plain, File.ReadAllBytes(filePath));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikEncryptedHeaderAndFile_БезПароля_ВозвращаетNotSupportedИНичегоНеПишет()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-and-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password);

    string root = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
      Assert.Equal(archive.Length, bytesConsumed);
      AssertDestinationIsEmptyOrMissing(root, fileName);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikEncryptedHeaderAndFile_СНевернымПаролемДляЗаголовка_ВозвращаетInvalidDataИНичегоНеПишет()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-and-file.bin";

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: correctPassword);

    string root = CreateTempRoot();

    try
    {
      using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(wrongPassword),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
      Assert.Equal(archive.Length, bytesConsumed);
      AssertDestinationIsEmptyOrMissing(root, fileName);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_GostKuznyechikEncryptedHeaderAndFile_СВернымПаролемДляЗаголовкаИНевернымДляФайла_ВозвращаетInvalidDataИНичегоНеПишет()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-and-file.bin";

    using SevenZipPassword headerPassword = SevenZipPassword.FromString("header");
    using SevenZipPassword filePassword = SevenZipPassword.FromString("file");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        headerPassword: headerPassword,
        filePassword: filePassword);

    string root = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(headerPassword),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
      Assert.Equal(archive.Length, bytesConsumed);
      AssertDestinationIsEmptyOrMissing(root, fileName);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void DecodeToArray_GostKuznyechikEncryptedHeaderAndFile_СПаролем_ВозвращаетФайл()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-and-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);

    SevenZipDecodedFile file = Assert.Single(files);
    Assert.Equal(fileName, file.Name);
    Assert.Equal(plain, file.Bytes);
  }

  [Fact]
  public void DecodeToArray_GostKuznyechikEncryptedHeaderAndFile_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-and-file.bin",
        password: password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToArray_GostKuznyechikEncryptedHeaderAndFile_СНевернымПаролемДляЗаголовка_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-and-file.bin",
        password: correctPassword);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToArray_GostKuznyechikEncryptedHeaderAndFile_СВернымПаролемДляЗаголовкаИНевернымДляФайла_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword headerPassword = SevenZipPassword.FromString("header");
    using SevenZipPassword filePassword = SevenZipPassword.FromString("file");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-and-file.bin",
        headerPassword: headerPassword,
        filePassword: filePassword);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(headerPassword),
        files: out SevenZipDecodedFile[] files,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToEntries_GostKuznyechikEncryptedHeaderAndFile_СПаролем_ВозвращаетФайловыйEntry()
  {
    byte[] plain = CreatePlainForTest();
    const string fileName = "gost-header-and-file.bin";

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: fileName,
        password: password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);

    SevenZipDecodedEntry entry = Assert.Single(entries);
    Assert.Equal(fileName, entry.Name);
    Assert.False(entry.IsDirectory);
    Assert.Equal(plain, entry.Bytes);
  }

  [Fact]
  public void DecodeToEntries_GostKuznyechikEncryptedHeaderAndFile_БезПароля_ВозвращаетNotSupported()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-and-file.bin",
        password: password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void DecodeToEntries_GostKuznyechikEncryptedHeaderAndFile_СНевернымПаролемДляЗаголовка_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword correctPassword = SevenZipPassword.FromString("ab");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-and-file.bin",
        password: correctPassword);

    using SevenZipPassword wrongPassword = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(wrongPassword),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void DecodeToEntries_GostKuznyechikEncryptedHeaderAndFile_СВернымПаролемДляЗаголовкаИНевернымДляФайла_ВозвращаетInvalidData()
  {
    byte[] plain = CreatePlainForTest();

    using SevenZipPassword headerPassword = SevenZipPassword.FromString("header");
    using SevenZipPassword filePassword = SevenZipPassword.FromString("file");

    byte[] archive = Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plain,
        fileName: "gost-header-and-file.bin",
        headerPassword: headerPassword,
        filePassword: filePassword);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(headerPassword),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  private static byte[] CreatePlainForTest()
  {
    var plain = new byte[256];

    for (int i = 0; i < plain.Length; i++)
    {
      plain[i] = unchecked((byte)(i * 31 + 7));
    }

    return plain;
  }

  private static byte[] Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_CopyFile(
      ReadOnlySpan<byte> plainFileBytes,
      string fileName,
      SevenZipPassword password,
      out byte[] expectedDecodedHeader)
  {
    byte[] filePackedStream = plainFileBytes.ToArray();

    byte[] innerHeader = BuildInnerHeader_SingleFile_SingleFolder_Copy(
        packSize: (ulong)filePackedStream.Length,
        unpackSize: (ulong)plainFileBytes.Length,
        fileName: fileName);

    expectedDecodedHeader = innerHeader;

    byte[] salt = [0xA1, 0xA2];
    byte[] iv = [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0];
    byte[] gostProperties = CreateGostDirectProperties(salt, iv);

    byte[] encryptedHeaderPackedStream = EncryptKuznyechikDirectKeyForTest(
        propertiesBytes: gostProperties,
        password: password,
        plain: innerHeader);

    byte[] outerNextHeader = BuildOuterNextHeader_EncodedHeader_GostKuznyechikThenCopy(
        packPos: (ulong)filePackedStream.Length,
        packSize: (ulong)encryptedHeaderPackedStream.Length,
        gostUnpackSize: (ulong)innerHeader.Length,
        finalUnpackSize: (ulong)innerHeader.Length,
        gostProperties: gostProperties,
        folderCrc: Crc32.Compute(innerHeader));

    uint nextHeaderCrc = Crc32.Compute(outerNextHeader);

    var signatureHeader = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)(filePackedStream.Length + encryptedHeaderPackedStream.Length),
        NextHeaderSize: (ulong)outerNextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] signatureHeaderBytes = new byte[SevenZipSignatureHeader.TotalSize];
    signatureHeader.Write(signatureHeaderBytes);

    byte[] archive = new byte[
        signatureHeaderBytes.Length +
        filePackedStream.Length +
        encryptedHeaderPackedStream.Length +
        outerNextHeader.Length];

    signatureHeaderBytes.CopyTo(archive.AsSpan(0));
    filePackedStream.CopyTo(archive.AsSpan(signatureHeaderBytes.Length));
    encryptedHeaderPackedStream.CopyTo(archive.AsSpan(signatureHeaderBytes.Length + filePackedStream.Length));
    outerNextHeader.CopyTo(archive.AsSpan(signatureHeaderBytes.Length + filePackedStream.Length + encryptedHeaderPackedStream.Length));

    return archive;
  }

  private static byte[] BuildInnerHeader_SingleFile_SingleFolder_Copy(
      ulong packSize,
      ulong unpackSize,
      string fileName)
  {
    List<byte> header =
    [
        SevenZipNid.Header,
            SevenZipNid.MainStreamsInfo,
            SevenZipNid.PackInfo,
        ];

    // Данные файла начинаются с нулевой позиции внутри packed streams.
    WriteEncodedUInt64(header, 0);
    WriteEncodedUInt64(header, 1);

    header.Add(SevenZipNid.Size);
    WriteEncodedUInt64(header, packSize);
    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.UnpackInfo);
    header.Add(SevenZipNid.Folder);
    WriteEncodedUInt64(header, 1);
    header.Add(0);

    // Один простой Copy coder.
    WriteEncodedUInt64(header, 1);
    header.Add(0x01);
    header.Add(0x00);

    header.Add(SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(header, unpackSize);
    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.SubStreamsInfo);
    header.Add(SevenZipNid.NumUnpackStream);
    WriteEncodedUInt64(header, 1);
    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.FilesInfo);
    WriteEncodedUInt64(header, 1);

    header.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteEncodedUInt64(header, (ulong)(1 + nameBytes.Length));
    header.Add(0);
    header.AddRange(nameBytes);

    header.Add(SevenZipNid.End);
    header.Add(SevenZipNid.End);

    return [.. header];
  }

  private static byte[] BuildOuterNextHeader_EncodedHeader_GostKuznyechikThenCopy(
      ulong packPos,
      ulong packSize,
      ulong gostUnpackSize,
      ulong finalUnpackSize,
      byte[] gostProperties,
      uint? folderCrc)
  {
    List<byte> header =
    [
        SevenZipNid.EncodedHeader,
            SevenZipNid.PackInfo,
        ];

    WriteEncodedUInt64(header, packPos);
    WriteEncodedUInt64(header, 1);

    header.Add(SevenZipNid.Size);
    WriteEncodedUInt64(header, packSize);
    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.UnpackInfo);
    header.Add(SevenZipNid.Folder);
    WriteEncodedUInt64(header, 1);
    header.Add(0);

    WriteFolderGostKuznyechikThenCopy(header, gostProperties);

    header.Add(SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(header, gostUnpackSize);
    WriteEncodedUInt64(header, finalUnpackSize);

    if (folderCrc.HasValue)
    {
      header.Add(SevenZipNid.Crc);
      header.Add(1);
      WriteUInt32LE(header, folderCrc.Value);
    }

    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.SubStreamsInfo);
    header.Add(SevenZipNid.NumUnpackStream);
    WriteEncodedUInt64(header, 1);
    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.End);

    return [.. header];
  }

  private static void WriteFolderGostKuznyechikThenCopy(
      List<byte> output,
      byte[] gostProperties)
  {
    var gostCoder = new SevenZipCoderInfo(
        methodId: SevenZipGostCoder.KuznyechikMethodId.ToArray(),
        properties: gostProperties,
        numInStreams: 1,
        numOutStreams: 1);

    var copyCoder = new SevenZipCoderInfo(
        methodId: [0x00],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    WriteEncodedUInt64(output, 2);
    WriteCoderInfo(output, gostCoder);
    WriteCoderInfo(output, copyCoder);

    // GOST.out0 -> Copy.in1
    WriteEncodedUInt64(output, 1);
    WriteEncodedUInt64(output, 0);
  }

  private static void WriteCoderInfo(
      List<byte> output,
      SevenZipCoderInfo coder)
  {
    int methodIdSize = coder.MethodId.Length;
    Assert.InRange(methodIdSize, 1, 15);

    bool isComplexCoder = coder.NumInStreams != 1 || coder.NumOutStreams != 1;
    bool hasProperties = coder.Properties.Length != 0;

    byte mainByte = (byte)(
        (methodIdSize & 0x0F) |
        (isComplexCoder ? 0x10 : 0) |
        (hasProperties ? 0x20 : 0));

    output.Add(mainByte);
    output.AddRange(coder.MethodId);

    if (isComplexCoder)
    {
      WriteEncodedUInt64(output, coder.NumInStreams);
      WriteEncodedUInt64(output, coder.NumOutStreams);
    }

    if (hasProperties)
    {
      WriteEncodedUInt64(output, (ulong)coder.Properties.Length);
      output.AddRange(coder.Properties);
    }
  }

  private static byte[] CreateGostDirectProperties(
      byte[] salt,
      byte[] iv)
  {
    Assert.InRange(salt.Length, 0, byte.MaxValue);
    Assert.InRange(iv.Length, 0, byte.MaxValue);

    var properties = new byte[5 + salt.Length + iv.Length];

    properties[0] = SevenZipGostCoder.CurrentPropertiesVersion;
    properties[1] = 0x00;
    properties[2] = SevenZipGostCoder.DirectKeyNumCyclesPower;
    properties[3] = (byte)salt.Length;
    properties[4] = (byte)iv.Length;

    salt.CopyTo(properties.AsSpan(5));
    iv.CopyTo(properties.AsSpan(5 + salt.Length));

    return properties;
  }

  private static byte[] EncryptKuznyechikDirectKeyForTest(
      byte[] propertiesBytes,
      SevenZipPassword password,
      byte[] plain)
  {
    Assert.True(SevenZipGostCoder.TryParseProperties(
        propertiesBytes,
        out SevenZipGostProperties? properties));

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    try
    {
      Assert.True(SevenZipGostKeyDerivation.TryDeriveDirectKey(
          properties!,
          password,
          key));

      Assert.True(GostKuznyechikCtrTransform.TryTransform(
          key,
          properties!.InitializationVector,
          plain,
          out byte[] encrypted));

      return encrypted;
    }
    finally
    {
      Array.Clear(key);
    }
  }

  private static void WriteEncodedUInt64(
      List<byte> output,
      ulong value)
  {
    Span<byte> buffer = stackalloc byte[9];

    SevenZipEncodedUInt64.WriteResult result = SevenZipEncodedUInt64.TryWrite(
        value,
        buffer,
        out int bytesWritten);

    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, result);
    Assert.True(bytesWritten > 0);

    output.AddRange(buffer[..bytesWritten].ToArray());
  }

  private static void WriteUInt32LE(
      List<byte> output,
      uint value)
  {
    output.Add((byte)value);
    output.Add((byte)(value >> 8));
    output.Add((byte)(value >> 16));
    output.Add((byte)(value >> 24));
  }

  private static string CreateTempRoot()
  {
    return Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveReaderGostEncryptedHeaderTests),
        Guid.NewGuid().ToString("N"));
  }

  private static void AssertDestinationIsEmptyOrMissing(
      string root,
      string fileName)
  {
    Assert.False(File.Exists(Path.Combine(root, fileName)));

    if (Directory.Exists(root))
      Assert.Empty(Directory.GetFileSystemEntries(root));
  }

  private static void TryDeleteTree(string path)
  {
    try
    {
      if (Directory.Exists(path))
        Directory.Delete(path, recursive: true);
    }
    catch
    {
      // Best-effort cleanup для тестового каталога.
    }
  }

  private static byte[] Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
    ReadOnlySpan<byte> plainFileBytes,
    string fileName,
    SevenZipPassword password)
  {
    return Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
        plainFileBytes: plainFileBytes,
        fileName: fileName,
        headerPassword: password,
        filePassword: password);
  }

  private static byte[] Build7zArchive_SingleFile_GostKuznyechikEncryptedHeader_GostKuznyechikCopyFile(
      ReadOnlySpan<byte> plainFileBytes,
      string fileName,
      SevenZipPassword headerPassword,
      SevenZipPassword filePassword)
  {
    byte[] plainFileBytesArray = plainFileBytes.ToArray();

    byte[] fileSalt = [0xB1, 0xB2];
    byte[] fileIv = [0x21, 0x43, 0x65, 0x87, 0xA9, 0xCB, 0xED, 0x0F];
    byte[] fileGostProperties = CreateGostDirectProperties(fileSalt, fileIv);

    byte[] filePackedStream = EncryptKuznyechikDirectKeyForTest(
        propertiesBytes: fileGostProperties,
        password: filePassword,
        plain: plainFileBytesArray);

    byte[] innerHeader = BuildInnerHeader_SingleFile_SingleFolder_GostKuznyechikThenCopy(
        packSize: (ulong)filePackedStream.Length,
        gostUnpackSize: (ulong)plainFileBytesArray.Length,
        finalUnpackSize: (ulong)plainFileBytesArray.Length,
        fileName: fileName,
        gostProperties: fileGostProperties,
        folderCrc: Crc32.Compute(plainFileBytesArray));

    byte[] headerSalt = [0xA1, 0xA2];
    byte[] headerIv = [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0];
    byte[] headerGostProperties = CreateGostDirectProperties(headerSalt, headerIv);

    byte[] encryptedHeaderPackedStream = EncryptKuznyechikDirectKeyForTest(
        propertiesBytes: headerGostProperties,
        password: headerPassword,
        plain: innerHeader);

    byte[] outerNextHeader = BuildOuterNextHeader_EncodedHeader_GostKuznyechikThenCopy(
        packPos: (ulong)filePackedStream.Length,
        packSize: (ulong)encryptedHeaderPackedStream.Length,
        gostUnpackSize: (ulong)innerHeader.Length,
        finalUnpackSize: (ulong)innerHeader.Length,
        gostProperties: headerGostProperties,
        folderCrc: Crc32.Compute(innerHeader));

    uint nextHeaderCrc = Crc32.Compute(outerNextHeader);

    var signatureHeader = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)(filePackedStream.Length + encryptedHeaderPackedStream.Length),
        NextHeaderSize: (ulong)outerNextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] signatureHeaderBytes = new byte[SevenZipSignatureHeader.TotalSize];
    signatureHeader.Write(signatureHeaderBytes);

    byte[] archive = new byte[
        signatureHeaderBytes.Length +
        filePackedStream.Length +
        encryptedHeaderPackedStream.Length +
        outerNextHeader.Length];

    signatureHeaderBytes.CopyTo(archive.AsSpan(0));
    filePackedStream.CopyTo(archive.AsSpan(signatureHeaderBytes.Length));
    encryptedHeaderPackedStream.CopyTo(archive.AsSpan(signatureHeaderBytes.Length + filePackedStream.Length));
    outerNextHeader.CopyTo(archive.AsSpan(signatureHeaderBytes.Length + filePackedStream.Length + encryptedHeaderPackedStream.Length));

    return archive;
  }

  private static byte[] BuildInnerHeader_SingleFile_SingleFolder_GostKuznyechikThenCopy(
    ulong packSize,
    ulong gostUnpackSize,
    ulong finalUnpackSize,
    string fileName,
    byte[] gostProperties,
    uint? folderCrc)
  {
    List<byte> header =
    [
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
        SevenZipNid.PackInfo,
    ];

    // Данные файла начинаются с нулевой позиции внутри packed streams.
    WriteEncodedUInt64(header, 0);
    WriteEncodedUInt64(header, 1);

    header.Add(SevenZipNid.Size);
    WriteEncodedUInt64(header, packSize);
    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.UnpackInfo);
    header.Add(SevenZipNid.Folder);
    WriteEncodedUInt64(header, 1);
    header.Add(0);

    WriteFolderGostKuznyechikThenCopy(header, gostProperties);

    header.Add(SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(header, gostUnpackSize);
    WriteEncodedUInt64(header, finalUnpackSize);

    if (folderCrc.HasValue)
    {
      header.Add(SevenZipNid.Crc);
      header.Add(1);
      WriteUInt32LE(header, folderCrc.Value);
    }

    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.SubStreamsInfo);
    header.Add(SevenZipNid.NumUnpackStream);
    WriteEncodedUInt64(header, 1);
    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.FilesInfo);
    WriteEncodedUInt64(header, 1);

    header.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteEncodedUInt64(header, (ulong)(1 + nameBytes.Length));
    header.Add(0);
    header.AddRange(nameBytes);

    header.Add(SevenZipNid.End);
    header.Add(SevenZipNid.End);

    return [.. header];
  }
}
