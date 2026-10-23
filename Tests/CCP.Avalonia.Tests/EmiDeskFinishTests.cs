using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

// Runs only in the assembly's CCP_USERDATA_DIR sandbox; no native hotkeys, browser or desktop.
public sealed class EmiDeskFinishTests
{
    private static string Lines(string effect)
    {
        var ids = new[] { "petted", "firstContact", "weekend", "backSoon", "bedtimeBroken", "dismissed",
            "bookOffer", "noMediaYet", "avatarMuted", "avatarKept" };
        return JsonConvert.SerializeObject(new
        {
            version = 1,
            moments = ids.ToDictionary(id => id, id => new
            {
                priority = 3, pools = new[] { id }, scripted = true,
                askOdds = id == "petted" ? 1 : 0,
                limit = new { per = "ever", max = 1 }
            }),
            pools = ids.ToDictionary(id => id, id => new[] { new
            {
                id, t = id == "bedtimeBroken" ? "broken {n}" : id == "backSoon" ? "gone {minutes}"
                    : id == "firstContact" ? "via {via} touring {touring}" : id, spice = 0
            } }),
            asks = new[] { new
            {
                id = "finish.ask", moment = "petted", q = "try?", face = "o_o", chips = new[] { "yes", "no" },
                yes = new { t = "okay", face = "^_^" }, no = new { t = "fine", face = "^_^" }, effect, spice = 0
            } }
        });
    }

    private static FieldInfo Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void Ready(EmiDeskWindow w)
    {
        Field(w, "_transiting").SetValue(w, false);
        typeof(EmiDeskWindow).GetProperty(nameof(EmiDeskWindow.InputLocked))!.SetValue(w, false);
    }

    private static void Land(EmiDeskWindow w)
    {
        w.AdvanceSpeech(); w.AdvanceSpeech(); w.AdvanceSpeech();
    }

