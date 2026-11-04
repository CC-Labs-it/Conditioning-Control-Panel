using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AvatarMarkdownLinksTests
{
    private const string Message = "Watch [clip](https://example.com/video) now";

    [Fact]
    public Task LiveAndHistoryMarkdownUseGuardedLauncher() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarMuted = false;
        var oldNavigate = CompanionLinkLauncher.Navigate;
        var oldRelay = RemoteControlTabView.Relay;
        using var relay = new RemoteRelay(() => null, () => null, "test", (_, _) => null, _ => { });
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        _ = RemoteControlTabView.Relay.Value;
        var urls = new List<string>();
        CompanionLinkLauncher.Navigate = url => { urls.Add(url); return true; };
        var parent = new Window();
        var tube = new AvatarTubeWindow(parent);
        try
        {
            parent.Show(); tube.Show();
            tube.GigglePriority(Message, playSound: false);
            Dispatcher.UIThread.RunJobs();
            var live = tube.FindControl<TextBlock>("TxtSpeech")!;
            var liveLink = Link(live);
            liveLink.Command!.Execute(null);
            Assert.Equal(new[] { "https://example.com/video" }, urls);

            tube.ShowChatHistory();
            Dispatcher.UIThread.RunJobs();
            tube.UpdateLayout();
            var history = tube.FindControl<ItemsControl>("ChatHistoryList")!;
            var historyLink = Assert.Single(history.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Content is TextBlock t && t.Text == "clip"));
            typeof(RemoteRelay).GetProperty(nameof(RemoteRelay.ControllerConnected))!.SetValue(relay, true);
            historyLink.Command!.Execute(null);
            Assert.Single(urls);
        }
        finally
        {
            tube.Close(); parent.Close();
            CompanionLinkLauncher.Navigate = oldNavigate;
            RemoteControlTabView.Relay = oldRelay;
        }
        return Task.CompletedTask;
    });

    private static Button Link(TextBlock text)
    {
        var link = Assert.IsType<Button>(Assert.Single(text.Inlines!.OfType<InlineUIContainer>()).Child);
        Assert.Equal("clip", Assert.IsType<TextBlock>(link.Content).Text);
        Assert.Equal(new[] { "Watch ", " now" }, text.Inlines.OfType<Run>().Select(r => r.Text));
        return link;
    }
}
