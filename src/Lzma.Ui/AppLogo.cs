using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Lzma.Ui;

/// <summary>
/// Логотип приложения «сжатие» (медный+стальные бруски в бейдже), отрисованный в bitmap на лету —
/// используется как иконка окна/задачи без внешних ассетов (.ico/.png). Цвета фиксированы (иконка
/// одинакова в обеих темах).
/// </summary>
internal static class AppLogo
{
    /// <summary>Рендерит логотип в <see cref="WindowIcon"/>; при любой ошибке — <see langword="null"/>.</summary>
    public static WindowIcon? TryCreateWindowIcon()
    {
        try
        {
            var copper = new SolidColorBrush(Color.Parse("#E08B54"));
            var steel = new SolidColorBrush(Color.Parse("#7FA8C9"));
            var badge = new SolidColorBrush(Color.Parse("#262B34"));

            static Border Bar(double width, IBrush fill) => new()
            {
                Width = width,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = fill,
            };

            var bars = new StackPanel
            {
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            bars.Children.Add(Bar(32, copper));
            bars.Children.Add(Bar(24, steel));
            bars.Children.Add(Bar(16, steel));

            var logo = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(14),
                Background = badge,
                BorderBrush = copper,
                BorderThickness = new Thickness(3),
                Child = bars,
            };

            var pixelSize = new PixelSize(64, 64);
            logo.Measure(new Size(64, 64));
            logo.Arrange(new Rect(0, 0, 64, 64));

            var render = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            render.Render(logo);

            using var ms = new MemoryStream();
            render.Save(ms);
            ms.Position = 0;
            return new WindowIcon(ms);
        }
        catch
        {
            return null; // без иконки — приложение всё равно запускается
        }
    }
}
