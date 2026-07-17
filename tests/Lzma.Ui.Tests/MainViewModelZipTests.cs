using System.Text;

using Lzma.Core.Zip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

// Открытие/обзор ZIP-архивов в UI (шаг 1: чтение содержимого; распаковка — отдельный шаг).
public sealed class MainViewModelZipTests
{
  private sealed class StubArchivePicker(PickedArchive? result) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult(result);
  }

  private sealed class CancellingPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
        => Task.FromResult<string?>(null);
  }

  private sealed class CancellingFolderPicker : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
  }

  // Фейк выбора папки: всегда возвращает заданный путь (для теста распаковки на диск).
  private sealed class StubFolderPicker(string path) : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(path);
  }

  private static MainViewModel CreateViewModel(PickedArchive? picked)
      => new(new StubArchivePicker(picked), new CancellingPasswordPrompt(), new CancellingFolderPicker());

  private static MainViewModel CreateViewModel(PickedArchive? picked, IFolderPicker folderPicker)
      => new(new StubArchivePicker(picked), new CancellingPasswordPrompt(), folderPicker);

  // ---- DetectFormat: чистая функция определения формата по сигнатуре ----

  [Fact]
  public void DetectFormat_Сигнатура7z_ВозвращаетSevenZip()
  {
    byte[] sig = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04];
    Assert.Equal(MainViewModel.ArchiveFormat.SevenZip, MainViewModel.DetectFormat(sig));
  }

  [Theory]
  [InlineData(0x03, 0x04)] // локальный заголовок
  [InlineData(0x05, 0x06)] // пустой архив (сразу EOCD)
  [InlineData(0x07, 0x08)] // spanned/split
  public void DetectFormat_СигнатураZip_ВозвращаетZip(byte b2, byte b3)
  {
    byte[] sig = [0x50, 0x4B, b2, b3, 0x00, 0x00];
    Assert.Equal(MainViewModel.ArchiveFormat.Zip, MainViewModel.DetectFormat(sig));
  }

  [Fact]
  public void DetectFormat_Мусор_ВозвращаетUnknown()
  {
    Assert.Equal(MainViewModel.ArchiveFormat.Unknown, MainViewModel.DetectFormat([1, 2, 3, 4, 5, 6]));
    Assert.Equal(MainViewModel.ArchiveFormat.Unknown, MainViewModel.DetectFormat([]));
  }

  // ---- OpenAsync: сквозной путь picker → ZipReader → дерево ----

  [Fact]
  public async Task OpenAsync_РеальныйZip_ПоказываетСодержимое()
  {
    byte[] content = Encoding.UTF8.GetBytes("hello from a real zip archive, compressed enough to deflate");

    ZipWriteResult writeResult = ZipWriter.Build(
        [
          new ZipWriterEntry("docs/", [], IsDirectory: true),
          new ZipWriterEntry("docs/readme.txt", content),
        ],
        out byte[] zip);
    Assert.Equal(ZipWriteResult.Ok, writeResult);

    // Убеждаемся, что тестовый архив действительно опознаётся как ZIP.
    Assert.Equal(MainViewModel.ArchiveFormat.Zip, MainViewModel.DetectFormat(zip));

    MainViewModel vm = CreateViewModel(new PickedArchive("test.zip", zip));

    await vm.OpenCommand.ExecuteAsync();

    Assert.True(vm.HasArchive);
    Assert.Equal("test.zip — LzmaSharp", vm.Title);
    Assert.Null(vm.StatusMessage);

    // В корне — папка docs.
    Assert.Contains(vm.Items, i => i.Name == "docs" && i.IsDirectory);
  }

  [Fact]
  public async Task OpenAsync_ПустойZip_СообщаетЧтоПуст()
  {
    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build([], out byte[] zip));

    MainViewModel vm = CreateViewModel(new PickedArchive("empty.zip", zip));

    await vm.OpenCommand.ExecuteAsync();

    Assert.True(vm.HasArchive);
    Assert.Empty(vm.Items);
    Assert.Equal("Архив пуст.", vm.StatusMessage);
  }

  [Fact]
  public async Task OpenAsync_БитыйZip_ПоказываетОшибку()
  {
    // Правильная сигнатура ZIP, но дальше мусор → InvalidData.
    byte[] broken = [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    MainViewModel vm = CreateViewModel(new PickedArchive("broken.zip", broken));

    await vm.OpenCommand.ExecuteAsync();

    Assert.False(vm.HasArchive);
    Assert.NotNull(vm.StatusMessage);
    Assert.Contains("ZIP", vm.StatusMessage);
  }

  // ---- ExtractAll на открытом ZIP: реальная распаковка на диск ----

  [Fact]
  public async Task ExtractAll_ОткрытЗип_РаспаковываетНаДиск()
  {
    byte[] content = Encoding.UTF8.GetBytes("zip extraction through the view model");
    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(
        [new ZipWriterEntry("folder/a.txt", content)], out byte[] zip));

    string dest = Path.Combine(Path.GetTempPath(), "lzs-vm-zipx-" + Guid.NewGuid().ToString("N"));
    try
    {
      MainViewModel vm = CreateViewModel(new PickedArchive("x.zip", zip), new StubFolderPicker(dest));
      await vm.OpenCommand.ExecuteAsync();
      Assert.True(vm.HasArchive);

      await vm.ExtractAllCommand.ExecuteAsync();

      Assert.Equal(content, File.ReadAllBytes(Path.Combine(dest, "folder", "a.txt")));
      Assert.NotNull(vm.StatusMessage);
      Assert.Contains("Извлечено", vm.StatusMessage);
    }
    finally
    {
      if (Directory.Exists(dest))
        Directory.Delete(dest, recursive: true);
    }
  }

  [Fact]
  public void ZipExtractStatus_Ok_УказываетПапку()
  {
    Assert.Contains("Извлечено", MainViewModel.ZipExtractStatus(ZipExtractResult.Ok, @"C:\out"));
    Assert.Contains("диск", MainViewModel.ZipExtractStatus(ZipExtractResult.IOError, @"C:\out"));
    Assert.Contains("путь", MainViewModel.ZipExtractStatus(ZipExtractResult.InvalidData, @"C:\out"));
  }
}
