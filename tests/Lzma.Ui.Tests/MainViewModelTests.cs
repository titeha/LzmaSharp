using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

public sealed class MainViewModelTests
{
  // Стаб выбора архива: возвращает заранее заданный результат (или null = отмена).
  private sealed class StubArchivePicker(PickedArchive? result) : IArchivePicker
  {
    private readonly PickedArchive? _result = result;

    public Task<PickedArchive?> PickAsync() => Task.FromResult(_result);
  }

  // Стаб запроса пароля по умолчанию: всегда отмена (для неэшифрованных сценариев — не вызывается).
  private sealed class CancellingPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
        => Task.FromResult<string?>(null);
  }

  private static MainViewModel CreateViewModel(PickedArchive? picked = null)
      => new(new StubArchivePicker(picked), new CancellingPasswordPrompt());

  // ---- ApplyResult: чистая логика отображения результата ----

  [Fact]
  public void ApplyResult_Ok_ЗаполняетСписокЗаголовокИСостояние()
  {
    MainViewModel vm = CreateViewModel();

    SevenZipDecodedEntry[] entries =
    [
      new("docs", [], IsDirectory: true),
      new("readme.txt", [1, 2, 3], IsDirectory: false),
    ];

    vm.ApplyResult("archive.7z", SevenZipArchiveDecodeResult.Ok, entries);

    Assert.True(vm.HasArchive);
    Assert.Equal("archive.7z — LzmaSharp", vm.Title);
    Assert.Null(vm.StatusMessage);
    Assert.Equal(2, vm.Items.Count);

    // Каталоги идут первыми.
    Assert.True(vm.Items[0].IsDirectory);
    Assert.Equal("docs", vm.Items[0].Name);
    Assert.False(vm.Items[1].IsDirectory);
    Assert.Equal("readme.txt", vm.Items[1].Name);
    Assert.Equal(3, vm.Items[1].Size);
  }

  [Fact]
  public void ApplyResult_OkПустойАрхив_СообщаетЧтоПуст()
  {
    MainViewModel vm = CreateViewModel();

    vm.ApplyResult("empty.7z", SevenZipArchiveDecodeResult.Ok, []);

    Assert.True(vm.HasArchive);
    Assert.Empty(vm.Items);
    Assert.Equal("Архив пуст.", vm.StatusMessage);
  }

  [Fact]
  public void ApplyResult_NotSupported_СбрасываетИПоказываетПодсказкуПроПароль()
  {
    MainViewModel vm = CreateViewModel();
    vm.ApplyResult("x.7z", SevenZipArchiveDecodeResult.Ok, [new("a", [1], false)]);

    vm.ApplyResult("enc.7z", SevenZipArchiveDecodeResult.NotSupported, []);

    Assert.False(vm.HasArchive);
    Assert.Empty(vm.Items);
    Assert.Equal(MainViewModel.DefaultTitle, vm.Title);
    Assert.NotNull(vm.StatusMessage);
    Assert.Contains("парол", vm.StatusMessage);
  }

  [Fact]
  public void ApplyResult_InvalidData_СбрасываетИПоказываетОшибку()
  {
    MainViewModel vm = CreateViewModel();

    vm.ApplyResult("bad.7z", SevenZipArchiveDecodeResult.InvalidData, []);

    Assert.False(vm.HasArchive);
    Assert.Empty(vm.Items);
    Assert.Equal(MainViewModel.DefaultTitle, vm.Title);
    Assert.NotNull(vm.StatusMessage);
    Assert.Contains("повреждён", vm.StatusMessage);
  }

  // ---- OpenAsync: сквозной путь picker → декод → ApplyResult ----

  [Fact]
  public async Task OpenAsync_РеальныйАрхив_ПоказываетФайл()
  {
    byte[] content = Encoding.UTF8.GetBytes("hello from LzmaSharp UI test");

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("hello.txt", content)],
        out byte[] archive);
    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);

    MainViewModel vm = CreateViewModel(new PickedArchive("hello.7z", archive));

    await vm.OpenCommand.ExecuteAsync();

    Assert.True(vm.HasArchive);
    Assert.Equal("hello.7z — LzmaSharp", vm.Title);
    ArchiveItemAssert(vm, "hello.txt", content.Length);
  }

  [Fact]
  public async Task OpenAsync_ОтменаВыбора_СостояниеНеМеняется()
  {
    MainViewModel vm = CreateViewModel(picked: null); // пользователь отменил диалог

    await vm.OpenCommand.ExecuteAsync();

    Assert.False(vm.HasArchive);
    Assert.Empty(vm.Items);
    Assert.Equal(MainViewModel.DefaultTitle, vm.Title);
    Assert.Null(vm.StatusMessage);
  }

  private static void ArchiveItemAssert(MainViewModel vm, string name, long size)
  {
    Assert.Contains(vm.Items, i => i.Name == name && i.Size == size && !i.IsDirectory);
  }
}
