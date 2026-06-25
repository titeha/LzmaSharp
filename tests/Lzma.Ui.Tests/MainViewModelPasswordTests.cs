using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

public sealed class MainViewModelPasswordTests
{
  private sealed class StubPicker(PickedArchive? result) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult(result);
  }

  // Очередь ответов на запрос пароля; фиксирует число вызовов и флаги «прошлая попытка неверна».
  private sealed class QueuedPasswordPrompt(params string?[] responses) : IPasswordPrompt
  {
    private readonly Queue<string?> _responses = new(responses);

    public int CallCount { get; private set; }
    public List<bool> PreviousFailedFlags { get; } = [];

    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
    {
      CallCount++;
      PreviousFailedFlags.Add(previousAttemptFailed);
      return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : null);
    }
  }

  private const string CorrectPassword = "secret";

  // Собирает ГОСТ-зашифрованный архив с одним файлом «a.txt».
  private static (byte[] Archive, byte[] Content) BuildEncryptedArchive()
  {
    byte[] content = Encoding.UTF8.GetBytes("encrypted content for UI password test");

    using SevenZipPassword password = SevenZipPassword.FromString(CorrectPassword);

    var options = new SevenZipGostEncryptionOptions
    {
      Cipher = SevenZipGostCipher.Kuznyechik,
      Password = password,
      NumCyclesPower = 4,
    };

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("a.txt", content)],
        options,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    return (archive, content);
  }

  [Fact]
  public async Task ВерныйПароль_ОткрываетАрхив()
  {
    (byte[] archive, _) = BuildEncryptedArchive();
    var prompt = new QueuedPasswordPrompt(CorrectPassword);
    var vm = new MainViewModel(new StubPicker(new PickedArchive("enc.7z", archive)), prompt);

    await vm.OpenCommand.ExecuteAsync();

    Assert.True(vm.HasArchive);
    Assert.Equal("enc.7z — LzmaSharp", vm.Title);
    Assert.Contains(vm.Items, i => i.Name == "a.txt");
    Assert.Equal(1, prompt.CallCount);
    Assert.Equal([false], prompt.PreviousFailedFlags);
  }

  [Fact]
  public async Task НеверныйПотомВерныйПароль_ОткрываетСоВторойПопытки()
  {
    (byte[] archive, _) = BuildEncryptedArchive();
    var prompt = new QueuedPasswordPrompt("wrong", CorrectPassword);
    var vm = new MainViewModel(new StubPicker(new PickedArchive("enc.7z", archive)), prompt);

    await vm.OpenCommand.ExecuteAsync();

    Assert.True(vm.HasArchive);
    Assert.Contains(vm.Items, i => i.Name == "a.txt");
    Assert.Equal(2, prompt.CallCount);
    // Вторая попытка должна получить флаг «прошлая неверна».
    Assert.Equal([false, true], prompt.PreviousFailedFlags);
  }

  [Fact]
  public async Task ОтменаВводаПароля_ПоказываетСообщениеИНеОткрывает()
  {
    (byte[] archive, _) = BuildEncryptedArchive();
    var prompt = new QueuedPasswordPrompt((string?)null); // сразу отмена
    var vm = new MainViewModel(new StubPicker(new PickedArchive("enc.7z", archive)), prompt);

    await vm.OpenCommand.ExecuteAsync();

    Assert.False(vm.HasArchive);
    Assert.Empty(vm.Items);
    Assert.Equal(1, prompt.CallCount);
    Assert.NotNull(vm.StatusMessage);
    Assert.Contains("парол", vm.StatusMessage);
  }

  private static byte[] ReadRealArchive(string fileName, [CallerFilePath] string caller = "")
  {
    string dir = Path.GetDirectoryName(caller)!;
    string path = Path.GetFullPath(Path.Combine(
        dir, "../Lzma.Core.Tests/SevenZip/TestData/Real/", fileName));
    return File.ReadAllBytes(path);
  }

  // Диагностика: какие формы реальных AES-архивов открываются через UI-поток с верным паролем.
  [Theory]
  [InlineData("aes_lzma2_singlefile_pwd_mhe_off.7z")]
  [InlineData("aes_lzma2_singlefile_pwd_mhe_on.7z")]
  [InlineData("aes_lzma2_multifile_pwd_mhe_off.7z")]
  [InlineData("aes_lzma2_multifile_pwd_mhe_on.7z")]
  [InlineData("aes_lzma2_solid_multifile_pwd_mhe_off.7z")]
  [InlineData("aes_lzma2_solid_multifile_pwd_mhe_on.7z")]
  public async Task РеальныеAesФормы_ВерныйПароль_Открываются(string fileName)
  {
    byte[] archive = ReadRealArchive(fileName);
    var prompt = new QueuedPasswordPrompt("LzmaSharp-AES-Stage15");
    var vm = new MainViewModel(new StubPicker(new PickedArchive(fileName, archive)), prompt);

    await vm.OpenCommand.ExecuteAsync();

    Assert.True(vm.HasArchive, $"{fileName}: HasArchive=false, статус: {vm.StatusMessage}");
  }

  [Fact]
  public async Task НеверныйПарольЗатемОтмена_ОстаётсяЗакрытым()
  {
    (byte[] archive, _) = BuildEncryptedArchive();
    var prompt = new QueuedPasswordPrompt("wrong", null);
    var vm = new MainViewModel(new StubPicker(new PickedArchive("enc.7z", archive)), prompt);

    await vm.OpenCommand.ExecuteAsync();

    Assert.False(vm.HasArchive);
    Assert.Empty(vm.Items);
    Assert.Equal(2, prompt.CallCount);
  }
}
