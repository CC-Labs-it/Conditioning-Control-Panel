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
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Resources/Theme/Inputs.xaml implicit chrome beyond the TextBox (InputTextBoxThemeTests):
/// ComboBox/ComboBoxItem, CheckBox, ListBoxItem, ContextMenu/MenuItem roles and both separator forms.</summary>
public sealed class InputChromeThemeTests
{
    [Fact]
    public Task ComboBoxOpensWithAccentGlowAndDotsTheSelectedRow() => Run(window =>
    {
        var combo = new ComboBox { Width = 200, ItemsSource = new[] { "one", "two" }, SelectedIndex = 1 };
        var own = new ControlTheme(typeof(ComboBoxItem)) { BasedOn = (ControlTheme)Application.Current!.FindResource(typeof(ComboBoxItem))! };
        var keyed = new ComboBox { Width = 200, ItemsSource = new[] { "x" }, SelectedIndex = 0, ItemContainerTheme = own };
        window.Content = new StackPanel { Children = { combo, keyed } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Res("InputComboBoxTheme"), combo.Theme);
        Assert.Equal(new CornerRadius(8), combo.CornerRadius);
        Assert.Equal(new Thickness(10, 0), combo.Padding);
        Assert.Equal(28, combo.MinHeight);
        var chrome = Part<Border>(combo, "Background");
        Assert.Equal(0, chrome.BoxShadow.Count);

        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colour(window, "PinkBrush"), ((ISolidColorBrush)chrome.BorderBrush!).Color);
        Assert.Equal(0x59, chrome.BoxShadow[0].Color.A); // DropShadowEffect Opacity 0.35
        Assert.Equal(10, chrome.BoxShadow[0].Blur);
        var popup = (Border)Part<Popup>(combo, "PART_Popup").Child!;
        Assert.Equal(new CornerRadius(10), popup.CornerRadius);
        Assert.Equal(Colour(window, "ElevatedSurfaceBrush"), ((ISolidColorBrush)popup.Background!).Color);
        var rows = combo.GetRealizedContainers().OfType<ComboBoxItem>().ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Same(Res("InputComboBoxItemTheme"), r.Theme));
        Assert.False(Part<Ellipse>(rows[0], "SelDot").IsVisible);
        Assert.True(Part<Ellipse>(rows[1], "SelDot").IsVisible);
        Assert.Equal(Colour(window, "TransparentPink40Brush"), ((ISolidColorBrush)Part<Border>(rows[1], "Bd").Background!).Color);
        Assert.Equal(Colour(window, "TextLightBrush"), ((ISolidColorBrush)rows[1].Foreground!).Color);
        Assert.Equal(Colour(window, "TextSecondaryBrush"), ((ISolidColorBrush)rows[0].Foreground!).Color);

