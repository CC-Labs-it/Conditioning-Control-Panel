using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class InputTextBoxThemeTests
{
    [Fact]
    public Task ImplicitAndFocusGlowTextBoxesUseWpfChromeAndKeepEditing() => Run(async window =>
    {
        var plain = new TextBox { Text = "alpha beta", Width = 240 };
        var alias = new TextBox { Theme = (ControlTheme)Application.Current!.FindResource("FocusGlowTextBox")!, Width = 240 };
        var other = new Button { Content = "Other" };
        window.Content = new StackPanel { Children = { plain, alias, other } };
        window.Show();
        Snap(plain, alias);
        other.Focus();
        window.MouseMove(plain.TranslatePoint(new Point(12, 12), window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(plain.IsPointerOver);
        Assert.Equal(0.6, ((ISolidColorBrush)Border(plain).BorderBrush!).Opacity);
        window.MouseMove(new Point(499, 299));
        foreach (var box in new[] { plain, alias })
        {
            Assert.Equal(new CornerRadius(8), box.CornerRadius);
            Assert.Equal(new Thickness(8, 5), box.Padding);
            Assert.Equal(new Thickness(1), box.BorderThickness);
            Assert.Equal(Colour(window, "TextLightBrush"), ((ISolidColorBrush)box.Foreground!).Color);
            Assert.Equal(Colour(window, "SurfaceBgBrush"), ((ISolidColorBrush)box.Background!).Color);
            Assert.Equal(Colour(window, "PinkBrush"), ((ISolidColorBrush)box.CaretBrush!).Color);
            var pink = Colour(window, "PinkBrush"); // WPF selection = pink at SelectionOpacity 0.4
            Assert.Equal(Color.FromArgb(0x66, pink.R, pink.G, pink.B), ((ISolidColorBrush)box.SelectionBrush!).Color);
            box.Focus();
            await Until(() => Shadow(box).Color.A > 110);
            Assert.Equal(Colour(window, "PinkBrush"), ((ISolidColorBrush)Border(box).BorderBrush!).Color);
            Assert.Equal(new Thickness(1), Border(box).BorderThickness); // no Fluent focus underline
            other.Focus();
            await Until(() => Shadow(box).Color.A == 0);
        }
        plain.Focus();
        plain.SelectionStart = 6;
        plain.SelectionEnd = 10;
        Assert.Equal("beta", plain.SelectedText);
        window.KeyTextInput("changed");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("alpha changed", plain.Text);
        plain.Text = "edited\nsecond line";
        Assert.Contains("second line", plain.Text);
        plain.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.5, plain.Opacity);
    });

    [Fact]
    public Task FocusGlowFollowsTheModAccentAndAnExplicitLocalThemeWins() => Run(async window =>
    {
        var box = new TextBox { Width = 240 };
        var other = new Button { Content = "Other" };
        var localTheme = new ControlTheme(typeof(TextBox))
        {
            BasedOn = (ControlTheme)Application.Current!.FindResource(typeof(TextBox))!,
        };
        localTheme.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(19)));
        var local = new TextBox { Theme = localTheme };
        window.Content = new StackPanel { Children = { box, other, local } };
        window.Show();
        Snap(box);
        box.Focus();
        await Until(() => Shadow(box).Color.A > 110);
        var accent = Color.FromRgb(0x18, 0xB8, 0xE0);
        window.Resources["PinkColor"] = accent;
        window.Resources["PinkBrush"] = new SolidColorBrush(accent);
        await Until(() => Shadow(box).Color.B == accent.B);
        Assert.Equal(accent, ((ISolidColorBrush)Border(box).BorderBrush!).Color);
        Assert.Equal(accent, ((ISolidColorBrush)box.CaretBrush!).Color);
        other.Focus(); // the WPF 200 ms fade itself is asserted in Snap
        await Until(() => Shadow(box).Color.A == 0);
        Assert.Same(localTheme, local.Theme);
        Assert.Equal(new Thickness(19), local.Padding);
    });

    [Fact]
    public Task SwitchingToALocalThemeClearsTheOldFocusGlow() => Run(async window =>
    {
        var box = new TextBox { Width = 240 };
        window.Content = box;
        window.Show();
        Snap(box);
        box.Focus();
        await Until(() => Shadow(box).Color.A > 110);
        box.Theme = new ControlTheme(typeof(TextBox))
        {
            BasedOn = (ControlTheme)Application.Current!.FindResource(typeof(TextBox))!,
        };
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, Shadow(box).Color.A);
    });

    [Fact]
    public Task BorderlessTransparentBoxGetsNoFocusHalo() => Run(async window =>
    {
        // MantraWindow.axaml:50-53 / LockCard / MediaHistory / SettingsPalette .transparent style.
        var style = new Style(x => x.OfType<TextBox>().Class("transparent").Template().OfType<Border>().Name("PART_BorderElement"));
        style.Setters.Add(new Setter(global::Avalonia.Controls.Border.BorderThicknessProperty, new Thickness(0)));
        window.Styles.Add(style);
        var box = new TextBox { Width = 240, Classes = { "transparent" } };
        window.Content = box;
        window.Show();
        Snap(box);
        box.Focus();
        await Until(() => InputFx.GetGlowLevel(box) > 0.44);
        Assert.Equal(0, Shadow(box).Color.A);
    });

    private static Border Border(TextBox box) => box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
    private static BoxShadow Shadow(TextBox box) => Border(box).BoxShadow.Count > 0 ? Border(box).BoxShadow[0] : default;
    private static Color Colour(Window window, string key) => ((ISolidColorBrush)window.FindResource(key)!).Color;
    // P08, no wall clock: Avalonia 12 keeps IClock/ClockBase/Animatable.Clock internal and the headless
    // render timer reads a Stopwatch, so a transition cannot be stepped. Assert the WPF 200 ms fade is
    // configured, then drop it so every glow change lands synchronously.
    private static void Snap(params TextBox[] boxes)
    {
        Dispatcher.UIThread.RunJobs();
        foreach (var box in boxes)
        {
            var fade = Assert.Single(box.Transitions!.OfType<DoubleTransition>(), t => t.Property == InputFx.GlowLevelProperty);
            Assert.Equal(TimeSpan.FromMilliseconds(200), fade.Duration);
            box.Transitions!.Remove(fade);
        }
    }
    private static Task Until(Func<bool> condition)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition());
        return Task.CompletedTask;
    }
    private static Task Run(Func<Window, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new Window { Width = 500, Height = 300 };
        try { await body(window); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
