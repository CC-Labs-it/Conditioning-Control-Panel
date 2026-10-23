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

    // MainWindow.xaml:341 implicit Slider + :409-500 implicit ScrollBar (WPF ScrollViewer keeps
    // the bar beside the content, never over it).
    [Fact]
    public Task UnthemedSlidersAndScrollBarsWearTheWpfChrome() => AvaloniaTestDispatcher.RunAsync(() =>
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
            Assert.Equal(18, slider.Height);
            Assert.Contains(slider.GetVisualDescendants(), v => v is Border { Name: "TrackGroove" });

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

    // MainWindow.xaml:1211 SkillTreeScrollBarStyle / :1260 SkillTreeScrollViewerStyle on
    // EnhancementsTabView.xaml:81: one 20px pink-thumb horizontal bar, no vertical bar.
    [Fact]
    public Task SkillTreeScrollerUsesTheWpfSkillTreeScrollChrome() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var tab = new EnhancementsTabView();
        var host = new Window { Width = 1200, Height = 700, Content = tab };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var scroller = tab.FindControl<ScrollViewer>("SkillTreeScroller")!;
            Assert.Same(Application.Current!.FindResource("SkillTreeScrollViewerStyle"), scroller.Theme);
            var bars = scroller.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.ScrollBar>()
                .Where(b => b.TemplatedParent == scroller).ToArray();
            var bar = Assert.Single(bars);
            Assert.Equal(20, bar.Bounds.Height);
            Assert.True(bar.Bounds.Bottom <= scroller.Bounds.Height);
            var thumb = bar.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Thumb>().Single();
            Assert.Same(host.FindResource("PinkBrush"), thumb.Background);

            // Dragging the thumb scrolls the tree (Track.Value is two-way to the ScrollBar).
            var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, 10), host)!.Value;
            host.MouseMove(start);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FF85C4"), ((ISolidColorBrush)thumb.Background!).Color);
            host.MouseDown(start, MouseButton.Left);
            host.MouseMove(start + new Vector(120, 0));
            host.MouseUp(start + new Vector(120, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroller.Offset.X > 0, $"offset {scroller.Offset.X}");
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SharedThemesReachAchievementQuestAndEmoteUserPaths() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var app = Application.Current!;
        var achievements = new AchievementsTabView();
        var quests = new QuestsTabView();
        var remote = new RemoteControlTabView();
        foreach (var key in new[] { "AchievementCard", "AchievementFilterChip", "AnimatedGradientPanel",
                     "AnimatedGradientPanelWide", "EmoteCircleButtonStyle", "EmoteCircleButtonBigStyle",
                     "EmoteEditOverlayStyle", "EmoteCustomBadgeStyle", "EmoteCustomTextBoxStyle" })
        {
            Assert.IsType<ControlTheme>(app.FindResource(key));
            Assert.False(achievements.Resources.ContainsKey(key));
            Assert.False(quests.Resources.ContainsKey(key));
            Assert.False(remote.Resources.ContainsKey(key));
        }
        var box = remote.FindControl<TextBox>("TxtEmoteCustom")!;
        Assert.Same(app.FindResource("EmoteCustomTextBoxStyle"), box.Theme);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CustomEmoteChromeKeepsTheGhostClearOfTypedTextAndUsesTheLiveFocusAccent() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var box = new TextBox
        {
            Theme = (ControlTheme)Application.Current!.FindResource("EmoteCustomTextBoxStyle")!,
            Watermark = "last sent",
        };
        var host = new Window { Width = 300, Height = 100, Content = box };
        host.Resources["PinkBrush"] = Brushes.Cyan;
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var ghost = box.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Placeholder");
            Assert.True(ghost.IsVisible);
            Assert.Equal("last sent", ghost.Text);
            Assert.Equal(FontStyle.Italic, ghost.FontStyle);
            Assert.Equal(Color.Parse("#5A5A75"), ((ISolidColorBrush)ghost.Foreground!).Color);
            Assert.Equal(60, box.MaxLength);
            box.Text = "typing";
            Dispatcher.UIThread.RunJobs();
            Assert.False(ghost.IsVisible);
            box.Text = "";
            box.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(ghost.IsVisible);
            Assert.Equal(Color.Parse("#5A5A75"), ((ISolidColorBrush)ghost.Foreground!).Color); // focused: still WPF's ghost
            var chrome = box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            Assert.Same(Brushes.Cyan, chrome.BorderBrush);
            Assert.Equal(new Thickness(1), chrome.BorderThickness);
            Assert.Equal(Color.Parse("#252542"), ((ISolidColorBrush)chrome.Background!).Color);
            host.Resources["PinkBrush"] = Brushes.Lime;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(Brushes.Lime, chrome.BorderBrush);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ImplicitTreeViewKeepsWpfChromeScrollingAndSelection() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var tree = new TreeView { ItemsSource = Enumerable.Range(0, 40).Select(i => $"folder {i}").ToArray() };
        var host = new Window { Width = 300, Height = 200, Content = tree };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(Brushes.Transparent.Color, ((ISolidColorBrush)tree.Background!).Color);
            Assert.Equal(new Thickness(0), tree.BorderThickness);
            var viewer = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.False(viewer.Focusable);
            Assert.Equal(global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, viewer.VerticalScrollBarVisibility);
            Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
            tree.SelectedItem = "folder 2";
            Assert.Equal("folder 2", tree.SelectedItem);
            viewer.Offset = new Vector(0, 100);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewer.Offset.Y > 0);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OrdinaryHelpTooltipExpiresAtSixtySecondsAndCancelsOnCloseDetachAndPanic() => AvaloniaTestDispatcher.RunAsync(() =>
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
            clock.Advance(59999);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ToolTip.GetIsOpen(owner));
            clock.Advance(1);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ToolTip.GetIsOpen(owner));

            ToolTip.SetIsOpen(owner, true);
            clock.Advance(60000); // queue an expiry, then close/reopen before it dispatches
            ToolTip.SetIsOpen(owner, false);
            ToolTip.SetIsOpen(owner, true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ToolTip.GetIsOpen(owner));
            global::ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All
                .Single(s => s.Id == "help-tooltips").Stop(null);
            Assert.False(ToolTip.GetIsOpen(owner));
            Assert.True(clock.Last!.Disposed);
            ToolTip.SetIsOpen(owner, true);
            host.Content = null;
            Assert.False(ToolTip.GetIsOpen(owner));
            Assert.True(clock.Last!.Disposed);
        }
        finally
        {
            ToolTip.SetIsOpen(owner, false);
            host.Close();
            global::ConditioningControlPanel.Avalonia.Controls.ToolTipLifetime.Time = oldTime;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SmallButtonPressAndReleaseKeepWpfTimingAndBackEase() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var button = new Button { Content = "press", Theme = (ControlTheme)Application.Current!.FindResource("SmallPinkButton")! };
        var host = new Window { Width = 150, Height = 100, Content = button };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var border = button.GetVisualDescendants().OfType<Border>().First();
            var point = button.TranslatePoint(new Point(20, 20), host)!.Value;
            host.MouseMove(point);
            host.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var press = Assert.IsType<global::Avalonia.Animation.TransformOperationsTransition>(Assert.Single(border.Transitions!));
            Assert.Equal(TimeSpan.FromMilliseconds(80), press.Duration);
            host.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var release = Assert.IsType<global::Avalonia.Animation.TransformOperationsTransition>(Assert.Single(border.Transitions!));
            Assert.Equal(TimeSpan.FromMilliseconds(150), release.Duration);
            Assert.Equal(1.025, release.Easing.Ease(0.5), 5); // WPF amplitude 0.3, including the tiny overshoot
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CollapsibleCardAndComboKeepWpfMotionAndLiveAccentChrome() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var card = new Expander { Header = "header", Content = "body", Theme = (ControlTheme)Application.Current!.FindResource("CollapsibleCard")! };
        var combo = new ComboBox { ItemsSource = Enumerable.Range(0, 30).ToArray(), Theme = (ControlTheme)Application.Current.FindResource("DarkComboBoxStyle")! };
        var panel = new StackPanel(); panel.Children.Add(card); panel.Children.Add(combo);
        var host = new Window { Width = 300, Height = 250, Content = panel };
        host.Resources["PinkBrush"] = Brushes.Cyan;
        host.Resources["PinkColor"] = Colors.Cyan;
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var chevron = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "Chevron");
            Assert.Equal(TimeSpan.FromMilliseconds(200), Assert.IsType<global::Avalonia.Animation.TransformOperationsTransition>(Assert.Single(chevron.Transitions!)).Duration);
            card.IsExpanded = false;
            Assert.False(card.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>()
                .Single(c => c.Name == "PART_ContentPresenter").IsVisible);
            var glow = card.GetVisualDescendants().OfType<Border>().Single(b => b.BoxShadow.Count > 0);
            Assert.Null(glow.Effect);
            Assert.Equal(Colors.Cyan.R, glow.BoxShadow[0].Color.R);
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var arrow = combo.GetVisualDescendants().OfType<PathIcon>().Single(p => p.Name == "DropDownGlyph");
            Assert.Same(Brushes.Cyan, arrow.Foreground);
            var chrome = combo.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Background");
            Assert.Same(Brushes.Cyan, chrome.BorderBrush);
            Assert.Equal(10, chrome.BoxShadow[0].Blur);
            var popup = combo.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Popup>().Single();
            var popupBorder = Assert.IsType<Border>(popup.Child);
            Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<global::Avalonia.Animation.TransformOperationsTransition>(Assert.Single(popupBorder.Transitions!)).Duration);
            Assert.Equal(240, popupBorder.GetVisualDescendants().OfType<ScrollViewer>().First().MaxHeight);
        }
        finally { combo.IsDropDownOpen = false; host.Close(); }
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
