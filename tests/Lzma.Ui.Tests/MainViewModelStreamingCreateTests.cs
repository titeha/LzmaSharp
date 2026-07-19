using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты потокового создания архива из UI (LZMA2 + пикер со ссылками): архив пишется на диск потоком
/// через сервис, без чтения файлов в память, и корректно распаковывается.
/// </summary>
public sealed class MainViewModelStreamingCreateTests
{
  private sealed class StubArchivePicker : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
  }

  private sealed class NullPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
        => Task.FromResult<string?>(null);
  }

  private sealed class StubFolderPicker : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
  }

  // Пикер, отдающий ссылки на файлы (streaming) из данных в памяти.
  private sealed class RefsSourceFilesPicker(IReadOnlyList<PickedFileRef> refs) : ISourceFilesPicker
  {
    public bool SupportsRefs => true;

    public Task<IReadOnlyList<PickedFileRef>?> PickFileRefsAsync(
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
    {
      long bytes = 0;
      for (int i = 0; i < refs.Count; i++)
      {
        bytes += refs[i].Length;
        progress?.Report(new ScanProgress(i + 1, bytes));
      }

      return Task.FromResult<IReadOnlyList<PickedFileRef>?>(refs);
    }

    // Байтовый путь не должен вызываться при streaming.
    public Task<IReadOnlyList<PickedFile>?> PickFilesAsync(
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
        => throw new InvalidOperationException("должен использоваться потоковый путь");
  }

  private sealed class StubSaveFilePicker(string? path) : ISaveFilePicker
  {
    public Task<string?> PickSavePathAsync(string suggestedFileName) => Task.FromResult(path);
  }

  [Fact]
  public async Task ПотоковоеСоздание_LZMA2_ПишетНаДискИРаспаковывается()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет-привет-привет");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("UI поток 0123456789 ", 5000)));

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiStreamingCreate", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string outPath = Path.Combine(dir, "out.7z");

    try
    {
      var refs = new List<PickedFileRef>
      {
        new("a.txt", a.LongLength, () => new MemoryStream(a)),
        new("big.bin", big.LongLength, () => new MemoryStream(big)),
      };

      var vm = new MainViewModel(
          new StubArchivePicker(),
          new NullPasswordPrompt(),
          new StubFolderPicker(),
          new LzmaArchiveService(),
          new RefsSourceFilesPicker(refs),
          new StubSaveFilePicker(outPath));

      // Метод по умолчанию — LZMA2 → потоковый путь.
      Assert.Equal(SevenZipWriterCompressionMethod.Lzma2, vm.SelectedCompressionMethod);

      await vm.CreateCommand.ExecuteAsync();

      Assert.True(File.Exists(outPath));
      Assert.Contains("стало", vm.StatusMessage); // итог с коэффициентом

      byte[] archive = File.ReadAllBytes(outPath);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

      Assert.Equal(2, entries.Length);
      Assert.Equal("a.txt", entries[0].Name);
      Assert.Equal(a, entries[0].Bytes);
      Assert.Equal("big.bin", entries[1].Name);
      Assert.Equal(big, entries[1].Bytes);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task ПотоковоеСоздание_AutoSolid_ГруппируетИРаспаковывается()
  {
    // Текст (→ PPMd) + несжимаемое (→ Copy) — Auto группирует по кодекам в solid-блоки.
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("текстовый документ для PPMd ", 300)));
    var rnd = new Random(5);
    byte[] noise = new byte[4000];
    rnd.NextBytes(noise);

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiAutoSolid", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string outPath = Path.Combine(dir, "out.7z");

    try
    {
      var refs = new List<PickedFileRef>
      {
        new("doc.txt", text.LongLength, () => new MemoryStream(text)),
        new("blob.bin", noise.LongLength, () => new MemoryStream(noise)),
      };

      var vm = new MainViewModel(
          new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
          new LzmaArchiveService(), new RefsSourceFilesPicker(refs), new StubSaveFilePicker(outPath))
      {
        SelectedCompressionMethod = SevenZipWriterCompressionMethod.Auto,
      };

      await vm.CreateCommand.ExecuteAsync();

      Assert.True(File.Exists(outPath));
      byte[] archive = File.ReadAllBytes(outPath);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

      Assert.Equal(2, entries.Length);
      var byName = new Dictionary<string, byte[]>();
      foreach (SevenZipDecodedEntry e in entries)
        byName[e.Name] = e.Bytes;
      Assert.Equal(text, byName["doc.txt"]);
      Assert.Equal(noise, byName["blob.bin"]);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task ПотоковоеСоздание_Zip_ПишетНаДискИРаспаковывается()
  {
    byte[] a = Encoding.UTF8.GetBytes("zip из UI");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("zip поток 0123456789 ", 5000)));

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiZipCreate", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string outPath = Path.Combine(dir, "out.zip");

    try
    {
      var refs = new List<PickedFileRef>
      {
        new("a.txt", a.LongLength, () => new MemoryStream(a)),
        new("sub/big.txt", big.LongLength, () => new MemoryStream(big)),
      };

      var vm = new MainViewModel(
          new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
          new LzmaArchiveService(), new RefsSourceFilesPicker(refs), new StubSaveFilePicker(outPath))
      {
        UseZipFormat = true, // формат ZIP вместо 7z
      };

      await vm.CreateCommand.ExecuteAsync();

      Assert.True(File.Exists(outPath));
      Assert.Contains("стало", vm.StatusMessage);

      // Читается и распаковывается нашим потоковым ZIP-путём.
      string ext = Path.Combine(dir, "x");
      using (var read = new FileStream(outPath, FileMode.Open, FileAccess.Read))
      {
        Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] listed));
        Assert.Equal(ZipExtractResult.Ok, ZipStreamExtractor.ExtractToDirectory(read, listed, ext));
      }

      Assert.Equal(a, File.ReadAllBytes(Path.Combine(ext, "a.txt")));
      Assert.Equal(big, File.ReadAllBytes(Path.Combine(ext, "sub", "big.txt")));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task ПотоковоеСоздание_ПоТомам_ПишетТомаИРаспаковывается()
  {
    // Плохо сжимаемое содержимое + маленький том → несколько .001/.002…
    var payload = new byte[30000];
    uint x = 0xC0FFEE;
    for (int i = 0; i < payload.Length; i++) { x = x * 1664525u + 1013904223u; payload[i] = (byte)(x >> 24); }

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiVolumes", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string basePath = Path.Combine(dir, "out.7z");

    try
    {
      var refs = new List<PickedFileRef> { new("blob.bin", payload.LongLength, () => new MemoryStream(payload)) };

      var vm = new MainViewModel(
          new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
          new LzmaArchiveService(), new RefsSourceFilesPicker(refs), new StubSaveFilePicker(basePath))
      {
        SelectedVolumeSize = 8192, // маленький том
      };

      await vm.CreateCommand.ExecuteAsync();

      // Появились тома .001/.002…, а не единый out.7z.
      Assert.True(File.Exists(basePath + ".001"));
      Assert.False(File.Exists(basePath));
      Assert.Contains("Томов:", vm.StatusMessage);

      // Склейка через spanning-read распаковывается.
      using var r = new VolumeSpanningReadStream(basePath);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.ExtractToDirectoryFromStream(r, SevenZipDecodeOptions.Default, Path.Combine(dir, "x"), overwrite: false));
      Assert.Equal(payload, File.ReadAllBytes(Path.Combine(dir, "x", "blob.bin")));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }

  private sealed class StubCreatePasswordPrompt(string? password) : ICreatePasswordPrompt
  {
    public int Calls;
    public Task<string?> RequestNewPasswordAsync() { Calls++; return Task.FromResult(password); }
  }

  [Fact]
  public void СписокМетодов_СодержитBcj2()
  {
    var vm = new MainViewModel(new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker());
    Assert.Contains(vm.CompressionMethods, m => m.Method == SevenZipWriterCompressionMethod.Bcj2);
  }

  [Fact]
  public void СписокМетодов_СодержитAes()
  {
    var vm = new MainViewModel(new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker());
    Assert.Contains(vm.CompressionMethods, m => m.Method == SevenZipWriterCompressionMethod.Aes);
  }

  [Fact]
  public async Task ПотоковоеСоздание_Aes_ШифруетИРаспаковываетсяСПаролем()
  {
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("секретные данные ", 400)));
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiAes", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string outPath = Path.Combine(dir, "out.7z");

    try
    {
      var refs = new List<PickedFileRef> { new("secret.txt", data.LongLength, () => new MemoryStream(data)) };
      var promptStub = new StubCreatePasswordPrompt("p@ssw0rd");

      var vm = new MainViewModel(
          new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
          new LzmaArchiveService(), new RefsSourceFilesPicker(refs), new StubSaveFilePicker(outPath),
          sourceFolderPicker: null, createPasswordPrompt: promptStub)
      {
        SelectedCompressionMethod = SevenZipWriterCompressionMethod.Aes,
      };

      await vm.CreateCommand.ExecuteAsync();

      Assert.Equal(1, promptStub.Calls);
      Assert.True(File.Exists(outPath));

      byte[] archive = File.ReadAllBytes(outPath);
      var options = SevenZipDecodeOptions.WithPassword(SevenZipPassword.FromString("p@ssw0rd"));
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archive, options, out SevenZipDecodedEntry[] entries));
      Assert.Equal(data, Assert.Single(entries).Bytes);
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
  }

  [Fact]
  public async Task ПотоковоеСоздание_Aes_ОтменаПароля_НеСоздаёт()
  {
    byte[] data = Encoding.UTF8.GetBytes("данные");
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiAesCancel", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string outPath = Path.Combine(dir, "out.7z");

    try
    {
      var refs = new List<PickedFileRef> { new("f.txt", data.LongLength, () => new MemoryStream(data)) };

      var vm = new MainViewModel(
          new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
          new LzmaArchiveService(), new RefsSourceFilesPicker(refs), new StubSaveFilePicker(outPath),
          sourceFolderPicker: null, createPasswordPrompt: new StubCreatePasswordPrompt(null)) // отмена
      {
        SelectedCompressionMethod = SevenZipWriterCompressionMethod.Aes,
      };

      await vm.CreateCommand.ExecuteAsync();

      Assert.False(File.Exists(outPath));
      Assert.Contains("отменено", vm.StatusMessage);
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
  }

  [Fact]
  public async Task ПотоковоеСоздание_Bcj2_ПишетНаДискИРаспаковывается()
  {
    // Синтетический x86 PE: MZ → PE\0\0 (i386) + call-heavy тело.
    var pe = new byte[20000];
    pe[0] = (byte)'M'; pe[1] = (byte)'Z';
    pe[0x3C] = 0x80;
    pe[0x80] = (byte)'P'; pe[0x81] = (byte)'E'; pe[0x84] = 0x4C; pe[0x85] = 0x01;
    for (int p = 0x100; p + 8 < pe.Length; p += 50)
    {
      pe[p] = 0xE8;
      uint rel = unchecked(0x40u - (uint)p - 5);
      pe[p + 1] = (byte)rel; pe[p + 2] = (byte)(rel >> 8); pe[p + 3] = (byte)(rel >> 16); pe[p + 4] = (byte)(rel >> 24);
    }

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiBcj2Create", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string outPath = Path.Combine(dir, "out.7z");

    try
    {
      var refs = new List<PickedFileRef> { new("app.exe", pe.LongLength, () => new MemoryStream(pe)) };

      var vm = new MainViewModel(
          new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
          new LzmaArchiveService(), new RefsSourceFilesPicker(refs), new StubSaveFilePicker(outPath))
      {
        SelectedCompressionMethod = SevenZipWriterCompressionMethod.Bcj2,
      };

      await vm.CreateCommand.ExecuteAsync();

      Assert.True(File.Exists(outPath));
      byte[] archive = File.ReadAllBytes(outPath);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));
      Assert.Equal(pe, Assert.Single(entries).Bytes);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task ПотоковоеСоздание_Ppmd_ИдётПотоковымПутём_БезЧтенияВПамять()
  {
    // PPMd тоже идёт ПОТОКОВЫМ путём (ref-пикер), а не байтовым (PickFilesAsync бросил бы). Это чинит
    // «на больших файлах PPMd читал в память». Round-trip нашим декодером.
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("PPMd поток документ 0123 ", 400)));
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiPpmdCreate", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string outPath = Path.Combine(dir, "out.7z");

    try
    {
      var refs = new List<PickedFileRef> { new("doc.txt", text.LongLength, () => new MemoryStream(text)) };

      var vm = new MainViewModel(
          new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
          new LzmaArchiveService(), new RefsSourceFilesPicker(refs), new StubSaveFilePicker(outPath))
      {
        SelectedCompressionMethod = SevenZipWriterCompressionMethod.Ppmd,
      };

      await vm.CreateCommand.ExecuteAsync(); // не должно бросить (PickFilesAsync не вызывается)

      Assert.True(File.Exists(outPath));
      byte[] archive = File.ReadAllBytes(outPath);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));
      Assert.Equal(text, Assert.Single(entries).Bytes);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task Copy_ТожеИдётПотоковымПутём_БезЧтенияВПамять()
  {
    // Теперь ПОТОКОВЫЙ путь используется для ВСЕХ методов (не только LZMA2): для Copy тоже берётся
    // ref-пикер (PickFileRefsAsync), а не байтовый (PickFilesAsync бросил бы) — и архив создаётся.
    byte[] a = System.Text.Encoding.UTF8.GetBytes("copy поток");
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiStreamingCreate2", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string outPath = Path.Combine(dir, "out.7z");

    try
    {
      var refs = new List<PickedFileRef> { new("a.txt", a.LongLength, () => new MemoryStream(a)) };

      var vm = new MainViewModel(
          new StubArchivePicker(),
          new NullPasswordPrompt(),
          new StubFolderPicker(),
          new LzmaArchiveService(),
          new RefsSourceFilesPicker(refs),
          new StubSaveFilePicker(outPath))
      {
        SelectedCompressionMethod = SevenZipWriterCompressionMethod.Copy,
      };

      await vm.CreateCommand.ExecuteAsync(); // не должно бросить (PickFilesAsync не вызывается)

      Assert.True(File.Exists(outPath));
      byte[] archive = File.ReadAllBytes(outPath);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));
      Assert.Single(entries);
      Assert.Equal(a, entries[0].Bytes);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }
}
