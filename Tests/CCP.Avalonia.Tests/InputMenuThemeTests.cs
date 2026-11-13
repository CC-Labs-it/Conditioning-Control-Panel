using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class InputMenuThemeTests
{
    [Fact]
    public Task ContextMenuUsesHouseSurfaceAndRespectsLocalValues() => Run(window =>
    {
        var button = new Button();
        var menu = new ContextMenu { Items = { new MenuItem { Header = new Border() } } };
        button.ContextMenu = menu;
        window.Content = button;
        window.Show();
        menu.Open(button);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(Application.Current!.FindResource("InputContextMenuTheme"), menu.Theme);
        Assert.Equal(Colour(window, "ElevatedSurfaceBrush"), ((ISolidColorBrush)menu.Background!).Color);
        Assert.Equal(Colour(window, "TransparentPink20Brush"), ((ISolidColorBrush)menu.BorderBrush!).Color);
        Assert.Equal(new Thickness(1), menu.BorderThickness);
        Assert.Equal(new CornerRadius(10), menu.CornerRadius);
        Assert.Equal(new Thickness(4), menu.Padding);
        var presenter = menu.GetVisualDescendants().OfType<ItemsPresenter>().Single(p => p.Name == "PART_ItemsPresenter");
        Assert.Equal(new Thickness(0), presenter.Margin);

        menu.Background = Brushes.Red;
        menu.CornerRadius = new CornerRadius(8);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colors.Red, ((ISolidColorBrush)menu.Background).Color);
        Assert.Equal(new CornerRadius(8), menu.CornerRadius);
        menu.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task MenuSeparatorUsesTheKeyedWpfInset() => Run(window =>
    {
        var standalone = new Separator();
        var divider = new Separator();
        var button = new Button();
        var menu = new ContextMenu { Items = { new MenuItem { Header = new Border() }, divider } };
        button.ContextMenu = menu;
        window.Content = new StackPanel { Children = { standalone, button } };
        window.Show();
        menu.Open(button);
        Dispatcher.UIThread.RunJobs();

        Assert.NotSame(Application.Current!.FindResource("InputSeparatorTheme"), standalone.Theme);
        Assert.Same(Application.Current.FindResource("InputSeparatorTheme"), divider.Theme);
        Assert.Equal(1, divider.Height);
        Assert.Equal(new Thickness(6, 4), divider.Margin);
        Assert.Equal(Colour(window, "GlassBorderBrush"), ((ISolidColorBrush)divider.Background!).Color);
        var line = divider.GetVisualDescendants().OfType<Border>().Single();
        Assert.Equal(1, line.Bounds.Height);
        Assert.Equal(Colour(window, "GlassBorderBrush"), ((ISolidColorBrush)line.Background!).Color);
        divider.Background = Brushes.Red;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colors.Red, ((ISolidColorBrush)line.Background!).Color);
        menu.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task StandaloneAndMenuBarDividersUseWpfInsets() => Run(window =>
    {
        var standalone = new Separator();
        var divider = new Separator();
        var menu = new Menu { Items = { divider } };
        window.Content = new StackPanel { Children = { standalone, menu } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(Application.Current!.FindResource("InputStandaloneSeparatorTheme"), standalone.Theme);
        Assert.Equal(new Thickness(0, 6), standalone.Margin);
        Assert.Equal(1, standalone.Height);
        Assert.Equal(Colour(window, "GlassBorderBrush"), ((ISolidColorBrush)standalone.Background!).Color);
        Assert.Same(Application.Current.FindResource("InputSeparatorTheme"), divider.Theme);
        Assert.Equal(new Thickness(6, 4), divider.Margin);
        Assert.Equal(1, divider.Height);
        standalone.Margin = new Thickness(0, 0, 0, 10);
        Assert.Equal(new Thickness(0, 0, 0, 10), standalone.Margin);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DeeperFileMenuUsesTheKeyedDividerBrush() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var editor = new DeeperEditorWindow();
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            var menu = editor.GetVisualDescendants().OfType<Menu>().Single();
            var file = (MenuItem)menu.Items[0]!;
            file.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            var dividers = file.Items.OfType<Separator>().ToArray();
            Assert.Equal(2, dividers.Length);
            foreach (var divider in dividers)
            {
                Assert.Same(Application.Current!.FindResource("InputSeparatorTheme"), divider.Theme);
                Assert.Equal(new Thickness(6, 4), divider.Margin);
                Assert.Equal(1, divider.Height);
                Assert.Equal(Colour(editor, "GlassBorderBrush"), ((ISolidColorBrush)divider.Background!).Color);
                var line = divider.GetVisualDescendants().OfType<Border>().Single();
                Assert.Equal(Colour(editor, "GlassBorderBrush"), ((ISolidColorBrush)line.Background!).Color);
            }
            var standalone = editor.FindControl<StackPanel>("RuleRegionDetails")!.Children.OfType<Separator>().Single();
            Assert.Equal(Colour(editor, "DeeperAccentTransparent40Brush"), ((ISolidColorBrush)standalone.Background!).Color);
            dividers[0].Background = Brushes.Red;
            Assert.Equal(Colors.Red, ((ISolidColorBrush)dividers[0].Background!).Color);
        }
        finally
        {
            editor.Close();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current is not null) return;
        var fonts = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(systemEmoji: false);
        var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
        mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
        fonts.FontFamilyMappings = mappings;
        global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Color Colour(Window window, string key) => ((ISolidColorBrush)window.FindResource(key)!).Color;

    private static Task Run(Func<Window, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var window = new Window { Width = 500, Height = 400 };
        try { await body(window); }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
