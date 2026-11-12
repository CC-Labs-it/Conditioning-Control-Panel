using System;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Race;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private sealed class Fetch : IRaceEngineFetch
    {
        public bool IsInstalled { get; set; }
        public CancellationToken Token;
        public int Calls;
        public IProgress<double>? Progress;
        public TaskCompletionSource<RaceEngineFetchResult> Reply = new();
        public Task<RaceEngineFetchResult> FetchAsync(IProgress<double> progress, CancellationToken cancellationToken)
        { Calls++; Progress = progress; Token = cancellationToken; return Reply.Task; }
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void FirstRace(Action<GameWindow, Fetch> body, bool installed = false) => RunGlassroad("1", (_, _, _) =>
    {
        var oldFetch = GameWindow.RaceEngineFetch;
        var oldOpen = GameWindow.OpenRaceBrowser;
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        var fake = new Fetch { IsInstalled = installed };
        try
        {
            LocalizationManager.Instance.Initialize("en");
            GameWindow.UseLinuxRaceCard = () => true;
            GameWindow.RaceEngineFetch = fake;
            var w = GameWindow.Launch("race")!;
            Assert.Null(w.PageUrl);
            Assert.Same(w, GameWindow.Launch("race"));
            body(w, fake);
        }
        finally { GameWindow.RaceEngineFetch = oldFetch; GameWindow.OpenRaceBrowser = oldOpen; LocalizationManager.Instance.Initialize(oldLanguage); }
    });

    [Fact]
    public void FirstRace_CardOffersBothLabelledFallbacksAndCancel() => FirstRace((w, _) =>
    {
        var card = Assert.IsType<StackPanel>(Assert.IsType<Border>(w.Content).Child);
        Assert.Equal("Racing Thoughts needs its race engine. 124 MB, once.", ((TextBlock)card.Children[0]).Text);
        Assert.Equal("Download", w.RaceDownload.Content);
        Assert.Equal("Cancel", w.RaceCancel.Content);
        Assert.Equal("Play in the app (slower)", w.RaceSlower.Content);
        Assert.Equal("Open in browser", w.RaceBrowser.Content);
        Assert.Equal("Panic cannot close a browser tab.", ((TextBlock)card.Children[5]).Text);
        Click(w.RaceCancel);
        Assert.False(GameWindow.IsAnyOpen());
    });

    [Theory]
    [InlineData(RaceEngineFetchResult.Network, "rt_engine_network")]
    [InlineData(RaceEngineFetchResult.Checksum, "rt_engine_checksum")]
    [InlineData(RaceEngineFetchResult.Storage, "rt_engine_storage")]
    [InlineData(RaceEngineFetchResult.Unsupported, "rt_engine_unsupported")]
    [InlineData(RaceEngineFetchResult.Unavailable, "rt_engine_unavailable")]
    public void FirstRace_FetchResultsNameTheReason(RaceEngineFetchResult result, string key) => FirstRace((w, fake) =>
    {
        Click(w.RaceDownload);
        Assert.False(w.RaceDownload.IsEnabled);
        fake.Progress!.Report(42);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.GetF("rt_engine_progress", 42), w.RaceStatus.Text);
        fake.Reply.SetResult(result);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Get(key), w.RaceStatus.Text);
        Assert.True(w.RaceDownload.IsEnabled);
        Assert.Null(w.PageUrl);
    });

    [Fact]
    public void FirstRace_InstalledLaunchesShellWithHandoffRoomAndClosesCard() => FirstRace((w, fake) =>
    {
        var oldLaunch = GameWindow.LaunchRaceShell;
        using var process = LinuxRaceHostTests.StartCat();
        LinuxRaceHost? host = null;
        try
        {
            int calls = 0;
            Uri? url = null;
            GameWindow.LaunchRaceShell = (session, uri) => { calls++; url = uri; return host = new LinuxRaceHost(session, process); };
            w.HandoffRaceRoom("abcdef123456");
            Click(w.RaceDownload);
            fake.Reply.SetResult(RaceEngineFetchResult.Installed);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, calls);
            Assert.Contains("join=abcdef123456", url!.Query);
            Assert.False(w.IsVisible);
            Assert.True(GameWindow.IsAnyOpen());
        }
        finally { GameWindow.CloseAllForPanic(); host?.Dispose(); GameWindow.LaunchRaceShell = oldLaunch; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstRace_InstalledShellFailureKeepsCard(bool throws) => FirstRace((w, fake) =>
    {
        var oldLaunch = GameWindow.LaunchRaceShell;
        try
        {
            int calls = 0;
            GameWindow.LaunchRaceShell = (_, _) => { calls++; if (throws) throw new InvalidOperationException(); return null; };
            Click(w.RaceDownload);
            fake.Reply.SetResult(RaceEngineFetchResult.Installed);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, calls);
            Assert.True(w.IsVisible);
            Assert.Equal(Loc.Get("rt_engine_start_failed"), w.RaceStatus.Text);
            Assert.True(w.RaceDownload.IsEnabled);
        }
        finally { GameWindow.LaunchRaceShell = oldLaunch; }
    });

    [Fact]
    public void FirstRace_InstalledEngineReportsMissingHost() => FirstRace((w, _) =>
    {
        Assert.Equal(Loc.Get("rt_engine_start_failed"), w.RaceStatus.Text);
        Assert.Null(w.PageUrl);
    }, installed: true);

    [Fact]
    public void FirstRace_MissingAndThrowingFetcherFailWithCopy() => FirstRace((w, fake) =>
    {
        GameWindow.RaceEngineFetch = null;
        Click(w.RaceDownload);
        Assert.Equal(Loc.Get("rt_engine_unavailable"), w.RaceStatus.Text);
        GameWindow.RaceEngineFetch = fake;
        Click(w.RaceDownload);
        fake.Reply.SetException(new InvalidOperationException());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Get("rt_engine_failed"), w.RaceStatus.Text);
        Assert.True(w.RaceDownload.IsEnabled);
    });

    [Fact]
    public void FirstRace_CancelAndPanicCancelFetchAndIgnoreLateCompletion() => FirstRace((w, fake) =>
    {
        Click(w.RaceDownload);
        Click(w.RaceCancel);
        Assert.True(fake.Token.IsCancellationRequested);
        Assert.False(w.RaceDownload.IsEnabled);
        Click(w.RaceDownload);
        Assert.Equal(1, fake.Calls);
        fake.Reply.SetResult(RaceEngineFetchResult.Installed);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.RaceDownload.IsEnabled);
        Assert.Equal("", w.RaceStatus.Text);
        Assert.Null(w.PageUrl);
        var second = new Fetch();
        var oldLaunch = GameWindow.LaunchRaceShell;
        int launches = 0;
        try
        {
            GameWindow.LaunchRaceShell = (_, _) => { launches++; return null; };
            GameWindow.RaceEngineFetch = second;
            Click(w.RaceDownload);
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(null!);
            Assert.True(second.Token.IsCancellationRequested);
            second.Reply.SetResult(RaceEngineFetchResult.Installed);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
            Assert.Null(w.PageUrl);
            Assert.Equal(0, launches);
        }
        finally { GameWindow.LaunchRaceShell = oldLaunch; }
    });

    [Fact]
    public void FirstRace_SlowerStartsOwnedWebHostAndPanicClosesIt() => FirstRace((w, fake) =>
    {
        Click(w.RaceDownload);
        Click(w.RaceSlower);
        Assert.True(fake.Token.IsCancellationRequested);
        Assert.EndsWith("/glassroad/index.html", w.PageUrl!.AbsolutePath);
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal("ok", w.CheckGlassroadWatch(DateTime.UtcNow));
        GameWindow.CloseAllForPanic();
        Assert.False(GameWindow.IsAnyOpen());
        fake.Reply.SetResult(RaceEngineFetchResult.Installed);
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
    });

    /// <summary>Linux beta 1 ships no engine fetch (the owned Electron host waits on the bench reply): the card says
    /// so before anyone presses Download and leaves only the two labelled fallbacks, which still work.</summary>
    [Fact]
    public void FirstRace_NoEngineInThisBuild_CardSaysSoAndKeepsTheFallbacks() => RunGlassroad("1", (_, _, _) =>
    {
        var (oldFetch, oldOpen, oldLanguage) = (GameWindow.RaceEngineFetch, GameWindow.OpenRaceBrowser, LocalizationManager.Instance.CurrentLanguage);
        try
        {
            LocalizationManager.Instance.Initialize("en");
            GameWindow.UseLinuxRaceCard = () => true;
            GameWindow.RaceEngineFetch = null;
            var w = GameWindow.Launch("race")!;
            Assert.Null(w.PageUrl);
            Assert.False(w.RaceDownload.IsEnabled);
            Assert.Equal("Race engine download is unavailable in this build.", w.RaceStatus.Text);
            Assert.True(w.RaceSlower.IsEnabled && w.RaceBrowser.IsEnabled);
            var card = Assert.IsType<StackPanel>(Assert.IsType<Border>(w.Content).Child);
            Assert.Equal("Panic cannot close a browser tab.", ((TextBlock)card.Children[5]).Text);
            GameWindow.OpenRaceBrowser = _ => false;
            Click(w.RaceBrowser);
            Assert.False(w.RaceDownload.IsEnabled);   // a failed browser open never re-offers a download
            Click(w.RaceSlower);
            Assert.EndsWith("/glassroad/index.html", w.PageUrl!.AbsolutePath);
            GameWindow.CloseAllForPanic();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally { GameWindow.RaceEngineFetch = oldFetch; GameWindow.OpenRaceBrowser = oldOpen; LocalizationManager.Instance.Initialize(oldLanguage); }
    });

    [Fact]
    public void FirstRace_BrowserUsesFixedUrlAndReportsFailure() => FirstRace((w, fake) =>
    {
        string? opened = null;
        GameWindow.OpenRaceBrowser = url => { opened = url; return false; };
        Click(w.RaceDownload);
        Click(w.RaceBrowser);
        Assert.True(fake.Token.IsCancellationRequested);
        Assert.Equal(GameWindow.RaceBrowserUrl, opened);
        Assert.Equal(Loc.Get("rt_engine_browser_failed"), w.RaceStatus.Text);
        Assert.True(GameWindow.IsAnyOpen());
        GameWindow.OpenRaceBrowser = url => { opened = url; return true; };
        Click(w.RaceBrowser);
        Assert.Equal("https://app.cclabs.app/race3d", opened);
        Assert.False(GameWindow.IsAnyOpen());
        fake.Reply.SetResult(RaceEngineFetchResult.Network);
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
    });
}
