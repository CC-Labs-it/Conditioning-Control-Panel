using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class InputComboChromeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OpenGlowAndRowStates(bool compact) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var combo = new ComboBox { Width = 200, HorizontalContentAlignment = HorizontalAlignment.Center, ItemsSource = new[] { "one", "two" }, SelectedIndex = 0 };
        if (compact) combo.Theme = (global::Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DarkComboBoxStyle")!;
        var window = new Window { Width = 500, Height = 400, Content = combo };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var border = Part<Border>(combo, "Background");
            Assert.Equal(0, border.BoxShadow.Count);
            var point = combo.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(combo.IsDropDownOpen);
            Assert.Equal(10, border.BoxShadow[0].Blur);
            Assert.Equal(Color.FromArgb(89, 255, 105, 180), border.BoxShadow[0].Color);
            var selected = (ComboBoxItem)combo.ContainerFromIndex(0)!;
            var other = (ComboBoxItem)combo.ContainerFromIndex(1)!;
            other.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(((ISolidColorBrush)window.FindResource("TransparentPink20Brush")!).Color, ((ISolidColorBrush)Part<Border>(other, "Bd").Background!).Color);
            selected.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(((ISolidColorBrush)window.FindResource("TransparentPink40Brush")!).Color, ((ISolidColorBrush)Part<Border>(selected, "Bd").Background!).Color);
            Assert.Equal(1, Part<Ellipse>(selected, "SelDot").Opacity);
            combo.IsDropDownOpen = false; Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, border.BoxShadow.Count);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });
    private static T Part<T>(Visual host, string name) where T : Control => host.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
}
