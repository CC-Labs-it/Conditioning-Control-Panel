using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

// No windows, layout, native browser or font creation: exercise the actual WebHost event seam.
[Collection(RunsAloneCollection.Name)]
public sealed class DeeperEditorBridgeTests
{
    [Fact]
    public void MessagesNavigationAndDisposalDriveZoomAndFullscreen() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var host = new WebHost();
        var scripts = new List<string>();
        var fullscreen = new List<bool>();
        using var bridge = new DeeperPageBridge(host, fullscreen.Add)
        {
            Invoke = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); }
        };
        host.OnWebMessage("ccp_zoom_in");
        host.OnWebMessage("\"ccp_zoom_in\"");
        host.OnWebMessage("ccp_zoom_out");
        host.OnWebMessage("unrelated");
        Assert.Equal(1.1, bridge.Zoom, 9);
        Assert.Contains("style.zoom='1.1'", scripts[^1]);
        // WPF :576 exits on the native dblclick only; a click-pair detector would fire on scrub + play.
        Assert.Contains("addEventListener('dblclick'", scripts[^1]);
        Assert.DoesNotContain("addEventListener('click'", scripts[^1]);
        host.OnWebMessage("ccp_fullscreen_on");
        host.OnWebMessage("ccp_fullscreen_on");
        Assert.True(bridge.PageFullscreen);
        Assert.Equal(new[] { true }, fullscreen);
        host.OnNavigationCompleted(new Uri("https://example.com/next"));
        Assert.Equal(new[] { true, false }, fullscreen);
        Assert.False(bridge.PageFullscreen);
        Assert.Contains("style.zoom='1.1'", scripts[^1]);
        host.OnWebMessage("ccp_fullscreen_on");
        host.OnWebMessage("ccp_exit_fullscreen");
        Assert.False(bridge.PageFullscreen);
        Assert.Equal(DeeperPageBridge.ExitScript, scripts[^1]);
        host.OnWebMessage("ccp_fullscreen_on");
        bridge.Dispose();
        Assert.False(bridge.PageFullscreen);
        int count = scripts.Count, changes = fullscreen.Count;
        host.OnWebMessage("ccp_zoom_in");
        host.OnWebMessage("ccp_fullscreen_on");
        host.OnNavigationCompleted(new Uri("https://example.com/later"));
        Assert.Equal(count, scripts.Count);
        Assert.Equal(changes, fullscreen.Count);
    });
}
