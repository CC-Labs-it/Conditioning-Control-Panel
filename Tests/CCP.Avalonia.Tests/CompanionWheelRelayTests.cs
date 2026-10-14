using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionWheelRelayTests
{
    [Fact]
    public Task RealNotchesStayInTheListUntilAnEdgeAndDetachStopsForwarding() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var inner = new ScrollViewer
        {
            Height = 100, Background = Brushes.White, Content = new Border { Height = 500 },
            IsScrollChainingEnabled = false
        };
        var outer = new ScrollViewer
        {
            Content = new StackPanel { Children = { inner, new Border { Height = 1000 } } }
        };
        var window = new Window { Width = 400, Height = 300, Content = outer };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            CompanionWheelRelay.Attach(inner);
            CompanionWheelRelay.Attach(inner); // Idempotent: exactly one outer movement per notch.
            window.MouseWheel(new Point(50, 50), new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();
            Assert.True(inner.Offset.Y > 0);
            Assert.Equal(0, outer.Offset.Y);
            inner.ScrollToEnd();
            Dispatcher.UIThread.RunJobs();
            window.MouseWheel(new Point(50, 50), new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();
            var oneNotch = outer.Offset.Y;
            Assert.True(oneNotch > 0);
            outer.Offset = default;
            CompanionWheelRelay.Detach(inner);
            window.MouseWheel(new Point(50, 50), new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, outer.Offset.Y);
            CompanionWheelRelay.Attach(inner);
            window.MouseWheel(new Point(50, 50), new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(oneNotch, outer.Offset.Y);
        }
        finally { CompanionWheelRelay.Detach(inner); window.Close(); }
        return Task.CompletedTask;
    });
}
