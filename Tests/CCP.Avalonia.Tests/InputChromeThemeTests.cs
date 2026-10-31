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
        finally
        {
            goon.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DeeperEditorCombosUseTheirKeyedRows() => Run(_ =>
    {
        // WPF DeeperEditorWindow.xaml:432 assigns EditorComboBoxItem to the actual input.
        var editor = new global::ConditioningControlPanel.Avalonia.Views.Deeper.DeeperEditorWindow(
            new global::ConditioningControlPanel.Models.Deeper.Enhancement { MediaType = global::ConditioningControlPanel.Models.Deeper.MediaTypes.Audio }, null);
        try
        {
            var combo = editor.FindControl<ComboBox>("CmbHapticPattern")!;
            // This editor tab starts hidden. Retain the real input and its resolved local themes.
            var theme = (ControlTheme)editor.FindResource("EditorComboBox")!;
            Assert.Same(theme, combo.Theme);
            editor.Content = combo.Parent is Panel panel ? Detach(panel, combo) : throw new InvalidOperationException();
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var row = (ComboBoxItem)combo.ContainerFromIndex(0)!;
            Assert.Same(editor.FindResource("EditorComboBoxItem"), row.Theme);
            Assert.Equal(new Thickness(8, 5), row.Padding);
            Assert.Equal(new CornerRadius(4), Part<Border>(row, "Bd").CornerRadius);
            ((IPseudoClasses)row.Classes).Set(":pointerover", true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colour(editor, "DeeperMenuItemHoverBgBrush"), ((ISolidColorBrush)Part<Border>(row, "Bd").Background!).Color);
            row.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colour(editor, "DeeperAccentTransparent40Brush"), ((ISolidColorBrush)Part<Border>(row, "Bd").Background!).Color);
            Assert.Empty(row.GetVisualDescendants().OfType<Ellipse>());
        }
        finally
        {
            editor.Close();
        }
        return Task.CompletedTask;
    });

    // WPF Inputs.xaml:191 and DeeperEditorWindow.xaml:449: IsHighlighted includes keyboard focus.
    [Theory]
    [InlineData(true)]
    public Task ArrowKeysHighlightUnselectedRowsAndKeepSelectedPrecedence(bool deeper) => Run(window =>
    {
        var editor = deeper ? new global::ConditioningControlPanel.Avalonia.Views.Deeper.DeeperEditorWindow(
            new global::ConditioningControlPanel.Models.Deeper.Enhancement { MediaType = global::ConditioningControlPanel.Models.Deeper.MediaTypes.Audio }, null) : null;
        try
        {
            var combo = new ComboBox { ItemsSource = new[] { "one", "two", "three" }, SelectedIndex = 0, Width = 220 };
            if (editor != null) combo.Theme = (ControlTheme)editor.FindResource("EditorComboBox")!;
            window.Content = combo;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(new Point(490, 390));
            combo.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, "");
            Dispatcher.UIThread.RunJobs();
            Assert.True(combo.IsDropDownOpen);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            Dispatcher.UIThread.RunJobs();
            var focused = (ComboBoxItem)combo.ContainerFromIndex(1)!;
            var selected = (ComboBoxItem)combo.ContainerFromIndex(0)!;
            Assert.True(focused.IsFocused);
            Assert.False(focused.IsSelected);
            Assert.False(focused.IsPointerOver);
            Assert.Equal(Colour(window, deeper ? "DeeperMenuItemHoverBgBrush" : "TransparentPink20Brush"), ((ISolidColorBrush)Part<Border>(focused, "Bd").Background!).Color);
            Assert.Equal(Colour(window, "TextLightBrush"), ((ISolidColorBrush)focused.Foreground!).Color);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
            Dispatcher.UIThread.RunJobs();
            Assert.True(selected.IsFocused);
            Assert.True(selected.IsSelected);
            Assert.Equal(Colour(window, deeper ? "DeeperAccentTransparent40Brush" : "TransparentPink40Brush"), ((ISolidColorBrush)Part<Border>(selected, "Bd").Background!).Color);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Dispatcher.UIThread.RunJobs();
            Assert.False(combo.IsDropDownOpen);
        }
        finally
        {
            editor?.Close();
        }
        return Task.CompletedTask;
    });

    private static Control Detach(Panel panel, Control child)
    {
        panel.Children.Remove(child);
        return child;
    }

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
        try
        {
            await body(window);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
