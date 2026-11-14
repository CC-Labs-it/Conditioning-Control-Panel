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

// restack b197 (#4364): one link builder for the live bubble and chat history (AvatarTubeWindow.SpeechLinks.cs).
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
        CompanionLinkLauncher.Navigate = url => { urls.Add(url); return Task.FromResult<bool?>(true); };
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

    [Theory]
    [InlineData("try Naughty Bambi now", "Naughty Bambi", "https://hypnotube.com/video/naughty-bambi-109749.html")]
    [InlineData("try \"Bambi TikTok Mix Eager Slut\" now", "Bambi TikTok Eager Slut", "https://hypnotube.com/video/bambi-tiktok-eager-slut-80971.html")]
    [InlineData("try https://hypnotube.com/video/naughty-bambi-109749.html now", "Naughty Bambi", "https://hypnotube.com/video/naughty-bambi-109749.html")]
    [InlineData("try https://example.com/clever-video now", "Clever Video", "https://example.com/clever-video")]
    [InlineData("try [Naughty Bambi](https://example.com/other) now", "Naughty Bambi", "https://example.com/other")]
    public Task LiveAndHistoryLinkTitlesAndUrls(string message, string label, string url) => AvaloniaTestDispatcher.RunAsync(() =>
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
        CompanionLinkLauncher.Navigate = value => { urls.Add(value); return Task.FromResult<bool?>(true); };
        var parent = new Window();
        var tube = new AvatarTubeWindow(parent);
        try
        {
            parent.Show(); tube.Show();
            tube.GigglePriority(message, playSound: false);
            Dispatcher.UIThread.RunJobs();
            var live = tube.FindControl<TextBlock>("TxtSpeech")!;
            var liveLink = Assert.IsAssignableFrom<Button>(Assert.Single(live.Inlines!.OfType<InlineUIContainer>()).Child);
            Assert.Equal(label, Assert.IsType<TextBlock>(liveLink.Content).Text);
            liveLink.Command!.Execute(null);
            tube.ShowChatHistory();
            Dispatcher.UIThread.RunJobs(); tube.UpdateLayout();
            var history = tube.FindControl<ItemsControl>("ChatHistoryList")!;
            var historyLink = Assert.Single(history.GetVisualDescendants().OfType<Button>(),
                b => b.Content is TextBlock t && t.Text == label);
            historyLink.Command!.Execute(null);
            Assert.Equal(new[] { url, url }, urls);
        }
        finally
        {
            tube.Close(); parent.Close();
            CompanionLinkLauncher.Navigate = oldNavigate;
            RemoteControlTabView.Relay = oldRelay;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task LongLinksWrapWithinLiveAndHistoryText(bool unbroken, bool history) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.AvatarMuted = false;
        var oldNavigate = CompanionLinkLauncher.Navigate;
        var urls = new List<string>();
        CompanionLinkLauncher.Navigate = url => { urls.Add(url); return Task.FromResult<bool?>(true); };
        var parent = new Window();
        var tube = new AvatarTubeWindow(parent);
        var label = unbroken ? new string('x', 200) : string.Join(" ", Enumerable.Repeat("Long linked title", 12));
        try
        {
            parent.Show(); tube.Show();
            tube.GigglePriority("Before [" + label + "](https://example.com/long) after", playSound: false);
            Dispatcher.UIThread.RunJobs(); tube.UpdateLayout();
            TextBlock owner;
            Button link;
            if (history)
            {
                tube.ShowChatHistory();
                Dispatcher.UIThread.RunJobs(); tube.UpdateLayout();
                var list = tube.FindControl<ItemsControl>("ChatHistoryList")!;
                link = Assert.Single(list.GetVisualDescendants().OfType<Button>(),
                    b => b.Content is TextBlock t && t.Text == label);
                owner = Assert.Single(list.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.Inlines?.OfType<InlineUIContainer>().Any(i => i.Child == link) == true);
            }
            else
            {
                owner = tube.FindControl<TextBlock>("TxtSpeech")!;
                link = Assert.IsAssignableFrom<Button>(Assert.Single(owner.Inlines!.OfType<InlineUIContainer>()).Child);
            }
            owner.MaxWidth = 160;
            tube.UpdateLayout(); Dispatcher.UIThread.RunJobs(); tube.UpdateLayout();
            AssertWrapped(link, owner);
            link.Command!.Execute(null);
            Assert.Equal(new[] { "https://example.com/long" }, urls);
        }
        finally { tube.Close(); parent.Close(); CompanionLinkLauncher.Navigate = oldNavigate; }
        return Task.CompletedTask;
    });

    private static void AssertWrapped(Button link, TextBlock owner)
    {
        var label = Assert.IsType<TextBlock>(link.Content);
        Assert.True(owner.Bounds.Width > 1 && owner.Bounds.Width <= 160.5, $"Owner {owner.Bounds}");
        Assert.True(double.IsFinite(link.MaxWidth) && link.MaxWidth <= owner.Bounds.Width,
            $"Link max width {link.MaxWidth}, owner {owner.Bounds}");
        Assert.True(link.Bounds.Width <= owner.Bounds.Width + 0.5, $"Link {link.Bounds}, owner {owner.Bounds}");
        Assert.True(label.Bounds.Height > label.FontSize * 3, $"Label {label.Bounds}");
    }

    private static Button Link(TextBlock text)
    {
        var link = Assert.IsAssignableFrom<Button>(Assert.Single(text.Inlines!.OfType<InlineUIContainer>()).Child);
        Assert.Equal("clip", Assert.IsType<TextBlock>(link.Content).Text);
        Assert.Equal(new[] { "Watch ", " now" }, text.Inlines.OfType<Run>().Select(r => r.Text));
        return link;
    }
}
