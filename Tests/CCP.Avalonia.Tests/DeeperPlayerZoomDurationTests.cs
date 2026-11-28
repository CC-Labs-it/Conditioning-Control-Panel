using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public class DeeperPlayerZoomDurationTests
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object obj, string name, params object?[] args) => obj.GetType().GetMethod(name, Flags)!.Invoke(obj, args);
    private static Task Run(Func<EnhancementPlayerWindow, WebHost, Task> test) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var player = new EnhancementPlayerWindow(null, null);
        try { player.Show(); await test(player, player.FindControl<WebHost>("VideoBrowser")!); }
        finally { player.Close(); }
    });

    [Fact]
    public Task ZoomControlsAndMessages() => Run((player, host) =>
    {
        double applied = 0;
        host.ZoomInvoker = (value, _) => applied = value;
        host.BrowserZoomAdapterReady();
        Call(player, "ShowMediaPaneFor", "video");
        Assert.True(player.FindControl<StackPanel>("BrowserZoomCluster")!.IsVisible);
        Call(player, "AdjustVideoZoom", 0.1); Assert.Equal  (1.1, applied, 6);
        for (var i = 0; i < 100; i++) Call(player, "AdjustVideoZoom", -0.1);
        Assert.Equal(0.25, applied, 6);
        for (var i = 0; i < 100; i++) Call(player, "AdjustVideoZoom", 0.1);
        Assert.Equal(5.0, applied, 6);
        host.OnWebMessage("ccp_zoom_out"); Assert.Equal(4.9, applied, 6);
        host.OnWebMessage("ccp_zoom_in"); Assert.Equal(5.0, applied, 6);
        Assert.Contains("ccp_zoom_in", EnhancementPlayerWindow.BrowserFullscreenScript);
        Assert.Contains("ccp_zoom_out", EnhancementPlayerWindow.BrowserFullscreenScript);
        Call(player, "ShowMediaPaneFor", "audio");
        Assert.False(player.FindControl<StackPanel>("BrowserZoomCluster")!.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DurationIsRememberedOncePerVideoLoad() => Run(async (player, host) =>
    {
        var remembered = new List<(string?, double)>();
        player.RememberVideoDuration = (key, duration) => remembered.Add((key, duration));
        string duration = "0";
        host.ScriptInvoker = _ => Task.FromResult<string?>("1|" + duration + "|0");
        var path = Path.Combine(Path.GetTempPath(), "sample.mp4");
        Call(player, "LoadLocalVideo", path);
        await (Task)Call(player, "PollVideoTimeAsync")!;
        duration = "Infinity"; await (Task)Call(player, "PollVideoTimeAsync")!;
        Assert.Empty(remembered);
        duration = "42"; await (Task)Call(player, "PollVideoTimeAsync")!;
        await (Task)Call(player, "PollVideoTimeAsync")!;
        Assert.Single(remembered); Assert.Equal((path, 42d), remembered[0]);
        const string url = "https://hypnotube.com/video/example";
        Call(player, "LoadVideoUrl", url);
        duration = "99"; await (Task)Call(player, "PollVideoTimeAsync")!;
        Assert.Equal(2, remembered.Count); Assert.Equal((url, 99d), remembered[1]);
    });
    [Fact]
    public Task VideoPollCentersLargestVideoOncePerElement() => Run(async (player, host) =>
    {
        string? script = null;
        host.ScriptInvoker = js => { script = js; return Task.FromResult<string?>("1|42|0"); };
        Call(player, "LoadVideoUrl", "https://hypnotube.com/video/example");
        await (Task)Call(player, "PollVideoTimeAsync")!;
        Assert.NotNull(script);
        Assert.Contains("scrollIntoView({block:'center'})", script);
        Assert.Contains("if(!l._ccpScrolled)", script);
        ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(s => s.Id == "deeper-player").Stop(null);
        Assert.False(EnhancementPlayerWindow.IsAnyEnhancementRunning);
    });

}
