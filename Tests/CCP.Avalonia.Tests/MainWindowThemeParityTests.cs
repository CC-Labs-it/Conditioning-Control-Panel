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
    public Task TreeChromeKeepsDefaultsAndCallerBrushes() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var tree = new TreeView { ItemsSource = new[] { "Folder" } };
        var host = new Window { Content = tree, Width = 300, Height = 180 };
        host.Show();
        try
        {
            host.UpdateLayout();
            Assert.Equal(Brushes.Transparent, tree.Background);
            Assert.Equal(new Thickness(0), tree.BorderThickness);
            tree.Background = Brushes.Olive;
            tree.BorderBrush = Brushes.Cyan;
            tree.BorderThickness = new Thickness(2);
            tree.Padding = new Thickness(9);
            host.UpdateLayout();
            var chrome = tree.GetVisualChildren().OfType<Border>().Single();
            Assert.Same(tree.Background, chrome.Background);
            Assert.Same(tree.BorderBrush, chrome.BorderBrush);
            Assert.Equal(tree.BorderThickness, chrome.BorderThickness);
            var scroll = tree.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(v => v.Name == "PART_ScrollViewer");
            Assert.Equal(tree.Padding, scroll.Padding);
            Assert.False(scroll.Focusable);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

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
    // WPF App.xaml:16 loads MainWindow.xaml globally; Windows/LayeredAudioWindow.xaml:61
    // therefore receives MainWindow.xaml:341's 18px PinkSlider, not Fluent's taller blue slider.
    [Fact]
    public Task AudioLayersMasterUsesTheGlobalWpfSliderChrome() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var window = new global::ConditioningControlPanel.Avalonia.Views.Windows.LayeredAudioWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var slider = window.FindControl<Slider>("SliderMaster")!;
            Assert.Equal(18, slider.Bounds.Height);
            Assert.Equal(new Thickness(0), slider.Margin);
            var groove = slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TrackGroove");
            Assert.Equal(3, groove.Height);
            var thumb = slider.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Thumb>().Single();
            Assert.Equal(14, thumb.Bounds.Width);
            Assert.Equal(14, thumb.Bounds.Height);
            var fill = slider.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_DecreaseButton");
            Assert.Same(window.FindResource("PinkBrush"), fill.Background);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // MainWindow.xaml:341 implicit Slider + :409-500 implicit ScrollBar (WPF ScrollViewer keeps
    // the bar beside the content, never over it).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task UnthemedSlidersAndScrollBarsWearTheWpfChrome(bool sliderOnly) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var slider = new Slider();
        var viewer = new ScrollViewer
        {
            Height = 200,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Border { Height = 2000 },
        };
        var panel = new DockPanel();
        DockPanel.SetDock(slider, Dock.Top);
        panel.Children.Add(slider);
        panel.Children.Add(viewer);
        var host = new Window { Width = 300, Height = 300, Content = panel };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            if (sliderOnly)
            {
                Assert.Equal(18, slider.Height);
                Assert.Contains(slider.GetVisualDescendants(), v => v is Border { Name: "TrackGroove" });
                return Task.CompletedTask;
            }

            var bar = viewer.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.ScrollBar>()
                .Single(b => b.Name == "PART_VerticalScrollBar");
            Assert.Equal(8, bar.Bounds.Width);
            var presenter = viewer.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ScrollContentPresenter>().Single();
            Assert.Equal(viewer.Bounds.Width - 8, presenter.Bounds.Width);

            var thumb = bar.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Thumb>().Single();
            Assert.Same(host.FindResource("PanelAccentHoverBrush"), thumb.Background);
            host.MouseMove(thumb.TranslatePoint(new Point(4, 10), host)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(host.FindResource("PinkBrush"), thumb.Background);

            // The track halves are WPF's LineUp/LineDown: one click scrolls one SmallChange.
            bar.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_LineDownButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(viewer.SmallChange.Height, viewer.Offset.Y);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    // WPF Resources/Theme/MainWindow.xaml:750. Close/detach cancel ownership;
    // panic cleanup is stricter than WPF and preserves the base stop ordering.
    [Theory]
    [InlineData("expiry")]
    [InlineData("stale")]
    [InlineData("close")]
    [InlineData("detach")]
    [InlineData("panic")]
    public Task OrdinaryHelpTooltipLifetime(string scenario) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var clock = new TooltipClock();
        var oldTime = global::ConditioningControlPanel.Avalonia.Controls.ToolTipLifetime.Time;
        global::ConditioningControlPanel.Avalonia.Controls.ToolTipLifetime.Time = clock;
        var owner = new Button { Theme = (ControlTheme)Application.Current!.FindResource("HelpButtonStyle")! };
        var host = new Window { Width = 100, Height = 100, Content = owner };
        ToolTip.SetTip(owner, "ordinary help");
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            ToolTip.SetIsOpen(owner, true);
            if (scenario == "expiry")
            {
                clock.Advance(59999);
                Dispatcher.UIThread.RunJobs();
                Assert.True(ToolTip.GetIsOpen(owner));
                clock.Advance(1);
                Dispatcher.UIThread.RunJobs();
                Assert.False(ToolTip.GetIsOpen(owner));
            }
            else if (scenario == "stale")
            {
                clock.Advance(60001); // queue expiry, then reopen before it dispatches
                ToolTip.SetIsOpen(owner, false);
                ToolTip.SetIsOpen(owner, true);
                Dispatcher.UIThread.RunJobs();
                Assert.True(ToolTip.GetIsOpen(owner));
            }
            else
            {
                if (scenario == "close") ToolTip.SetIsOpen(owner, false);
                if (scenario == "detach") host.Content = null;
                if (scenario == "panic")
                    global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All
                        .Single(s => s.Id == "help-tooltips").Stop(null);
                Assert.False(ToolTip.GetIsOpen(owner));
                Assert.True(clock.Last!.Disposed);
            }
        }
        finally
        {
            ToolTip.SetIsOpen(owner, false);
            host.Close();
            global::ConditioningControlPanel.Avalonia.Controls.ToolTipLifetime.Time = oldTime;
        }
        return Task.CompletedTask;
    });

    private sealed class TooltipClock : TimeProvider
    {
        private long _now;
        internal Alarm? Last;
        public override System.Threading.ITimer CreateTimer(System.Threading.TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period) => Last = new Alarm(callback, state, _now + (long)dueTime.TotalMilliseconds);
        internal void Advance(long milliseconds)
        {
            _now += milliseconds;
            if (Last is { Disposed: false } alarm && _now >= alarm.Due) alarm.Fire();
        }
        internal sealed class Alarm(System.Threading.TimerCallback callback, object? state, long due) : System.Threading.ITimer
        {
            internal long Due = due;
            internal bool Disposed;
            internal void Fire() { Due = long.MaxValue; callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
