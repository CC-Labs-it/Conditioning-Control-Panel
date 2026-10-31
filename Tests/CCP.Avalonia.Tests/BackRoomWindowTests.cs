using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BackRoomWindowTests
{
    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSecrets = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var service = new SettingsService();
        service.RestoreFrom(new AppSettings());
        var oldSettings = CoreSettings.ServiceProvider;
        var oldServer = GameWindow.Server;
        var oldRelay = GameWindow.BackRoomRelay;
        var oldSignIn = GameWindow.RequestBackRoomSignIn;
        var root = Directory.CreateTempSubdirectory("room-test-").FullName;
        using var server = new ConditioningControlPanel.Avalonia.Platform.WebAssetServer(root);
        CoreSettings.ServiceProvider = () => service;
        GameWindow.Server = () => server;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.OfflineMode = false;
        s.UnifiedId = "room-test-account";
        s.AuthToken = "test-fixture";
        try { body(); }
        finally
        {
            GameWindow.CloseAllForPanic();
            CoreSettings.ServiceProvider = oldSettings;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = oldSecrets;
            GameWindow.Server = oldServer;
            GameWindow.BackRoomRelay = oldRelay;
            GameWindow.RequestBackRoomSignIn = oldSignIn;
            Directory.Delete(root, true);
        }
    });

    // WPF MainWindow.SparkleWallet.cs:85, BackRoomHostService.cs:312-318.
    [Fact]
    public void WalletVisitOpensTheRoomAndSignedOutDoorRequestsLogin() => Run(() =>
    {
        int signIns = 0;
        GameWindow.RequestBackRoomSignIn = () => signIns++;
        CoreSettings.Current.OfflineMode = true;
        Assert.Null(GameWindow.Launch(GameWindow.BackRoomId));
        Assert.Equal(1, signIns);
        CoreSettings.Current.OfflineMode = false;
        using var shell = new ShellLifetime();
        var wallet = shell.Window.Named<SparkleWallet>("HeaderSparkleWallet")!;
        wallet.FindControl<Button>("WalletButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        wallet.FindControl<SparkleWalletInviteCard>("InviteCard")!.FindControl<Button>("VisitButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var room = GameWindow.OpenForTests(GameWindow.BackRoomId);
        Assert.NotNull(room);
        Assert.Same(shell.Window, room.Owner);
        Assert.Contains("backroom/index.html", room.PageUrl!.AbsolutePath);
        Assert.Same(room, GameWindow.Launch(GameWindow.BackRoomId));
        Assert.False(wallet.InviteOpen);
    });

    // WPF BackRoomHostService.cs:399-447,590-648,560-574 and BackRoomBridge.Handle.
    [Fact]
    public void RoomCarrierInitializesRelaysAdoptsAndClosesWithoutCrossAccountWrites() => Run(() =>
    {
        Action<int>? adopt = null;
        var relay = new Relay();
        GameWindow.BackRoomRelay = a => { adopt = a; return relay; };
        CoreSettings.Current.SkillPoints = 12;
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        var room = GameWindow.Launch(GameWindow.BackRoomId)!;
        var sent = new List<JObject>();
        room.Posted += json => sent.Add(JObject.Parse(json));
        void Send(string json) { room.Web.OnWebMessage(json); Dispatcher.UIThread.RunJobs(); }
        Send("{\"type\":\"ready\"}");
        Assert.Empty(sent); // untrusted origin cannot reach the bridge
        room.Web.OnNavigationCompleted(room.PageUrl!);
        Send("{\"type\":\"ready\"}");
        Send("{\"type\":\"ready\"}");
        var init = Assert.Single(sent);
        Assert.Equal("init", (string?)init["type"]);
        Assert.Equal(12, (int)init["sp"]!);
        Assert.Equal("calm", (string?)init["intensity"]);
        Assert.NotEmpty((JObject)init["lex"]!);
        Assert.Contains("slot", init["stations"]!.Values<string>());
        const string request = "{\"type\":\"station-request\",\"reqId\":\"room-request-0001\",\"station\":\"wheel\",\"op\":\"state\"}";
        Send(request); Send(request);
        Assert.Equal(1, relay.Calls);
        Assert.Single(sent.Where(x => (string?)x["type"] == "station-result"));
        adopt!(25); Dispatcher.UIThread.RunJobs();
        Assert.Equal(25, CoreSettings.Current.SkillPoints);
        Assert.DoesNotContain(sent, x => (string?)x["type"] == "balance");
        CoreSettings.Current.SkillPoints = 30; Dispatcher.UIThread.RunJobs();
        Assert.Equal(30, (int)sent.Last()["sp"]!);
        Send("{\"type\":\"room-option\",\"key\":\"invertLook\",\"value\":true}");
        Assert.True(CoreSettings.Current.BackRoomInvertLook);
        Assert.True((bool)sent.Last()["invertLook"]!);
        Send("{\"type\":\"media-request\",\"reqId\":\"room-media-00001\",\"station\":\"slot\"}");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!sent.Any(x => (string?)x["type"] == "media") && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
        var media = sent.Single(x => (string?)x["type"] == "media");
        Assert.Equal(4, media["gifs"]!.Count());
        Assert.StartsWith(room.PageUrl!.GetLeftPart(UriPartial.Authority), (string?)media["gifs"]![0]!["url"]);
        Assert.Equal(PanicPolicy.KeptEscapePage.Room, room.KeptEscapePage);
        room.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs();
        Assert.Equal("suspend", (string?)sent.Last()["type"]);
        Assert.True((bool)sent.Last()["on"]!);
        CoreSettings.Current.UnifiedId = "other-account";
        adopt(99); Dispatcher.UIThread.RunJobs();
        Assert.Equal(30, CoreSettings.Current.SkillPoints);
        Send("{\"type\":\"exit-done\"}");
        Assert.Null(GameWindow.OpenForTests(GameWindow.BackRoomId));
    });

    [Theory]
    [InlineData("tunnel", "true", "BackRoomTunnel", "gates.tunnel")]
    [InlineData("melt", "true", "BackRoomMelt", "gates.melt")]
    [InlineData("intensity", "'full'", "BackRoomFxIntensity", "intensityChoice")]
    [InlineData("subVolume", "42", "BackRoomSubVolume", "audio.sub")]
    [InlineData("sfxVolume", "43", "BackRoomSfxVolume", "audio.sfx")]
    [InlineData("musicVolume", "44", "BackRoomMusicVolume", "audio.music")]
    [InlineData("welcomeSeen", "true", "BackRoomWelcomeSeen", "")]
    public void RoomOptionsPersistAndRepaint(string key, string value, string property, string path) => Run(() =>
    {
        var room = GameWindow.Launch(GameWindow.BackRoomId)!;
        var sent = new List<JObject>();
        room.Posted += json => sent.Add(JObject.Parse(json));
        room.Web.OnNavigationCompleted(room.PageUrl!);
        room.Web.OnWebMessage("{type:'ready'}");
        room.Web.OnWebMessage("{type:'room-option',key:'" + key + "',value:" + value + "}");
        Dispatcher.UIThread.RunJobs();
        var actual = typeof(AppSettings).GetProperty(property)!.GetValue(CoreSettings.Current);
        Assert.Equal(value.Trim('\'').ToLowerInvariant(), actual!.ToString()!.ToLowerInvariant());
        if (path.Length > 0)
        {
            var frame = sent.Last();
            var expected = key.EndsWith("Volume") ? JToken.FromObject(double.Parse(value) / 100) : JToken.Parse(value);
            Assert.Equal(expected, frame.SelectToken(path));
        }
        room.Web.OnWebMessage("{type:'boot-error'}");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(GameWindow.OpenForTests(GameWindow.BackRoomId));
    });

    // WPF MainWindow.xaml.cs:1694-1707,1742-1747; BackRoomHostService.cs:553-585.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanicKeyUsesTheRoomLadderOnlyWithoutOverride(bool overrideAll) => Run(() =>
    {
        var now = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);
        var oldClock = GameWindow.BackRoomUtcNow;
        var oldSurfaces = PanicSurfaces.All;
        int stops = 0;
        GameWindow.BackRoomUtcNow = () => now;
        PanicSurfaces.All = [oldSurfaces.Single(x => x.Id == "games"), new("probe", _ => stops++)];
        try
        {
            using var shell = new ShellLifetime();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true; s.PanicKey = "F8"; s.PanicOverridesAll = overrideAll;
            var room = GameWindow.Launch(GameWindow.BackRoomId)!;
            var sent = new List<JObject>();
            room.Posted += json => sent.Add(JObject.Parse(json));
            room.HandleMessage("{type:'ready'}"); Dispatcher.UIThread.RunJobs(); sent.Clear();
            shell.Window.HandlePanicKeyPress(now); Dispatcher.UIThread.RunJobs();
            Assert.Equal(overrideAll ? 1 : 0, stops);
            if (overrideAll) { Assert.Null(GameWindow.OpenForTests(GameWindow.BackRoomId)); return; }
            Assert.Same(room, GameWindow.OpenForTests(GameWindow.BackRoomId));
            Assert.Equal("suspend", (string?)Assert.Single(sent)["type"]);
            Assert.True((bool)sent[0]["on"]!);
            now = now.AddSeconds(2); // inclusive boundary on WPF's room ladder
            shell.Window.HandlePanicKeyPress(now); Dispatcher.UIThread.RunJobs();
            Assert.Equal("close", (string?)sent.Last()["type"]);
            Assert.Equal("panic", (string?)sent.Last()["reason"]);
            Assert.False(room.KeptEscapeReady);
            Assert.Equal(0, stops); // neither press reaches the global stop or app exit ladder
            room.HandleMessage("{type:'exit-done'}");
            Assert.Null(GameWindow.OpenForTests(GameWindow.BackRoomId));
        }
        finally { GameWindow.BackRoomUtcNow = oldClock; PanicSurfaces.All = oldSurfaces; }
    });

    [Fact]
    public void SuspendedRoomResumesOnlyOnActivationAfterTheDoublePressWindow() => Run(() =>
    {
        var now = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);
        var oldClock = GameWindow.BackRoomUtcNow;
        GameWindow.BackRoomUtcNow = () => now;
        try
        {
            using var shell = new ShellLifetime();
            var room = GameWindow.Launch(GameWindow.BackRoomId)!;
            var sent = new List<JObject>();
            room.Posted += json => sent.Add(JObject.Parse(json));
            Assert.True(GameWindow.TryBackRoomPanic()); Dispatcher.UIThread.RunJobs();
            Assert.True((bool)Assert.Single(sent)["on"]!);
            void ActivateRoom() { shell.Window.Activate(); room.Activate(); Dispatcher.UIThread.RunJobs(); }
            now = now.AddSeconds(2); ActivateRoom(); Assert.Single(sent);
            now = now.AddTicks(1); ActivateRoom();
            Assert.False((bool)sent.Last()["on"]!);
            Assert.Equal("panic", (string?)sent.Last()["reason"]);
            sent.Clear();
            GameWindow.TryBackRoomPanic();
            now = now.AddSeconds(3); GameWindow.TryBackRoomPanic(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, sent.Count);
            Assert.All(sent, frame => Assert.True((bool)frame["on"]!)); // spaced presses suspend again
            Assert.Same(room, GameWindow.OpenForTests(GameWindow.BackRoomId));
        }
        finally { GameWindow.BackRoomUtcNow = oldClock; }
    });

    // WPF MainWindow.xaml.cs:1557,1577-1578: a ready foreground room keeps Escape as page pause.
    [Fact]
    public void ReadyForegroundRoomKeepsEscapeBeforeThePanicLadder() => Run(() =>
    {
        using var shell = new ShellLifetime();
        var s = CoreSettings.Current;
        s.PanicKeyEnabled = true; s.PanicKey = "Escape"; s.PanicOverridesAll = false;
        var room = GameWindow.Launch(GameWindow.BackRoomId)!;
        room.HandleMessage("{type:'ready'}"); Dispatcher.UIThread.RunJobs();
        room.Activate(); Dispatcher.UIThread.RunJobs();
        Assert.True(room.IsActive);
        Assert.True(room.KeptEscapeReady);
        Assert.False(CoreEngine.IsRunning);
        Assert.False(LockCardWindow.IsAnyOpen());
        Assert.Same(room, GameWindow.EscapePauseInFront());
        Assert.Equal("Escape", CoreSettings.Current.PanicKey);
        var sent = new List<JObject>();
        room.Posted += json => sent.Add(JObject.Parse(json));
        shell.Window.HandlePanicKeyPress(DateTime.Now); Dispatcher.UIThread.RunJobs();
        Assert.Equal("kept-escape", (string?)Assert.Single(sent)["type"]);
        Assert.Same(room, GameWindow.OpenForTests(GameWindow.BackRoomId));
    });

    // WPF MainWindow.xaml.cs:1719-1747: DtRH consumes panic before the room, even during boot.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DtrhConsumesLegacyPanicBeforeBackRoom(bool ready) => Run(() =>
    {
        using var shell = new ShellLifetime();
        var s = CoreSettings.Current;
        s.PanicKeyEnabled = true; s.PanicKey = "F8"; s.PanicOverridesAll = false;
        var room = GameWindow.Launch(GameWindow.BackRoomId)!;
        var dtrh = new GameWindow(GameWindow.Games[GameWindow.DtrhId]);
        dtrh.Show();
        if (ready) dtrh.HandleMessage("{type:'ready'}");
        Dispatcher.UIThread.RunJobs();
        var roomFrames = new List<JObject>(); var dtrhFrames = new List<JObject>();
        room.Posted += json => roomFrames.Add(JObject.Parse(json));
        dtrh.Posted += json => dtrhFrames.Add(JObject.Parse(json));
        shell.Window.HandlePanicKeyPress(DateTime.Now); Dispatcher.UIThread.RunJobs();
        Assert.Empty(roomFrames);
        if (ready)
        {
            Assert.Equal("end-run", (string?)Assert.Single(dtrhFrames)["type"]);
            Assert.Equal("host", (string?)dtrhFrames[0]["reason"]);
            Assert.False(dtrh.KeptEscapeReady);
            var until = DateTime.UtcNow.AddSeconds(3);
            while (GameWindow.OpenForTests(GameWindow.DtrhId) != null && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
        }
        Assert.Null(GameWindow.OpenForTests(GameWindow.DtrhId));
        Assert.Same(room, GameWindow.OpenForTests(GameWindow.BackRoomId));
    });

    private sealed class Relay : IBackRoomRelay
    {
        public int Calls;
        public Task<BackRoomStationResult> RelayAsync(string station, string op, string? idem, JObject? body, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new BackRoomStationResult(true, 200, null, new JObject { ["sp"] = 25 }));
        }
    }

    private sealed class ShellLifetime : IDisposable
    {
        internal readonly MainShellWindow Window = new();
        public ShellLifetime() { Window.Show(); Dispatcher.UIThread.RunJobs(); }
        public void Dispose() => Window.Close();
    }
}
