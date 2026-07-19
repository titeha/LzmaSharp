using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();

            // Иконка окна/задачи — логотип, отрисованный в bitmap (без внешних ассетов).
            if (AppLogo.TryCreateWindowIcon() is { } icon)
                window.Icon = icon;

            var picker = new AvaloniaArchivePicker(window);
            var passwordPrompt = new AvaloniaPasswordPrompt(window);
            var folderPicker = new AvaloniaFolderPicker(window);
            var sourceFilesPicker = new AvaloniaSourceFilesPicker(window);
            var saveFilePicker = new AvaloniaSaveFilePicker(window);
            var sourceFolderPicker = new AvaloniaSourceFolderPicker(window);
            var createPasswordPrompt = new AvaloniaCreatePasswordPrompt(window);
            var fileSystemBrowser = new DesktopFileSystemBrowser();
            window.DataContext = new MainViewModel(
                picker,
                passwordPrompt,
                folderPicker,
                new LzmaArchiveService(),
                sourceFilesPicker,
                saveFilePicker,
                sourceFolderPicker,
                createPasswordPrompt,
                fileSystemBrowser);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}