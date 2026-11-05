using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Inputs.xaml:218-308 checkbox and 394-431 list row parity.</summary>
public sealed class InputChromeThemeTests
{
    [Fact]
    public Task CheckBoxIsTheHouseDotWithCoreGlowAndThreeState() => Run(window =>
    {
        var wrap = new CheckBox { Width = 160, Content = new TextBlock { Text = string.Join(" ", Enumerable.Repeat("wrapping", 12)), TextWrapping = TextWrapping.Wrap } };
        var bare = new CheckBox { HorizontalAlignment = HorizontalAlignment.Left };
        var empty = new CheckBox { Content = "", HorizontalAlignment = HorizontalAlignment.Left };
        var three = new CheckBox { IsThreeState = true, IsChecked = null, Content = "three" };
        var toggle = new CheckBox { Theme = Res("ToggleStyle") };
        window.Content = new StackPanel { Children = { wrap, bare, empty, three, toggle } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Res("InputCheckBoxTheme"), wrap.Theme);
        Assert.Same(Res("ToggleStyle"), toggle.Theme); // keyed switches keep their own theme
        Assert.Equal(18, Part<Panel>(wrap, "DotHost").Bounds.Width);
        Assert.True(wrap.Bounds.Height > 40, $"content did not wrap: {wrap.Bounds.Height}");
        Assert.Equal(18, empty.Bounds.Width);
        Assert.Equal(18, bare.Bounds.Width); // no content -> no 8px gap
        Assert.Equal(Colour(window, "TransparentPink40Brush"), ((ISolidColorBrush)Part<Border>(three, "Dot").Background!).Color);
        Assert.False(Part<Ellipse>(three, "Core").IsVisible);

        var dot = Part<Border>(wrap, "Dot");
        Assert.Equal(0, dot.BoxShadow.Count);
        wrap.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Part<Ellipse>(wrap, "Core").IsVisible);
        Assert.Equal(Colour(window, "PinkBrush"), ((ISolidColorBrush)dot.Background!).Color);
        Assert.Equal(128, dot.BoxShadow[0].Color.A); // DropShadowEffect Opacity 0.5
        Assert.Equal(8, dot.BoxShadow[0].Blur);
        var accent = Color.FromRgb(0x18, 0xB8, 0xE0); // a mod switch repaints the glow
        window.Resources["PinkColor"] = accent;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(accent.B, dot.BoxShadow[0].Color.B);
        wrap.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, dot.BoxShadow.Count);

        window.MouseMove(wrap.TranslatePoint(new Point(5, 5), window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(wrap.IsPointerOver);
        Assert.Equal(Colour(window, "PinkBrush"), ((ISolidColorBrush)dot.BorderBrush!).Color);
        wrap.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.45, wrap.Opacity);
        Assert.Equal("Arrow", wrap.Cursor!.ToString());

        // check-pop 1 -> 1.18 -> 1 over 140ms, asserted as configured (P08: no stepped clock).
        var pop = Res("InputCheckBoxTheme").Children.OfType<Style>().Single(s => s.Animations.Count > 0);
        var animation = (Animation)pop.Animations[0];
        Assert.Equal(TimeSpan.FromMilliseconds(140), animation.Duration);
        Assert.Equal(new[] { 1d, 1.18d, 1d }, animation.Children.Select(k => (double)((Setter)k.Setters[0]).Value!));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ListBoxRowsUseTheAccentSelectionUnlessTheListBringsItsOwn() => Run(window =>
    {
        var list = new ListBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 };
        var own = new ControlTheme(typeof(ListBoxItem)) { BasedOn = (ControlTheme)Application.Current!.FindResource(typeof(ListBoxItem))! };
        var other = new ListBox { ItemsSource = new[] { "c" }, ItemContainerTheme = own };
        window.Content = new StackPanel { Children = { list, other } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var selected = (ListBoxItem)list.ContainerFromIndex(0)!;
        var plain = (ListBoxItem)list.ContainerFromIndex(1)!;
        Assert.Same(Res("InputListBoxItemTheme"), selected.Theme);
        Assert.Same(own, other.ContainerFromIndex(0)!.Theme);
        Assert.Equal(new Thickness(4, 3), plain.Padding);
        Assert.Equal(new CornerRadius(6), plain.CornerRadius);
        Assert.Equal(Colour(window, "TransparentPink40Brush"), ((ISolidColorBrush)Part<ContentPresenter>(selected, "PART_ContentPresenter").Background!).Color);
        Assert.Equal(Colour(window, "TextLightBrush"), ((ISolidColorBrush)selected.Foreground!).Color);
        Assert.Equal(Colour(window, "TextSecondaryBrush"), ((ISolidColorBrush)plain.Foreground!).Color);
        window.MouseMove(plain.TranslatePoint(new Point(5, 5), window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(plain.IsPointerOver);
        Assert.Equal(Colour(window, "TransparentPink20Brush"), ((ISolidColorBrush)Part<ContentPresenter>(plain, "PART_ContentPresenter").Background!).Color);
        plain.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.45, plain.Opacity);
        return Task.CompletedTask;
    });

    [Fact]
    public Task WindowLocalReplacementThemesKeepStockChrome() => Run(_ =>
    {
        // WPF GoonTestWindow.xaml:72,102 styles have no BasedOn.
        var goon = new global::ConditioningControlPanel.Avalonia.Views.Windows.GoonTestWindow();
        try
        {
            var check = new CheckBox();
            var list = new ListBox();
            goon.Content = new StackPanel { Children = { check, list } };
            goon.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Application.Current!.FindResource(typeof(CheckBox)), check.Theme);
            Assert.Same(Application.Current.FindResource(typeof(ListBoxItem)), list.ItemContainerTheme);
        }
        finally { goon.Close(); }
        return Task.CompletedTask;
    });

    private static ControlTheme Res(string key) => (ControlTheme)Application.Current!.FindResource(key)!;
    private static T Part<T>(Visual host, string name) where T : Control =>
        host.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    private static Color Colour(Window window, string key) => ((ISolidColorBrush)window.FindResource(key)!).Color;
    private static Task Run(Func<Window, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new Window { Width = 500, Height = 400 };
        try { await body(window); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
