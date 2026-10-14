using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralEmbedViewTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task PageHandshakeSendsLatestState_ThenLiveUpdates_DisposeStopsMessages() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var sent = new List<string>();
        using var view = new SpiralEmbedView("map")
        { SendJson = json => { sent.Add(json); return Task.CompletedTask; } };
        int ready = 0;
        view.Ready += (_, _) => ready++;
        view.PostState(new DescentBlock { DevotionDays = 1 });
        view.PostState(new DescentBlock { DevotionDays = 8 });
        view.Receive("{\"type\":\"ready\"}");
        view.Receive("not json");
        Assert.Empty(sent);
        Assert.False(view.IsReady);
        view.Receive("{\"type\":\"spiral:ready\"}");
        Assert.Equal(8, (int)JObject.Parse(Assert.Single(sent))["devotion_days"]!);
        Assert.True(view.IsReady);
        Assert.Equal(1, ready);
        view.Receive("{\"type\":\"spiral:ready\"}");
        Assert.Equal(1, ready);
        view.PostState(new DescentBlock { DevotionDays = 9 });
        Assert.Equal(2, sent.Count);
        Assert.Equal(9, (int)JObject.Parse(sent[1])["devotion_days"]!);
        view.Dispose();
        view.PostState(new DescentBlock { DevotionDays = 10 });
        view.Receive("{\"type\":\"spiral:ready\"}");
        Assert.False(view.IsReady);
        Assert.Equal(2, sent.Count);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SuccessfulNavigationRevealsBeforeReady_FailureDisposesInstead() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var view = new SpiralEmbedView("map");
        int navigated = 0, failed = 0;
        view.Navigated += (_, _) => navigated++;
        view.Failed += (_, _) => failed++;
        view.OnNavigated(new Uri("https://evil.test/"), true);
        Assert.Equal(0, navigated);
        view.OnNavigated(new Uri(view.Url), true);
        Assert.Equal(1, navigated);
        Assert.False(view.IsReady);
        view.OnNavigated(new Uri(view.Url), false);
        Assert.Equal(1, failed);
        Assert.True(view.HasFailed);
        view.Receive("{\"type\":\"spiral:ready\"}");
        Assert.False(view.IsReady);
        view.OnNavigated(new Uri(view.Url), true);
        Assert.Equal(1, navigated);
        return Task.CompletedTask;
    });

    [Fact]
    public Task MissingEngineFailsOnce_AndDropsNativeHost() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var view = new SpiralEmbedView("map");
        int failed = 0;
        view.Failed += (_, reason) => { Assert.False(string.IsNullOrWhiteSpace(reason)); failed++; };
        view.Start();
        view.Start();
        Assert.Equal(1, failed);
        Assert.True(view.HasFailed);
        Assert.Empty(view.Children);
        Assert.Null(view.Host);
        Assert.False(view.IsReady);
        Assert.Equal("https://app.cclabs.app/embed/spiral?mode=map", view.Url);
        return Task.CompletedTask;
    });
}
