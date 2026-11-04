using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class AwarenessPulseTests
{
    [Fact]
    public void FeedShowsSnapshotsAndRefreshesAfterWorkerFire() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var engine = new KeywordAudioRuntime();
        var view = new AwarenessTabView { PulseEngine = engine };
        var host = new Window { Content = view };
        var premium = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            host.Show();
            var feed = view.FindControl<StackPanel>("AwarenessPulseFeed")!;
            var count = view.FindControl<TextBlock>("TxtAwarenessFireCount")!;
            Assert.Single(feed.Children);
            Assert.Equal("", count.Text);
            for (var i = 0; i < 6; i++)
                engine.Fires.Record(new KeywordTrigger { Keyword = $"word{i}", Actions = new() { new PlayAudioAction { Enabled = true } } }, "Keyboard", DateTime.Now);
            view.RefreshPulse();
            Assert.Equal(5, feed.Children.Count);
            Assert.Equal("6 fires today", count.Text);
            var first = Assert.IsType<StackPanel>(feed.Children[0]);
            Assert.Equal("\"word5\"", Assert.IsType<TextBlock>(first.Children[0]).Text);
            Assert.Equal("🔊", Assert.IsType<TextBlock>(first.Children[1]).Text);
            Assert.Equal("KEYBOARD", Assert.IsType<TextBlock>(Assert.IsType<Border>(first.Children[2]).Child).Text);
            Assert.Equal("\"word1\"", Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(feed.Children[4]).Children[0]).Text);
            Task.Run(() => engine.FireDemo("live", "Text")).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("7 fires today", count.Text);
            Assert.Equal("\"live\"", Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(feed.Children[0]).Children[0]).Text);
        }
        finally { host.Close(); CoreEntitlement.HasPremiumProvider = premium; }
    });
}
