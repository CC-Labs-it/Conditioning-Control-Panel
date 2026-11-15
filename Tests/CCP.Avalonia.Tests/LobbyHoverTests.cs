using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LobbyHoverTests
{
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Off)]
    public void CardHoverUsesOneScaleAndRestoresTheRim(MotionLevel motion) => AvaloniaTestDispatcher.Run(() =>
    {
        LobbyDepthSeamTests.Setup();
        var old = CoreSettings.Current.MotionLevel;
        var clock = new Clock();
        AvailableSubjectsTabView.FxTime = clock;
        CoreSettings.Current.MotionLevel = motion;
        var page = new AvailableSubjectsTabView();
        var gates = LobbyGates.From(true, false, false);
        var row = new LobbyRow { Key = "hover", HostName = "Host", Game = LobbyGame.Chess };
        page.PaintSnapshot(new LobbySnapshot(new[] { row }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), gates);
        var window = new Window { Width = 1200, Height = 800, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            page.PaintSnapshot(new LobbySnapshot(new[] { row }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), gates);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            clock.Now += TimeSpan.FromSeconds(1).Ticks; page.StepSubjectsFx();
            var list = page.FindControl<ItemsControl>("AvailableSubjectsList")!;
            var container = list.ContainerFromIndex(0)!;
            var card = container.GetVisualDescendants().OfType<Grid>().Single(x => Equals(x.Tag, "lobby-card"));
            var rim = card.Children.OfType<Border>().Single();
            var sheen = card.Children.OfType<Rectangle>().Single(x => Equals(x.Tag, "lobby-sheen"));
            var paint = ((LobbyCardView)list.Items[0]!).CardRim;
            var at = card.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseMove(at); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, sheen.Opacity);
            Assert.Equal(Color.Parse("#66FF8CC0"), Assert.IsAssignableFrom<ISolidColorBrush>(rim.BorderBrush).Color);
            var scale = Assert.IsType<ScaleTransform>(card.RenderTransform);
            if (motion == MotionLevel.Off) { Assert.Equal(1.02, scale.ScaleX); Assert.False(page.SubjectsFxRunning); }
            else { clock.Now += TimeSpan.FromMilliseconds(150).Ticks; page.StepSubjectsFx(); Assert.Equal(1.02, scale.ScaleX); }
            if (motion == MotionLevel.Full)
            {
                page.LobbyEntrance(new System.Collections.Generic.HashSet<string> { ((LobbyCardView)list.Items[0]!).Id });
                Assert.True(card.IsPointerOver);
                Assert.Same(scale, card.RenderTransform);
                clock.Now += TimeSpan.FromMilliseconds(320).Ticks; page.StepSubjectsFx();
                Assert.Equal(1, scale.ScaleX);
                clock.Now += TimeSpan.FromMilliseconds(280).Ticks; page.StepSubjectsFx();
                Assert.Equal(1, scale.ScaleX);
            }
            window.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, sheen.Opacity); Assert.Same(paint, rim.BorderBrush);
            if (motion != MotionLevel.Off) { clock.Now += TimeSpan.FromMilliseconds(150).Ticks; page.StepSubjectsFx(); }
            Assert.Equal(1, scale.ScaleX); Assert.False(page.SubjectsFxRunning);
            if (motion == MotionLevel.Full)
            {
                window.MouseMove(at); Dispatcher.UIThread.RunJobs();
                clock.Now += TimeSpan.FromMilliseconds(150).Ticks; page.StepSubjectsFx();
                Assert.Equal(1.02, scale.ScaleX);
                window.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs();
                clock.Now += TimeSpan.FromMilliseconds(150).Ticks; page.StepSubjectsFx();
                Assert.Equal(1, scale.ScaleX); Assert.False(page.SubjectsFxRunning);
            }
        }
        finally { window.Close(); CoreSettings.Current.MotionLevel = old; AvailableSubjectsTabView.FxTime = TimeProvider.System; }
    });
    [Fact]
    public void EnterAndLeaveDuringPopKeepTheRingFading() => AvaloniaTestDispatcher.Run(() =>
    {
        LobbyDepthSeamTests.Setup();
        var old = CoreSettings.Current.MotionLevel;
        var clock = new Clock();
        AvailableSubjectsTabView.FxTime = clock;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var page = new AvailableSubjectsTabView();
        var gates = LobbyGates.From(true, false, false);
        var row = new LobbyRow { Key = "pop", HostName = "Host", Game = LobbyGame.Chess };
        var snapshot = new LobbySnapshot(new[] { row }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true);
        var window = new Window { Width = 1200, Height = 800, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            page.PaintSnapshot(snapshot, gates); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            clock.Now += TimeSpan.FromSeconds(1).Ticks; page.StepSubjectsFx();
            var list = page.FindControl<ItemsControl>("AvailableSubjectsList")!;
            var container = list.ContainerFromIndex(0)!;
            var card = container.GetVisualDescendants().OfType<Grid>().Single(x => Equals(x.Tag, "lobby-card"));
            var ring = card.Children.OfType<Rectangle>().Single(x => Equals(x.Tag, "lobby-ring"));
            window.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs();
            page.LobbyEntrance(new System.Collections.Generic.HashSet<string> { ((LobbyCardView)list.Items[0]!).Id });
            var scale = Assert.IsType<ScaleTransform>(card.RenderTransform);
            var spread = Assert.IsType<ScaleTransform>(ring.RenderTransform);
            clock.Now += TimeSpan.FromMilliseconds(100).Ticks; page.StepSubjectsFx();
            double opacity = ring.Opacity, size = spread.ScaleX;
            Assert.InRange(opacity, 0.72, 0.73);
            window.MouseMove(card.TranslatePoint(new Point(20, 20), window)!.Value); Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsPointerOver);
            Assert.Same(scale, card.RenderTransform);
            Assert.Equal(opacity, ring.Opacity);
            clock.Now += TimeSpan.FromMilliseconds(100).Ticks; page.StepSubjectsFx();
            Assert.InRange(ring.Opacity, 0.55, 0.56);
            Assert.True(spread.ScaleX > size);
            opacity = ring.Opacity; size = spread.ScaleX;
            window.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs();
            Assert.False(card.IsPointerOver);
            Assert.Equal(opacity, ring.Opacity);
            clock.Now += TimeSpan.FromMilliseconds(150).Ticks; page.StepSubjectsFx();
            Assert.Equal(1, scale.ScaleX);
            Assert.InRange(ring.Opacity, 0.29, 0.30);
            Assert.True(spread.ScaleX > size);
            Assert.True(page.SubjectsFxRunning);
            clock.Now += TimeSpan.FromMilliseconds(170).Ticks; page.StepSubjectsFx();
            Assert.Equal(0, ring.Opacity);
            Assert.Equal(1.08, spread.ScaleX);
            Assert.Equal(1, scale.ScaleX);
            Assert.False(page.SubjectsFxRunning);
        }
        finally { window.Close(); CoreSettings.Current.MotionLevel = old; AvailableSubjectsTabView.FxTime = TimeProvider.System; }
    });
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
    }
}
