using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MainWindowThemeParityTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task HelpChromeMatchesWpfAndHoverUsesTheCurrentAccent() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var button = new Button { Theme = (ControlTheme)Application.Current!.FindResource("HelpButtonStyle")! };
        var host = new Window { Width = 100, Height = 100, Content = button };
        host.Resources["PinkBrush"] = Brushes.Cyan;
        host.Resources["TransparentPink20Brush"] = Brushes.DarkCyan;
        host.Resources["PanelBgBrush"] = Brushes.Navy;
        host.Resources["SecondaryBrush"] = Brushes.Olive;
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(new Cursor(StandardCursorType.Help).ToString(), button.Cursor?.ToString());
            Assert.Same(Brushes.Navy, button.Background);
            Assert.Same(Brushes.Olive, button.BorderBrush);
            Assert.Equal(22, button.Width);
            Assert.Equal(22, button.Height);
            Assert.Equal(100, ToolTip.GetShowDelay(button));
            Assert.Equal(0, ToolTip.GetBetweenShowDelay(button));
            var chrome = button.GetVisualDescendants().OfType<Border>().First();
            Assert.Equal(new CornerRadius(11), chrome.CornerRadius);
            var glyph = button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "?");
            Assert.Equal(13, glyph.FontSize);
            Assert.Equal(FontWeight.SemiBold, glyph.FontWeight);
            var point = button.TranslatePoint(new Point(11, 11), host)!.Value;
            host.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Brushes.Cyan, button.Foreground);
            Assert.Same(Brushes.Cyan, button.BorderBrush);
            Assert.Same(Brushes.DarkCyan, chrome.Background);
            host.Resources["PinkBrush"] = Brushes.Lime;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Brushes.Lime, button.Foreground);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AutomaticTooltipHasDarkPinkRoundedChrome() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var tip = new ToolTip { Content = "help" };
        var owner = new Button { Content = "owner" };
        var host = new Window { Width = 100, Height = 100, Content = owner };
        host.Show();
        ToolTip.SetTip(owner, tip);
        ToolTip.SetIsOpen(owner, true);
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(new Thickness(10, 6), tip.Padding);
            Assert.Equal(12, tip.FontSize);
            Assert.Same(tip.FindResource("DarkerBgBrush"), tip.Background);
            Assert.Same(tip.FindResource("PinkBrush"), tip.BorderBrush);
            Assert.Same(tip.FindResource("TextLightBrush"), tip.Foreground);
            var chrome = tip.GetVisualDescendants().OfType<Border>().First();
            Assert.Equal(new CornerRadius(6), chrome.CornerRadius);
            Assert.Null(chrome.Effect);
            // A local radius (RelationshipConstellation sets 10) must still reach the chrome.
            tip.CornerRadius = new CornerRadius(10);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new CornerRadius(10), chrome.CornerRadius);
        }
        finally { ToolTip.SetIsOpen(owner, false); host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task QuestTabsResolveTheSharedWpfTabThemes() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var inactive = (ControlTheme)Application.Current!.FindResource("TabButton")!;
        var active = (ControlTheme)Application.Current.FindResource("TabButtonActive")!;
        var view = new QuestsTabView();
        var host = new Window { Width = 900, Height = 700, Content = view };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.False(view.Resources.ContainsKey("TabButton"));
            Assert.Same(active, view.FindControl<Button>("BtnQuestSubDaily")!.Theme);
            Assert.Same(inactive, view.FindControl<Button>("BtnQuestSubRoadmap")!.Theme);
            Assert.Equal(32, view.FindControl<Button>("BtnQuestSubDaily")!.Height);
            view.FindControl<Button>("BtnQuestSubRoadmap")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(inactive, view.FindControl<Button>("BtnQuestSubDaily")!.Theme);
            Assert.Same(active, view.FindControl<Button>("BtnQuestSubRoadmap")!.Theme);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });
}
