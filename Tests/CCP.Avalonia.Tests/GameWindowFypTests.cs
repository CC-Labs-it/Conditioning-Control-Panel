using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The For You feed on the shared game host (WPF Services/Fyp/FypHostService.cs): ShowTab("fyp") opens it
/// behind OpenFypFeed's premium gate, panic closes it, and the page's frames run the Core FypRules. No network:
/// the remote batch and the sub probe are seams.</summary>
public sealed class GameWindowFypTests
{
    private static void Run(Action<MainShellWindow, string> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = true;
            s.WhatMovedCardShown = 1;   // P52/P76
            var root = Directory.CreateTempSubdirectory("fyp-host-").FullName;
            var lib = Directory.CreateDirectory(Path.Combine(root, "lib")).FullName;
            var server = new WebAssetServer(root);
            var (oldServer, oldPremium, oldDenied, oldAssets, oldXp) = (GameWindow.Server, CoreEntitlement.HasPremiumProvider,
                CoreEntitlement.ShowDeniedHandler, CorePaths.EffectiveAssetsProvider, CoreProgression.AddXPProvider);
            GameWindow.Server = () => server;
            CorePaths.EffectiveAssetsProvider = () => lib;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(shell, lib);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                shell.Close();
                (GameWindow.Server, CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler) = (oldServer, oldPremium, oldDenied);
                (CorePaths.EffectiveAssetsProvider, CoreProgression.AddXPProvider) = (oldAssets, oldXp);
                CoreSettings.ServiceProvider = null;
                server.Dispose();
                Directory.Delete(root, true);
            }
        });
    }

    /// <summary>WPF ShowTab("fyp") -> OpenFypFeed (MainWindow.Lab.cs:234), refused out loud; panic closes it (GameSurfaces.cs:56).</summary>
    [Fact]
    public void ShowTabFyp_IsGatedOpensTheFeed_AndPanicClosesIt() => Run((shell, _) =>
    {
        var denied = 0;
        CoreEntitlement.ShowDeniedHandler = _ => denied++;
        CoreEntitlement.HasPremiumProvider = () => false;
        shell.ShowTab("fyp");
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.Equal(1, denied);
        CoreEntitlement.HasPremiumProvider = () => true;
        var score = EmiSuggester.ScoreOf("fyp");
        var tab = shell.CurrentTab;
        shell.ShowTab("fyp");
        Dispatcher.UIThread.RunJobs();
        Assert.True(GameWindow.IsAnyOpen());
        var w = GameWindow.Launch(GameWindow.FypId);   // focuses the live one
        Assert.NotNull(w);
        Assert.Equal(GameWindow.FypId, w!.Spec.Id);
        Assert.EndsWith("/fyp/index.html", w.PageUrl!.AbsolutePath);
        Assert.Equal(tab, shell.CurrentTab);                 // a window, not a tab
        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.True(EmiSuggester.ScoreOf("fyp") > score);   // WPF Launch NoteOpen("fyp")
        // The EMI door opens the feed itself (WPF EmiTargets.cs:215), not the panel.
        shell.Hide();
        ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk.EmiTargets.Find("fyp")!.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.True(GameWindow.IsAnyOpen());
        Assert.False(shell.IsVisible);
    });

    /// <summary>WPF OnPageMessage: init on this window's asset gate, WPF's own ghost/eye refusals, clip XP.</summary>
    [Fact]
    public void Frames_InitRefusalsAndXp() => Run((_, lib) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        Directory.CreateDirectory(Path.Combine(lib, "videos"));
        File.WriteAllBytes(Path.Combine(lib, "videos", "clip.mp4"), new byte[] { 1 });
        CoreSettings.Current.FypEyeControl = true;
        var xp = new List<double>();
        CoreProgression.AddXPProvider = (amount, _) => xp.Add(amount);
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        var init = sent.Single();
        Assert.Equal("init", (string?)init["type"]);
        Assert.False((bool)init["settings"]!["eyeControl"]!);
        Assert.EndsWith("/ccp.assets/videos/clip.mp4", (string?)init["assets"]!.Single()["url"]);
        Assert.Equal(new Uri((string)init["assets"]![0]!["url"]!).Authority, w.PageUrl!.Authority);
        sent.Clear();
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":true}");
        Assert.Equal(new[] { "clickThrough", "ghost-unavailable" }, sent.Select(o => (string?)o["type"]));
        Assert.False((bool)sent[0]["on"]!);
        sent.Clear();
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
        Assert.False(CoreSettings.Current.FypEyeControl);
        Assert.Equal("eyeStatus", (string?)sent.Single()["type"]);
        Assert.False((bool)sent[0]["enabled"]!);
        Assert.Equal("unsupported", (string?)sent[0]["reason"]);   // the camera exists; this host does not drive it
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"muted\",\"value\":true}");
        Assert.True(CoreSettings.Current.FypMuted);
        w.HandleMessage("{\"type\":\"clip-viewed\",\"segId\":\"videos/clip.mp4\"}");
        w.HandleMessage("{\"type\":\"attention-hit\"}");
        Assert.Equal(new[] { 5.0, 15.0 }, xp);
    });

    /// <summary>WPF ServeRemoteBatch / ProbeCustomSub: no media without consent; a found sub is committed.</summary>
    [Fact]
    public void RemoteBatchNeedsConsent_ProbeCommits() => Run((_, _) =>
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var w = GameWindow.Launch(GameWindow.FypId)!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        int fetches = 0;
        w.FypFetchOverride = _ =>
        {
            fetches++;
            var entry = new ConditioningControlPanel.Services.Fyp.FypAssetManifest.Entry { Id = "scrolller/a/1", Url = "https://cdn.invalid/1.mp4" };
            return Task.FromResult(new FypOnlineCoordinator.FeedBatch(new() { entry }, null, false, 7));
        };
        w.FypProbeOverride = (_, _) => Task.FromResult(new SubProbe { Ok = true, VideoCount = 9 });
        Wait(w.ServeFypRemoteBatch());
        Assert.Equal(0, fetches);                            // no consent: never asked
        var s = CoreSettings.Current;
        s.FypOnlineConsented = true;
        s.FypSource = "online";
        Wait(w.ServeFypRemoteBatch());
        Assert.Equal(1, fetches);
        Assert.Equal(new[] { "assets-append", "online-status" }, sent.Select(o => (string?)o["type"]));
        Assert.Equal(7, (int)sent[1]["poolTotal"]!);
        sent.Clear();
        w.HandleMessage("{\"type\":\"probe-sub\",\"sub\":\"hypnofeed\"}");
        Wait(w.FypLastProbe!);
        Assert.Equal(new[] { "sub-probe", "library" }, sent.Select(o => (string?)o["type"]));
        Assert.Contains("hypnofeed", s.FypOnlineCustomSubs);
    });

    private static void Wait(Task t)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!t.IsCompleted && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
        Assert.True(t.IsCompletedSuccessfully);
        Dispatcher.UIThread.RunJobs();
    }
}
