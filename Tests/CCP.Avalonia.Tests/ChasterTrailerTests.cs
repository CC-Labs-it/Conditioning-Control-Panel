using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChasterTrailerTests
{
    private static void App()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options(systemEmoji: false)).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private const string NoLock = "[]";
    private sealed class FakeChaster : HttpMessageHandler
    {
        public volatile string Locks = NoLock;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(r.RequestUri!.AbsolutePath switch
                {
                    "/auth/profile" => "{\"username\":\"kitten\"}",
                    "/locks" => Locks,
                    _ => "{}",
                }),
            });
    }

    private static async Task WithTab(Func<ChasterTabView, Window, Task> body)
    {
        App();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.MotionLevel = MotionLevel.Full; s.PerformanceMode = false; s.AutoPerformanceMode = false;
        var old = (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPrices, s.ChasterPriceOverrides, s.ChasterFlashDodge);
        var (oldGet, oldSet) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>();   // memory only: never the OS keyring
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPrices, s.ChasterPriceOverrides, s.ChasterFlashDodge) =
            ("l1", true, new List<string> { "attention" }, new Dictionary<string, int>(), false);
        var dir = Directory.CreateTempSubdirectory("ccp-chaster-menu-").FullName;
        new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
        var fake = new FakeChaster();
        var chaster = new ChasterService(new ChasterClient(fake), new SecretChasterTokenStore(), Path.Combine(dir, "chaster_tab.json"),
            () => new ChasterOptions(CoreSettings.Current.ChasterTabEnabled, CoreSettings.Current.ChasterLockId,
                new HashSet<string>(CoreSettings.Current.ChasterPrices ?? new List<string>())));
        ChasterHead.Service = chaster;
        var tab = new ChasterTabView();
        var w = new Window { Width = 1200, Height = 3000, Content = tab };
        try
        {
            w.Show();
            await chaster.RefreshLockAsync();
            tab.OnTabShown();
            Dispatcher.UIThread.RunJobs();
            await body(tab, w);
        }
        finally
        {
            w.Close();
            ChasterHead.Service = null;
            chaster.Dispose();
            new SecretChasterTokenStore().Clear();
            try { Directory.Delete(dir, true); } catch { }
            (s.ChasterLockId, s.ChasterTabEnabled, s.ChasterPrices, s.ChasterPriceOverrides, s.ChasterFlashDodge) = old;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (oldGet, oldSet);
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
    }

    [Fact]
    public Task HoverWaitsThenRetargetsImmediatelyAndClosesAfterTheGrace() => AvaloniaTestDispatcher.RunAsync(() => WithTab((tab, w) =>
    {
        void Hover(Control row) => w.MouseMove(row.TranslatePoint(new Point(20, row.Bounds.Height / 2), w)!.Value);
        Assert.Equal(320, tab.FindControl<Border>("TrailerCard")!.Width);
        Assert.Equal(190, tab.FindControl<Grid>("TrailerPlate")!.Height);
        Assert.Equal(PlacementMode.Bottom, tab.FindControl<Popup>("Trailer")!.Placement);
        var open = tab.TrailerOpenTimer;
        var close = tab.TrailerCloseTimer;
        Assert.Equal(TimeSpan.FromMilliseconds(150), open.Interval); // WPF ChasterTabView.xaml.cs open delay
        Assert.Equal(TimeSpan.FromMilliseconds(140), close.Interval); // WPF close grace
        // A slow first layout inside MouseMove can outlast 150 ms on CI and fire a real tick: park the
        // timers so only the explicit ticks below advance the state (P08).
        open.Interval = close.Interval = TimeSpan.FromHours(1);
        Hover(tab.PriceRows["typo"]);
        Assert.Null(tab.TrailerId);
        Assert.True(open.IsEnabled);
        w.MouseMove(new Point(2, 2));
        Assert.False(open.IsEnabled); // a brief pass over a row never opens the card
        Assert.True(close.IsEnabled);
        tab.TrailerCloseTick();
        Assert.Null(tab.TrailerId);
        Hover(tab.PriceRows["typo"]);
        Assert.True(open.IsEnabled);
        tab.TrailerOpenTick();
        Assert.Equal("typo", tab.TrailerId);
        Hover(tab.PriceRows["session"]);
        Assert.Equal("session", tab.TrailerId);
        Assert.Equal(Loc.Get(TabMenuCopy.FlavourKey("session")), tab.FindControl<TextBlock>("TxtTrailerFlavour")!.Text);
        Assert.False(open.IsEnabled); // an open card switches rows without the delay
        w.MouseMove(new Point(2, 2));
        Assert.Equal("session", tab.TrailerId);
        Assert.True(close.IsEnabled);
        var card = tab.FindControl<Border>("TrailerCard")!;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card, pointer, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.False(close.IsEnabled); // crossing the gap onto the card cancels close
        tab.TrailerCloseTick(); // even a tick already queued keeps the card while the pointer is on it
        Assert.Equal("session", tab.TrailerId);
        card.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, card, pointer, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.True(close.IsEnabled);
        Assert.Equal("session", tab.TrailerId);
        tab.TrailerCloseTick();
        Assert.Null(tab.TrailerId);
        Assert.False(tab.TrailerFigureTicking);
        Assert.False(tab.TrailerDrifting);
        return Task.CompletedTask;
    }));

    [Fact]
    public Task EveryRowHasItsCopyTierAndLiveFiguresAndEditResetRefreshTheCard() => AvaloniaTestDispatcher.RunAsync(() => WithTab(async (tab, w) =>
    {
        foreach (var price in TabPrices.All)
        {
            tab.OpenTrailer(tab.PriceRows[price.Id]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(price.Id, tab.TrailerId);
            if (TabMenuCopy.ArtFor(price.Id) != null) Assert.NotNull(tab.FindControl<Image>("TrailerArt")!.Source);
            Assert.Equal(Loc.Get(TabMenuCopy.FlavourKey(price.Id)), tab.FindControl<TextBlock>("TxtTrailerFlavour")!.Text);
            Assert.Equal(TabMenuCopy.Why(price.Id, Loc.Get, ChasterTabView.ShownSeconds(price.Id)), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
            var tier = tab.FindControl<Border>("TrailerBadgeHost")!.Child as TierBadge;
            Assert.Equal(TabMenuCopy.BadgeTier(price.Gate), tier?.Tier ?? 0);
            Assert.True(tab.TrailerFigureTicking);
            if (BookedFlashPlan.For(price.Id, price.Seconds, AmbientFxCanvas.Env.Level) is { } plan)
            {
                var layer = AdornerLayer.GetAdornerLayer(tab.FindControl<Border>("TrailerFigureAnchor")!)!;
                Assert.Equal(plan.Text, Assert.Single(layer.Children.OfType<ChasterBookedFlash>()).Figure.Text);
            }
        }
        tab.OpenTrailer(tab.PriceRows["typo"]);
        Assert.True(tab.BeginPriceEdit("typo"));
        tab.PriceEditor!.Text = "2:00";
        tab.CommitPriceEdit();
        Dispatcher.UIThread.RunJobs();
        var editedLayer = AdornerLayer.GetAdornerLayer(tab.FindControl<Border>("TrailerFigureAnchor")!)!;
        Assert.Equal(BookedFlashPlan.For("typo", 120, AmbientFxCanvas.Env.Level)!.Value.Text,
            Assert.Single(editedLayer.Children.OfType<ChasterBookedFlash>()).Figure.Text);
        Assert.Equal(TabMenuCopy.Why("typo", Loc.Get, 120), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
        tab.FindControl<Button>("BtnResetPrices")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(TabMenuCopy.Why("typo", Loc.Get, ChasterTabView.ShownSeconds("typo")), tab.FindControl<TextBlock>("TxtTrailerWhy")!.Text);
        Assert.Equal(TimeSpan.FromMilliseconds(1700), tab.TrailerFigureTimer.Interval);
        Assert.True(tab.TrailerDrifting);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(tab.TrailerDrifting);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.True(tab.TrailerDrifting);
        tab.AdvanceTrailerDrift(7);
        var transforms = (TransformGroup)tab.FindControl<Image>("TrailerArt")!.RenderTransform!;
        Assert.Equal(1.09, ((ScaleTransform)transforms.Children[0]).ScaleX, 6);
        tab.AdvanceTrailerDrift(9.1);
        Assert.Equal(-6, ((TranslateTransform)transforms.Children[1]).X, 6);
        tab.IsVisible = false;
        Assert.Null(tab.TrailerId);
        Assert.False(tab.TrailerFigureTicking);
        Assert.False(tab.TrailerDrifting);
        tab.IsVisible = true;
        tab.OpenTrailer(tab.PriceRows["session"]);
        PanicSurfaces.All.Single(s => s.Id == "chaster-trailer").Stop(null);
        Assert.Null(tab.TrailerId);
        Assert.False(tab.TrailerFigureTicking);
        Assert.False(tab.TrailerDrifting);
        tab.OpenTrailer(tab.PriceRows["typo"]);
        await ChasterHead.Service!.UnlinkAsync();
        tab.Refresh(); // unlink hides the menu and trailer
        Assert.Null(tab.TrailerId);
        tab.OpenTrailer(tab.PriceRows["session"]);
        w.Content = null; // detach stops both timers and ambient work
        Assert.Null(tab.TrailerId);
        Assert.False(tab.TrailerFigureTicking);
        Assert.False(tab.TrailerDrifting);
        await Task.CompletedTask;
    }));
}
