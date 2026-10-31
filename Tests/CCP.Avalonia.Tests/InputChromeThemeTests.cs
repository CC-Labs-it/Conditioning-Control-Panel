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
            var combo = new ComboBox();
            goon.Content = new StackPanel { Children = { check, list, combo } };
            goon.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Application.Current!.FindResource(typeof(CheckBox)), check.Theme);
            Assert.Same(Application.Current.FindResource(typeof(ListBoxItem)), list.ItemContainerTheme);
            Assert.Same(Application.Current.FindResource(typeof(ComboBox)), combo.Theme);
        }
        finally { goon.Close(); }
        return Task.CompletedTask;
    });

    // WPF Inputs.xaml:42-208. Exercise pointer opening, selection, dismissal and local themes.
    [Fact]
    public Task ComboUsesHouseChromeAndParentAlignedDotRows() => Run(window =>
    {
        var combo = new ComboBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0, Width = 220,
            HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Bottom };
        var keyed = new ComboBox { Theme = Res("DarkComboBoxStyle"), ItemsSource = new[] { "local" } };
        window.Content = new StackPanel { Children = { combo, keyed } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        Assert.Same(Res("InputComboBoxTheme"), combo.Theme);
        Assert.Same(Res("DarkComboBoxStyle"), keyed.Theme);
        Assert.Equal(28, combo.MinHeight);
        var chrome = Part<Border>(combo, "Chrome");
        Assert.Equal(new CornerRadius(8), chrome.CornerRadius);
        Assert.Same(combo.Background, chrome.Background);
        combo.Background = Brushes.Navy; Dispatcher.UIThread.RunJobs();
        Assert.Same(Brushes.Navy, chrome.Background);
        var click = combo.TranslatePoint(new Point(10, 10), window)!.Value;
        window.MouseMove(click); window.MouseDown(click, MouseButton.Left); window.MouseUp(click, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsDropDownOpen);
        Assert.Equal(89, chrome.BoxShadow[0].Color.A);
        var popup = Part<Popup>(combo, "PART_Popup");
        var border = Assert.IsType<Border>(popup.Child);
        Assert.Equal(new Thickness(0, 4, 0, 0), border.Margin);
        Assert.Equal(new CornerRadius(10), border.CornerRadius);
        Assert.True(border.Bounds.Width >= combo.Bounds.Width);
        var selected = (ComboBoxItem)combo.ContainerFromIndex(0)!;
        var plain = (ComboBoxItem)combo.ContainerFromIndex(1)!;
        Assert.Same(Res("InputComboBoxItemTheme"), selected.Theme);
        Assert.Equal(HorizontalAlignment.Right, plain.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Bottom, plain.VerticalContentAlignment);
        Assert.Equal(1, Part<Ellipse>(selected, "SelDot").Opacity);
        Assert.Equal(0, Part<Ellipse>(plain, "SelDot").Opacity);
        Assert.Equal(Colour(window, "TransparentPink40Brush"), ((ISolidColorBrush)Part<Border>(selected, "Bd").Background!).Color);
        ((IPseudoClasses)plain.Classes).Set(":pointerover", true); Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colour(window, "TransparentPink20Brush"), ((ISolidColorBrush)Part<Border>(plain, "Bd").Background!).Color);
        plain.IsEnabled = false; Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.45, plain.Opacity);
        plain.IsEnabled = true;
        plain.IsSelected = true; Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("b", combo.SelectionBoxItem);
        Assert.Equal(0, Part<Ellipse>(selected, "SelDot").Opacity);
        combo.IsDropDownOpen = false; Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, chrome.BoxShadow.Count);
        window.MouseMove(new Point(450, 350)); window.MouseMove(click); Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsPointerOver);
        Assert.Same(window.FindResource("InputHoverBorderBrush"), chrome.BorderBrush);
        combo.IsEnabled = false; Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.45, combo.Opacity);
        Assert.Equal("Arrow", combo.Cursor!.ToString());
        combo.IsEnabled = true; combo.ItemsSource = Array.Empty<string>();
        combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
        Assert.Equal(28, border.MinHeight);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SlideRestartsOnOpenAndCancelsOnCloseDisableAndDetach() => Run(async window =>
    {
        var combo = new ComboBox { ItemsSource = new[] { "one", "two" }, Width = 220 };
        window.Content = combo; window.Show(); Dispatcher.UIThread.RunJobs();
        var popup = Part<Popup>(combo, "PART_Popup");
        Assert.True(InputPopupFx.GetSlide(popup));
        var slide = InputPopupFx.CreateSlideAnimation(80);
        Assert.Equal(TimeSpan.FromMilliseconds(150), slide.Duration);
        Assert.Equal(new[] { -80d, 0d }, slide.Children.Select(k => (double)((Setter)k.Setters[0]).Value!));
        combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
        Assert.True(InputPopupFx.IsSliding(popup));
        combo.IsDropDownOpen = false; Dispatcher.UIThread.RunJobs();
        await Task.Delay(20);
        Assert.False(InputPopupFx.IsSliding(popup));
        combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
        Assert.True(InputPopupFx.IsSliding(popup));
        InputPopupFx.SetSlide(popup, false); Dispatcher.UIThread.RunJobs();
        await Task.Delay(20);
        Assert.False(InputPopupFx.IsSliding(popup));
        InputPopupFx.SetSlide(popup, true);
        Assert.True(InputPopupFx.IsSliding(popup));
        window.Close(); Dispatcher.UIThread.RunJobs();
        await Task.Delay(20);
        Assert.False(InputPopupFx.IsSliding(popup));
    });

    [Fact]
    public Task AttentionEditorFontComboUsesTheImplicitTheme() => Run(window =>
    {
        // WPF Dialogs/AttentionTargetEditorDialog.xaml:149: font combo is unkeyed.
        var dialog = new global::ConditioningControlPanel.Avalonia.Views.Dialogs.AttentionTargetEditorDialog();
        try
        {
            var combo = dialog.FindControl<ComboBox>("CmbFont")!;
            // Host the real input without the dialog's platform-specific Segoe UI font.
            window.Content = Detach((Panel)combo.Parent!, combo);
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Same(Res("InputComboBoxTheme"), combo.Theme);
            combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
            Assert.Same(Res("InputComboBoxItemTheme"), combo.ContainerFromIndex(0)!.Theme);
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task QuizTemplateRowsKeepTheirLocalReplacement() => Run(window =>
    {
        var editor = new global::ConditioningControlPanel.Avalonia.Views.Windows.QuizCategoryEditorWindow(null);
        try
        {
            var combo = editor.FindControl<ComboBox>("CboTemplate")!;
            window.Content = Detach((Panel)combo.Parent!, combo);
            window.Show(); Dispatcher.UIThread.RunJobs();
            combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
            Assert.Same(Application.Current!.FindResource(typeof(ComboBoxItem)), combo.ContainerFromIndex(0)!.Theme);
        }
        finally { editor.Close(); }
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
            editor.Show(); Dispatcher.UIThread.RunJobs();
            combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
            var row = (ComboBoxItem)combo.ContainerFromIndex(0)!;
            Assert.Same(editor.FindResource("EditorComboBoxItem"), row.Theme);
            Assert.Equal(new Thickness(8, 5), row.Padding);
            Assert.Equal(new CornerRadius(4), Part<Border>(row, "Bd").CornerRadius);
            ((IPseudoClasses)row.Classes).Set(":pointerover", true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colour(editor, "DeeperMenuItemHoverBgBrush"), ((ISolidColorBrush)Part<Border>(row, "Bd").Background!).Color);
            row.IsSelected = true; Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colour(editor, "DeeperAccentTransparent40Brush"), ((ISolidColorBrush)Part<Border>(row, "Bd").Background!).Color);
            Assert.Empty(row.GetVisualDescendants().OfType<Ellipse>());
        }
        finally { editor.Close(); }
        return Task.CompletedTask;
    });

    private static Control Detach(Panel panel, Control child) { panel.Children.Remove(child); return child; }

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
