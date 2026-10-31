using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class GameWindowReadyQueueTests
{
    // WPF ChaosWebViewHost.cs:788-800,1711-1715,1740-1748,1770:
    // hold serialized frames, flush in order before page-ready replies, discard on disposal.
    [Theory]
    [InlineData("breakoutdemo")]
    [InlineData("loom")]
    public void NativeDelivery_WaitsForLoadedPageReady_FlushesOnce_AndStopsAtClose(string id)
        => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var root = Directory.CreateTempSubdirectory("game-ready-").FullName;
        using var server = new WebAssetServer(root);
        var window = new GameWindow(GameWindow.Games[id]);
        var abandoned = new GameWindow(GameWindow.Games[id]);
        try
        {
            window.Load(server);
            window.Show();
            var scripts = new List<string>();
            window.Web.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            var first = new JObject { ["type"] = "queued-first", ["value"] = 1 };
            window.Post(first);
            first["value"] = 2; // WPF serializes when Post is called, not when ready arrives.
            window.Post(new { type = "queued-second" });
            Assert.Empty(scripts);
            window.Web.OnNavigationCompleted(new Uri(window.PageUrl!, "/other.html"));
            window.Web.OnWebMessage("{\"type\":\"ready\"}");
            Assert.Empty(scripts);
            Assert.False(window.IsReady);
            window.Web.OnNavigationCompleted(window.PageUrl!);
            window.Web.OnWebMessage("unreadable");
            Assert.Empty(scripts);
            window.Web.OnWebMessage("{\"type\":\"ready\"}");
            Assert.True(window.IsReady);
            Assert.True(scripts.Count >= 3); // queued frames, then the page-ready reply
            Assert.Contains("\"type\":\"queued-first\",\"value\":1", scripts[0]);
            Assert.Contains("\"type\":\"queued-second\"", scripts[1]);
            Assert.Contains(id == "loom" ? "\"type\":\"loom-list\"" : "\"type\":\"init\"", scripts[2]);
            scripts.Clear();
            window.Web.OnWebMessage("{\"type\":\"ready\"}");
            Assert.DoesNotContain(scripts, s => s.Contains("queued-"));
            scripts.Clear();
            window.Post(new { type = "live" });
            Assert.Contains("\"type\":\"live\"", Assert.Single(scripts));
            window.Close();
            scripts.Clear();
            window.Post(new { type = "after-close" });
            window.HandleMessage("{\"type\":\"ready\"}");
            Assert.Empty(scripts);
            Assert.False(window.IsReady);

            abandoned.Load(server);
            abandoned.Show();
            abandoned.Web.ScriptInvoker = window.Web.ScriptInvoker;
            abandoned.Post(new { type = "abandoned" });
            abandoned.Close();
            abandoned.HandleMessage("{\"type\":\"ready\"}");
            Assert.Empty(scripts);
        }
        finally { window.Close(); abandoned.Close(); Directory.Delete(root, true); }
    });
}
