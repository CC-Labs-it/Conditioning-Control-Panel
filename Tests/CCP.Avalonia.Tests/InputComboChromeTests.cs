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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PointerEntryMovesKeyboardHighlight(bool compact) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var combo = new ComboBox { Width = 200, ItemsSource = new[] { "one", "two", "three" }, SelectedIndex = 0 };
        if (compact) combo.Theme = (global::Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DarkComboBoxStyle")!;
        var window = new Window { Width = 500, Height = 400, Content = combo };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var point = combo.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(combo.IsDropDownOpen);
            var selected = (ComboBoxItem)combo.ContainerFromIndex(0)!;
            var keyboard = (ComboBoxItem)combo.ContainerFromIndex(1)!;
            var hovered = (ComboBoxItem)combo.ContainerFromIndex(2)!;
            var popup = TopLevel.GetTopLevel(keyboard)!;
            popup.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            Dispatcher.UIThread.RunJobs();
            Assert.True(keyboard.IsFocused);
            var pink20 = ((ISolidColorBrush)window.FindResource("TransparentPink20Brush")!).Color;
            var pink40 = ((ISolidColorBrush)window.FindResource("TransparentPink40Brush")!).Color;
            Assert.Equal(pink20, ((ISolidColorBrush)Part<Border>(keyboard, "Bd").Background!).Color);
            popup.MouseMove(hovered.TranslatePoint(new Point(10, 10), popup)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(hovered.IsFocused);
            Assert.False(keyboard.IsFocused);
            Assert.Equal(Colors.Transparent, ((ISolidColorBrush)Part<Border>(keyboard, "Bd").Background!).Color);
            Assert.Equal(pink20, ((ISolidColorBrush)Part<Border>(hovered, "Bd").Background!).Color);
            Assert.Equal(pink40, ((ISolidColorBrush)Part<Border>(selected, "Bd").Background!).Color);
            Assert.Equal(1, Part<Ellipse>(selected, "SelDot").Opacity);
            popup.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
            Dispatcher.UIThread.RunJobs();
            Assert.True(keyboard.IsFocused);
            Assert.Equal(pink20, ((ISolidColorBrush)Part<Border>(keyboard, "Bd").Background!).Color);
            Assert.Equal(Colors.Transparent, ((ISolidColorBrush)Part<Border>(hovered, "Bd").Background!).Color);
            popup.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, combo.SelectedIndex);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });
    private static T Part<T>(Visual host, string name) where T : Control => host.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
}
