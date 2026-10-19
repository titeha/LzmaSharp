using Lzma.Core.SevenZip;
using Lzma.Core.Zip;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Сообщения статуса об отказах pre-flight извлечения (<c>InsufficientSpace</c>, <c>PathTooLong</c>):
/// пользователь должен видеть точную причину, а не общую формулировку про повреждённые данные,
/// конфликт имён или неверный пароль. Проверяются чистые функции маппинга результата в строку
/// для 7z (<see cref="MainViewModel.ExtractStatus"/>), для потокового 7z
/// (<see cref="MainViewModel.StreamingExtractStatus"/>) и для ZIP
/// (<see cref="MainViewModel.ZipExtractStatus"/>).
/// </summary>
public sealed class MainViewModelExtractStatusTests
{
  private const string Destination = @"C:\out";

  [Theory]
  [InlineData(SevenZipArchiveDecodeResult.InsufficientSpace, "свободного места")]
  [InlineData(SevenZipArchiveDecodeResult.PathTooLong, "допустимую длину")]
  public void ExtractStatus_ОтказPreFlight_СообщаетТочнуюПричину(SevenZipArchiveDecodeResult result, string expected)
  {
    string message = MainViewModel.ExtractStatus(result, Destination);

    Assert.Contains(expected, message);
    Assert.StartsWith("Не удалось извлечь:", message);
    // Не общая формулировка «ошибка данных или файл уже существует» и не подсказка про пароль.
    Assert.DoesNotContain("ошибка данных", message);
    Assert.DoesNotContain("пароль", message);
  }

  [Fact]
  public void ExtractStatus_ПовреждённыеДанные_СохраняетПрежнююОбщуюФормулировку()
  {
    Assert.Equal(
        "Не удалось извлечь: ошибка данных или файл уже существует.",
        MainViewModel.ExtractStatus(SevenZipArchiveDecodeResult.InvalidData, Destination));
  }

  [Theory]
  [InlineData(SevenZipArchiveDecodeResult.InsufficientSpace, "свободного места")]
  [InlineData(SevenZipArchiveDecodeResult.PathTooLong, "допустимую длину")]
  public void StreamingExtractStatus_ЗашифрованныйАрхивОтказPreFlight_НеПодсказываетПроПароль(
      SevenZipArchiveDecodeResult result, string expected)
  {
    string message = MainViewModel.StreamingExtractStatus(result, Destination, encrypted: true);

    Assert.Contains(expected, message);
    Assert.DoesNotContain("пароль", message);
    Assert.Equal(MainViewModel.ExtractStatus(result, Destination), message);
  }

  [Fact]
  public void StreamingExtractStatus_ЗашифрованныйАрхивОшибкаДанных_ПоПрежнемуПодсказываетПроПароль()
  {
    Assert.Equal(
        "Не удалось извлечь: неверный пароль или повреждённый архив.",
        MainViewModel.StreamingExtractStatus(SevenZipArchiveDecodeResult.InvalidData, Destination, encrypted: true));
  }

  [Fact]
  public void StreamingExtractStatus_Успех_УказываетПапку()
  {
    Assert.Equal(
        $"Извлечено в: {Destination}",
        MainViewModel.StreamingExtractStatus(SevenZipArchiveDecodeResult.Ok, Destination, encrypted: false));
  }

  [Theory]
  [InlineData(ZipExtractResult.InsufficientSpace, "свободного места")]
  [InlineData(ZipExtractResult.PathTooLong, "допустимую длину")]
  public void ZipExtractStatus_ОтказPreFlight_СообщаетТочнуюПричину(ZipExtractResult result, string expected)
  {
    string message = MainViewModel.ZipExtractStatus(result, Destination);

    Assert.Contains(expected, message);
    Assert.StartsWith("Не удалось извлечь ZIP:", message);
    Assert.DoesNotContain("конфликт имён", message);
    Assert.DoesNotContain("записи на диск", message);
  }

  [Fact]
  public void ZipExtractStatus_ПрежниеСостояния_ФормулировкиНеИзменились()
  {
    Assert.Equal($"Извлечено в: {Destination}", MainViewModel.ZipExtractStatus(ZipExtractResult.Ok, Destination));
    Assert.Equal(
        "Не удалось извлечь ZIP: ошибка записи на диск.",
        MainViewModel.ZipExtractStatus(ZipExtractResult.IOError, Destination));
    Assert.Equal(
        "Не удалось извлечь ZIP: небезопасный путь, конфликт имён или файл уже существует.",
        MainViewModel.ZipExtractStatus(ZipExtractResult.InvalidData, Destination));
  }
}