    private static void Accept(EmiDeskService desk)
    {
        Ready(desk.Window!);
        desk.Fire("petted");
        Assert.True(desk.Window!.AskLive);
        desk.Window.AdvanceAsk(); desk.Window.AdvanceAsk(); desk.Window.AdvanceAsk();
        Assert.Equal("try?", desk.Window.BubbleText);
        desk.Window.AskChips[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(desk.Window.AskLive);
    }

    private static Task WithDesk(Func<EmiDeskService, Task> body, string effect = "none",
        Action<EmiDeskService>? before = null, string? why = null) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previousSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        var desk = EmiDeskService.Instance;
        var engine = desk.LineEngine;
        var now = desk.Now;
        var askMute = desk.AskMute;
        var saved = JsonConvert.SerializeObject(EmiState.Current);
        var fields = new[] { "_lastDismissUtc", "_outSinceUtc", "_bedtimeSkips" }
            .ToDictionary(n => Field(desk, n), n => Field(desk, n).GetValue(desk));
        var assets = Directory.CreateTempSubdirectory("ccp-emi-finish-").FullName;
        var previousAssets = CorePaths.EffectiveAssetsProvider;
        CoreSettings.ServiceProvider = () => settings;
        CorePaths.EffectiveAssetsProvider = () => assets;
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            CoreSettings.Current.EmiDeskOffers = true;
            CoreSettings.Current.MediaSource = "local";
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            EmiState.Current.IgnoreStreak = 0;
            EmiState.Current.SummonCount = 3;
            EmiState.Current.CodexOpens = 0;
            EmiState.Current.Pins.Clear();
            Field(desk, "_lastDismissUtc").SetValue(desk, DateTime.MinValue);
            Field(desk, "_bedtimeSkips").SetValue(desk, 0);
            desk.Now = () => new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Local); // Friday
            desk.LineEngine = EmiLineEngine.FromJson(Lines(effect));
            before?.Invoke(desk);
            await desk.Summon(why);
            Ready(desk.Window!);
            await body(desk);
        }
        finally
        {
            desk.PanicSilence(); desk.Window?.ShutDown();
            Dispatcher.UIThread.RunJobs();
            desk.ResetMutePrompt(); desk.LineEngine = engine; desk.Now = now; desk.AskMute = askMute;
            foreach (var f in fields) f.Key.SetValue(desk, f.Value);
            JsonConvert.PopulateObject(saved, EmiState.Current,
                new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
            settings.SealForReset(); CoreSettings.ServiceProvider = previousSettings;
            CorePaths.EffectiveAssetsProvider = previousAssets;
            Directory.Delete(assets, true);
        }
    });

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 59, 0)]
    [InlineData(6, 0, 1)]
    [InlineData(23, 59, 1)]
    public void BedtimeUsesTheNextLocalSix(int hour, int minute, int days)
    {
        var local = new DateTime(2026, 10, 23, hour, minute, 0, DateTimeKind.Local);
        Assert.Equal(local.Date.AddDays(days).AddHours(6).ToUniversalTime(), EmiOffers.BedtimeUntil(local));
    }

    [Fact]
    public Task BookChipOpensOneBookAndThenBecomesInfeasible() => WithDesk(desk =>
    {
        Assert.True(EmiOffers.EffectFeasible(" BOOK:OPEN "));
        Assert.False(EmiOffers.EffectFeasible("book:unknown"));
        Accept(desk);
        var book = desk.Window!.Book;
        Assert.NotNull(book);
        Assert.True(book.IsVisible);
        Assert.False(EmiOffers.EffectFeasible("book:open"));
        EmiOffers.Run("book:open", true);
        Assert.Same(book, desk.Window.Book);
        desk.Window.CloseBook();
        Assert.True(EmiOffers.EffectFeasible("book:open"));
        return Task.CompletedTask;
    }, "book:open");

    [Fact]
    public Task PinChipMovesTheTargetToTheFirstSlotAndRefreshesAnOpenRing() => WithDesk(desk =>
    {
        EmiState.Current.Pins.AddRange(new[] { "loom", "sessions", "flashes", "codex", "videos", "subliminals" });
        Assert.True(EmiOffers.EffectFeasible("pinTop:settings"));
        Assert.False(EmiOffers.EffectFeasible("pinTop:missing"));
        desk.Window!.ToggleRing();
        Assert.True(desk.Window.RingOpen);
        Accept(desk);
        Assert.Equal(new[] { "settings", "loom", "sessions", "flashes", "codex", "videos" }, EmiState.Current.Pins);
        Assert.Equal("settings", EmiSuggester.Last[0].Target.Id);
        Assert.False(EmiOffers.EffectFeasible("PINTOP:settings"));
        return Task.CompletedTask;
    }, "pinTop:settings");

    [Fact]
    public Task BedtimeChipPersistsQuietWithoutDismissingHer() => WithDesk(desk =>
    {
        Assert.True(EmiOffers.EffectFeasible("bedtime"));
        Accept(desk);
        Assert.True(EmiLineEngine.BedtimeSet);
        Assert.False(EmiOffers.EffectFeasible("bedtime"));
        Assert.True(desk.IsOut && desk.Window!.IsVisible);
        return Task.CompletedTask;
    }, "bedtime");

    [Fact]
    public Task PackChipUsesTheExistingBrowserOpener() => WithDesk(desk =>
    {
        var previous = AppUpdater.OpenUrl;
        var urls = new List<string>();
        AppUpdater.OpenUrl = (_, url) => { urls.Add(url); return Task.CompletedTask; };
        try { Accept(desk); Assert.Equal(new[] { DiscordLinks.PackCatalogue }, urls); }
        finally { AppUpdater.OpenUrl = previous; }
        return Task.CompletedTask;
    }, "packs");

    [Fact]
    public Task AssetsChipBringsTheShellToAssetsWithoutChangingTheMediaSource() => WithDesk(desk =>
    {
        var previous = EmiOffers.ShellLookup;
        var main = new MainShellWindow();
        EmiOffers.ShellLookup = () => main;
        try
        {
            Assert.True(EmiOffers.EffectFeasible("assets"));
            Accept(desk);
            Assert.True(main.IsVisible);
            Assert.Equal("assets", main.CurrentTab);
            Assert.Equal("local", CoreSettings.Current.MediaSource);
            CoreSettings.Current.MediaSource = "mixed";
            Assert.False(EmiOffers.EffectFeasible("assets"));
        }
        finally { main.Close(); EmiOffers.ShellLookup = previous; }
        return Task.CompletedTask;
    }, "assets");

    [Fact]
    public Task SettingsReloadRebuildsTheRingFromTheSharedPinStore() => WithDesk(desk =>
    {
        var section = new EmiDeskSettingsSection();
        var picker = section.FindControl<EmiRingPicker>("RingPicker")!;
        EmiSuggester.PinToTop("settings");
        section.SyncFromSettings();
        var tiles = (Dictionary<string, ToggleButton>)Field(picker, "_ringTiles").GetValue(picker)!;
        Assert.True(tiles["settings"].IsChecked);
        Assert.True(section.FindControl<Button>("BtnRingReset")!.IsEnabled);
        return Task.CompletedTask;
    });

    [Fact]
    public Task BubbleDodgesTheBookAndReturnsWhenItCloses() => WithDesk(desk =>
    {
        var w = desk.Window!;
        w.Position = new PixelPoint(0, 0);
        w.Say("a short line"); Land(w);
        Dispatcher.UIThread.RunJobs();
        var bubble = (Border)Field(w, "_bubble").GetValue(w)!;
        var normal = Canvas.GetLeft(bubble);
        w.OpenBook();
        var book = w.Book!;
        Field(book, "_onHerLeft").SetValue(book, false);
        w.PostBubbleRelayout(); Dispatcher.UIThread.RunJobs();
        Assert.True(Canvas.GetLeft(bubble) < normal);
        Field(book, "_onHerLeft").SetValue(book, true);
        w.PostBubbleRelayout(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(normal, Canvas.GetLeft(bubble));
        Field(book, "_onHerLeft").SetValue(book, false);
        w.PostBubbleRelayout(); Dispatcher.UIThread.RunJobs();
        w.CloseBook(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(normal, Canvas.GetLeft(bubble));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("hotkey", "via hotkey touring False")]
    [InlineData("tour", "via rail touring True")]
    public Task GreetingCarriesItsRealSummonRoute(string why, string expected) => WithDesk(desk =>
    {
        desk.GreetingDue(); Land(desk.Window!);
        Assert.Equal(expected, desk.Window!.BubbleText);
        return Task.CompletedTask;
    }, why: why);

    [Theory]
    [InlineData(24, true)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public Task WeekendMomentUsesTheLocalDay(int day, bool expected) => WithDesk(desk =>
    {
        Assert.Equal(expected, desk.LineEngine.EverSpent("weekend"));
        return Task.CompletedTask;
    }, before: desk => desk.Now = () => new DateTime(2026, 10, day, 12, 0, 0, DateTimeKind.Local));

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public Task BackSoonUsesTheDismissInstantAndFiveMinuteBoundary(int minutes, bool expected) => WithDesk(async desk =>
    {
        desk.Dismiss();
        // Drive the existing outro completion instead of waiting for its animation timers.
        var finish = typeof(EmiDeskWindow).GetMethod("FinishDismiss", BindingFlags.Instance | BindingFlags.NonPublic)!;
        finish.Invoke(desk.Window, new object?[] { (Action)(() => typeof(EmiDeskService)
            .GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, false)) });
        desk.Now = () => new DateTime(2026, 10, 23, 12, minutes, 0, DateTimeKind.Local);
        await desk.Summon();
        Assert.Equal(expected, desk.LineEngine.EverSpent("backSoon"));
        if (expected) { Land(desk.Window!); Assert.Equal("gone 5", desk.Window!.BubbleText); }
    });

    [Fact]
    public Task BedtimeSummonCountsTheBrokenPromise() => WithDesk(desk =>
    {
        Land(desk.Window!);
        Assert.Equal("broken 1", desk.Window!.BubbleText);
        return Task.CompletedTask;
    }, before: _ => EmiState.Current.BedtimeUntil = DateTime.UtcNow.AddHours(1));

    [Fact]
    public Task EmptyLibraryBeatRechecksMediaAndPanicCancelsBothFollowups() => WithDesk(desk =>
    {
        desk.GreetingDue();
        Assert.False(desk.LineEngine.EverSpent("noMediaYet"));
        Assert.Equal(TimeSpan.FromSeconds(7), ((DispatcherTimer)Field(desk, "_emptyLibraryTimer").GetValue(desk)!).Interval);
        var images = Directory.CreateDirectory(Path.Combine(CorePaths.EffectiveAssets, "images")).FullName;
        var image = Path.Combine(images, "installed.PNG");
        File.WriteAllBytes(image, new byte[1]);
        desk.EmptyLibraryDue();
        Assert.False(desk.LineEngine.EverSpent("noMediaYet"));
        File.Delete(image);
        desk.GreetNow(); desk.EmptyLibraryDue();
        Assert.True(desk.LineEngine.EverSpent("noMediaYet"));
        PanicSurfaces.All.Single(s => s.Id == "emi-desk-speech").Stop(null);
        Assert.Null(Field(desk, "_emptyLibraryTimer").GetValue(desk));
        Assert.Null(Field(desk, "_bookOfferTimer").GetValue(desk));
        desk.BookOfferDue();
        Assert.False(desk.LineEngine.EverSpent("bookOffer"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task SecondSummonOffersTheUnreadBookAfterTwentySeconds() => WithDesk(desk =>
    {
        Assert.Equal(TimeSpan.FromSeconds(20), ((DispatcherTimer)Field(desk, "_bookOfferTimer").GetValue(desk)!).Interval);
        Assert.False(desk.LineEngine.EverSpent("bookOffer"));
        desk.BookOfferDue();
        Assert.True(desk.LineEngine.EverSpent("bookOffer"));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    public Task FirstSummonOrReadBookDoesNotArmAnOffer(int summons, int opens) => WithDesk(desk =>
    {
        Assert.Null(Field(desk, "_bookOfferTimer").GetValue(desk));
        return Task.CompletedTask;
    }, before: _ => { EmiState.Current.SummonCount = summons; EmiState.Current.CodexOpens = opens; });

    [Theory]
    [InlineData("dismiss")]
    [InlineData("presentation")]
    [InlineData("shutdown")]
    public Task LeavingTheDeskCancelsFollowupTimers(string action) => WithDesk(desk =>
    {
        desk.GreetingDue();
        Assert.NotNull(Field(desk, "_emptyLibraryTimer").GetValue(desk));
        Assert.NotNull(Field(desk, "_bookOfferTimer").GetValue(desk));
        if (action == "dismiss") desk.Dismiss();
        else if (action == "presentation") desk.BeginPresentation();
        else desk.Window!.ShutDown();
        Assert.Null(Field(desk, "_emptyLibraryTimer").GetValue(desk));
        Assert.Null(Field(desk, "_bookOfferTimer").GetValue(desk));
        desk.EmptyLibraryDue(); desk.BookOfferDue();
        Assert.False(desk.LineEngine.EverSpent("noMediaYet"));
        Assert.False(desk.LineEngine.EverSpent("bookOffer"));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("images/nested/local.PNG")]
    [InlineData("videos/nested/local.MKV")]
    public Task EmptyLibraryProbeAcceptsNestedLocalMedia(string relative) => WithDesk(_ =>
    {
        Assert.True(EmiOffers.LibraryIsEmpty());
        var path = Path.Combine(CorePaths.EffectiveAssets, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[1]);
        Assert.False(EmiOffers.LibraryIsEmpty());
        File.Delete(path);
        CoreSettings.Current.MediaSource = "mixed";
        Assert.False(EmiOffers.LibraryIsEmpty());
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(EmiMuteChoice.Mute, "avatarMuted")]
    [InlineData(EmiMuteChoice.Keep, "avatarKept")]
    public Task MuteVerdictWaitsForTheGreeting(EmiMuteChoice choice, string moment) => WithDesk(desk =>
    {
        Assert.False(desk.LineEngine.EverSpent(moment));
        desk.GreetingDue();
        Assert.True(desk.LineEngine.EverSpent(moment));
        return Task.CompletedTask;
    }, before: desk =>
    {
        desk.ResetMutePrompt();
        CoreSettings.Current.EmiDeskMuteAvatar = true;
        CoreSettings.Current.EmiDeskMuteDontAsk = false;
        CoreSettings.Current.AiChatEnabled = true;
        desk.AskMute = () => Task.FromResult(choice);
    });
}