        combo.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, chrome.BoxShadow.Count);
        keyed.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(own, keyed.GetRealizedContainers().Single().Theme); // a combo's own row theme wins
        return Task.CompletedTask;
    });

    [Fact]
    public Task CheckBoxIsTheHouseDotWithCoreGlowAndThreeState() => Run(window =>
    {
        var wrap = new CheckBox { Width = 160, Content = new TextBlock { Text = string.Join(" ", Enumerable.Repeat("wrapping", 12)), TextWrapping = TextWrapping.Wrap } };
        var bare = new CheckBox { HorizontalAlignment = HorizontalAlignment.Left };
        var three = new CheckBox { IsThreeState = true, IsChecked = null, Content = "three" };
        var toggle = new CheckBox { Theme = Res("ToggleStyle") };
        window.Content = new StackPanel { Children = { wrap, bare, three, toggle } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Res("InputCheckBoxTheme"), wrap.Theme);
        Assert.Same(Res("ToggleStyle"), toggle.Theme); // keyed switches keep their own theme
        Assert.Equal(18, Part<Panel>(wrap, "DotHost").Bounds.Width);
        Assert.True(wrap.Bounds.Height > 40, $"content did not wrap: {wrap.Bounds.Height}");
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

        // check-pop 1 -> 1.18 -> 1 over 140ms, asserted as configured (P08: no stepped clock).
        var pop = Res("InputCheckBoxTheme").Children.OfType<Style>().Single(s => s.Animations.Count > 0);
        Assert.Equal(TimeSpan.FromMilliseconds(140), ((Animation)pop.Animations[0]).Duration);
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
        return Task.CompletedTask;
    });

    [Fact]
    public Task ContextMenuItemsCheckGesturesAndSeparatorsWearWpfChrome() => Run(window =>
    {
        var target = new Border { Width = 100, Height = 40, Background = Brushes.Transparent };
        var check = new MenuItem { Header = "Check", IsChecked = true, InputGesture = new KeyGesture(Key.S, KeyModifiers.Control) };
        var inner = new MenuItem { Header = "Inner" };
        var sub = new MenuItem { Header = "More", Items = { inner } };
        var menuLine = new Separator();
        var menu = new ContextMenu { Items = { check, menuLine, sub } };
        target.ContextMenu = menu;
        var line = new Separator();
        window.Content = new StackPanel { Children = { target, line } };
        window.Show();
        menu.Open(target);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Res("InputContextMenuTheme"), menu.Theme);
        Assert.Equal(new CornerRadius(10), menu.CornerRadius);
        Assert.Equal(Colour(window, "ElevatedSurfaceBrush"), ((ISolidColorBrush)menu.Background!).Color);
        foreach (var item in new[] { check, sub, inner }) Assert.Same(Res("InputMenuItemTheme"), item.Theme);
        var mark = Part<ContentControl>(check, "PART_ToggleIconPresenter");
        Assert.True(mark.IsVisible); // any IsChecked shows the mark, as WPF
        Assert.Equal(IconKind.Checkmark, mark.GetVisualDescendants().OfType<IconGlyph>().Single().Kind);
        var gesture = Part<TextBlock>(check, "PART_InputGestureText");
        Assert.Equal(Colour(window, "TextMutedBrush"), ((ISolidColorBrush)gesture.Foreground!).Color);
        Assert.Equal(new Thickness(18, 0, 0, 0), gesture.Margin);
        check.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colour(window, "TransparentPink20Brush"), ((ISolidColorBrush)Part<Border>(check, "PART_LayoutRoot").Background!).Color);
        Assert.Equal(Colour(window, "TextLightBrush"), ((ISolidColorBrush)Part<ContentPresenter>(check, "PART_HeaderPresenter").Foreground!).Color);

        Assert.Same(Res("InputMenuSeparatorTheme"), menuLine.Theme);
        Assert.Equal(new Thickness(6, 4), menuLine.Margin);
        Assert.Same(Res("InputSeparatorTheme"), line.Theme);
        Assert.Equal(new Thickness(0, 6), line.Margin);
        Assert.Equal(1, line.Bounds.Height);
        Assert.Equal(Colour(window, "GlassBorderBrush"), ((ISolidColorBrush)line.Background!).Color);
        menu.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task MenuBarEntriesUseTheTopLevelRoleAndTheirChildrenTheSubmenuRole() => Run(window =>
    {
        var inner = new MenuItem { Header = "Inner" };
        var top = new MenuItem { Header = "File", Items = { inner } };
        window.Content = new Menu { Items = { top } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Res("InputTopLevelMenuItemTheme"), top.Theme);
        Assert.Same(Res("InputMenuItemTheme"), inner.Theme);
        Assert.Equal(new Thickness(10, 4), top.Padding);
        top.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colour(window, "TransparentPink40Brush"), ((ISolidColorBrush)Part<Border>(top, "PART_LayoutRoot").Background!).Color);
        Assert.Equal(new CornerRadius(10), ((Border)Part<Popup>(top, "PART_Popup").Child!).CornerRadius);
        top.IsSubMenuOpen = false;
        return Task.CompletedTask;
    });

    private static ControlTheme Res(string key) => (ControlTheme)Application.Current!.FindResource(key)!;
    private static T Part<T>(Visual host, string name) where T : Control =>
        host.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    private static Color Colour(Window window, string key) => ((ISolidColorBrush)window.FindResource(key)!).Color;
    private static Task Run(Func<Window, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new Window { Width = 500, Height = 400 };
        try { await body(window); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
