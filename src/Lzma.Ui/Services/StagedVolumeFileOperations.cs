namespace Lzma.Ui.Services;

/// <summary>
/// Узкий seam файловых операций для <see cref="StagedVolumeSet"/>: позволяет тестам
/// детерминированно выбрасывать <see cref="System.IO.IOException"/> на заданной операции
/// (Exists/Move/Delete) без введения общей файловой системы всего приложения.
/// </summary>
internal interface IStagedVolumeFileOperations
{
  bool Exists(string path);

  void Move(
      string sourcePath,
      string destinationPath,
      bool overwrite);

  void Delete(string path);
}

/// <summary>
/// Реализация <see cref="IStagedVolumeFileOperations"/> по умолчанию поверх
/// <see cref="System.IO.File"/>.
/// </summary>
internal sealed class StagedVolumeFileOperations : IStagedVolumeFileOperations
{
  public bool Exists(string path) => System.IO.File.Exists(path);

  public void Move(string sourcePath, string destinationPath, bool overwrite)
      => System.IO.File.Move(sourcePath, destinationPath, overwrite);

  public void Delete(string path) => System.IO.File.Delete(path);
}