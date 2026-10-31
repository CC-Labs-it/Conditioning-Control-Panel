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

/// <summary>Inputs.xaml:42-208 implicit combo and popup parity.</summary>
public sealed class InputComboThemeTests
{
    [Fact]
    public Task GoonComboKeepsItsLocalReplacement() => Run(_ =>
    {
        // WPF GoonTestWindow.xaml:86 styles have no BasedOn.
        var goon = new global::ConditioningControlPanel.Avalonia.Views.Windows.GoonTestWindow();
        try
        {
            var combo = new ComboBox();
            goon.Content = new StackPanel { Children = { combo } };
            goon.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Application.Current.FindResource(typeof(ComboBox)), combo.Theme);
        }
        finally
        {
            goon.Close();
        }
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
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Res("InputComboBoxTheme"), combo.Theme);
        Assert.Same(Res("DarkComboBoxStyle"), keyed.Theme);
        var arrow = Part<IconGlyph>(combo, "Arrow");
        Assert.Equal("ChevronDown", arrow.Kind.ToString());
        Assert.Equal(12d, arrow.Size);
        Assert.Same(window.FindResource("TextSecondaryBrush"), arrow.Foreground);
        Assert.Equal(28, combo.MinHeight);
        var chrome = Part<Border>(combo, "Chrome");
        Assert.Equal(new CornerRadius(8), chrome.CornerRadius);
        Assert.Same(combo.Background, chrome.Background);
        combo.Background = Brushes.Navy;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Brushes.Navy, chrome.Background);
        var click = combo.TranslatePoint(new Point(10, 10), window)!.Value;
        window.MouseMove(click);
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsDropDownOpen);
        Assert.Equal(89, chrome.BoxShadow[0].Color.A);
        var popup = Part<Popup>(combo, "PART_Popup");
        // WPF Inputs.xaml:73 keeps the toggle/open state synchronized on outside dismissal.
        // Click on the window outside both the combo and the popup, then reopen with one click.
        var outside = new Point(450, 350);
        window.MouseMove(outside);
        window.MouseDown(outside, MouseButton.Left);
        window.MouseUp(outside, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(0, chrome.BoxShadow.Count);
        Assert.Same(window.FindResource("TextSecondaryBrush"), arrow.Foreground);
        window.MouseMove(click);
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsDropDownOpen);
        Assert.Equal(89, chrome.BoxShadow[0].Color.A);
        Assert.Same(window.FindResource("PinkBrush"), arrow.Foreground);
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
        ((IPseudoClasses)plain.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colour(window, "TransparentPink20Brush"), ((ISolidColorBrush)Part<Border>(plain, "Bd").Background!).Color);
        plain.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.45, plain.Opacity);
        plain.IsEnabled = true;
        plain.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("b", combo.SelectionBoxItem);
        Assert.Equal(0, Part<Ellipse>(selected, "SelDot").Opacity);
        combo.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, chrome.BoxShadow.Count);
        window.MouseMove(new Point(450, 350));
        window.MouseMove(click);
        Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsPointerOver);
        Assert.Same(window.FindResource("InputHoverBorderBrush"), chrome.BorderBrush);
        combo.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.45, combo.Opacity);
        Assert.Equal("Arrow", combo.Cursor!.ToString());
        combo.IsEnabled = true;
        combo.ItemsSource = Array.Empty<string>();
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(28, border.MinHeight);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SlideRestartsOnOpenAndCancelsOnCloseDisableAndDetach() => Run(async window =>
    {
        var combo = new ComboBox { ItemsSource = new[] { "one", "two" }, Width = 220 };
        window.Content = combo;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popup = Part<Popup>(combo, "PART_Popup");
        Assert.True(InputPopupFx.GetSlide(popup));
        var slide = InputPopupFx.CreateSlideAnimation(80);
        Assert.Equal(TimeSpan.FromMilliseconds(150), slide.Duration);
        Assert.Equal(new[] { -80d, 0d }, slide.Children.Select(k => (double)((Setter)k.Setters[0]).Value!));
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(InputPopupFx.IsSliding(popup));
        combo.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(20);
        Assert.False(InputPopupFx.IsSliding(popup));
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(InputPopupFx.IsSliding(popup));
        InputPopupFx.SetSlide(popup, false);
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(20);
        Assert.False(InputPopupFx.IsSliding(popup));
        InputPopupFx.SetSlide(popup, true);
        Assert.True(InputPopupFx.IsSliding(popup));
        window.Close();
        Dispatcher.UIThread.RunJobs();
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
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Res("InputComboBoxTheme"), combo.Theme);
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Res("InputComboBoxItemTheme"), combo.ContainerFromIndex(0)!.Theme);
        }
        finally
        {
            dialog.Close();
        }
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
            window.Show();
            Dispatcher.UIThread.RunJobs();
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Application.Current!.FindResource(typeof(ComboBoxItem)), combo.ContainerFromIndex(0)!.Theme);
        }
        finally
        {
            editor.Close();
        }
        return Task.CompletedTask;
    });

    // WPF Inputs.xaml:191 and DeeperEditorWindow.xaml:449: IsHighlighted includes keyboard focus.
    [Theory]
    [InlineData(false)]
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
