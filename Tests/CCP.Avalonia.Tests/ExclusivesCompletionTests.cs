using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesTimingTests
{
    [Fact]
    public void EntranceHidesUntilItsTurnCapsDelayAndNeverOvershootsOpacity()
    {
        Assert.Equal((0d, 18d), ExclusivesTabView.EntranceAt(37, 1, MotionLevel.Full));
        Assert.Equal((0d, 18d), ExclusivesTabView.EntranceAt(607, 40, MotionLevel.Full));
        Assert.Equal((0.75, 0d), ExclusivesTabView.EntranceAt(120, 1, MotionLevel.Reduced));
        Assert.Equal((1d, 0d), ExclusivesTabView.EntranceAt(0, 20, MotionLevel.Off));
        for (int ms = 0; ms < 1500; ms++) Assert.InRange(ExclusivesTabView.EntranceAt(ms, 0, MotionLevel.Full).Opacity, 0, 1);
        Assert.True(ExclusivesTabView.EntranceAt(300, 0, MotionLevel.Full).Y < 0); // WPF BackEase, amplitude .35
    }
}

public sealed class ExclusivesCompletionTests
{
    private static void Platform()
    {
        if (Application.Current == null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task PlansIsInlineLocalizedAndLinksToTheFullShelf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var lang = LocalizationManager.Instance.CurrentLanguage;
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode);
        MainShellWindow? shell = null;
        try
        {
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
            shell = new MainShellWindow { Width = 1400, Height = 900 }; shell.Show();
            shell.OpenAppSettingsSection("account"); Settle();
            var plans = shell.PlansVault!;
            Assert.True(plans.PlansMode);
            Assert.NotNull(plans.InvitesPanel);
            Assert.False(plans.FindControl<Border>("SpotlightCard")!.IsVisible);
            Assert.Empty(plans.FindControl<ItemsControl>("ExclusivesShelf")!.Items);
            Assert.Null(plans.FindControl<ScrollViewer>("ContentScroll")!.Parent);
            Assert.True(plans.FindControl<Border>("InvitesWrap")!.IsVisible);
            Assert.Equal(Loc.Get("plans_header_title"), plans.FindControl<TextBlock>("TxtVaultTitle")!.Text);
            LocalizationManager.Instance.SetLanguage("de"); Settle();
            Assert.Equal(Loc.Get("plans_header_sub"), plans.FindControl<TextBlock>("TxtVaultSub")!.Text);
            Assert.NotNull(((ImageBrush)plans.FindControl<Border>("VaultHeroPlate")!.Background!).Source);
            Assert.IsType<Image>(plans.FindControl<Border>("TierPlate1")!.Child);
            plans.FindControl<Button>("BtnSeePremium")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            Assert.Equal("premium", shell.CurrentTab);
            Assert.False(plans.MotionRunning);
            var page = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            Assert.False(page.FindControl<Border>("InvitesWrap")!.IsVisible);
            Assert.Null(page.InvitesPanel);
            Assert.NotEmpty(page.FindControl<ItemsControl>("ExclusivesShelf")!.Items);
            shell.OpenInvitesCard();
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.True(plans.MotionRunning);
        }
        finally { shell?.Close(); LocalizationManager.Instance.SetLanguage(lang); (s.MotionLevel, s.PerformanceMode) = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CardsLiftBloomBurstAndPanicCancelsTheDelayedDoor() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var s = CoreSettings.Current; var old = (s.MotionLevel, s.PerformanceMode);
        var clock = new Clock(); var oldClock = FxAdorner.Time; FxAdorner.Time = clock;
        MainShellWindow? shell = null;
        try
        {
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
            shell = new MainShellWindow { Width = 1400, Height = 900 }; shell.Show(); shell.ShowTab("premium");
            Settle();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ExCard" && ((ExclusiveCardRow)b.DataContext!).Feature.Key == "haptics");
            view.HoverCard(card, true); clock.Now += TimeSpan.TicksPerSecond; view.FlairFrame();
            var transform = (TransformGroup)card.RenderTransform!;
            Assert.Equal(1.02, transform.Children.OfType<ScaleTransform>().Single().ScaleX, 6);
            Assert.Null(card.Effect);
            Assert.True(card.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "HoverGlow").BoxShadow.Count > 0);
            view.UpdateVaultZones();
            var fx = view.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            Assert.NotEmpty(fx.VaultZones);
            Assert.True(fx.Layers.HasFlag(AmbientFxLayers.VaultMotes));
            view.ClickCard(card, (ExclusiveCardRow)card.DataContext!);
            Assert.Equal(1, view.PendingClickCount);
            Assert.Equal("premium", shell.CurrentTab);
            PanicSurfaces.All.Single(p => p.Id == "vault-card").Stop(shell);
            Assert.Equal(0, view.PendingClickCount);
            Assert.Equal("premium", shell.CurrentTab);
            var sign = view.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Rectangle>().First(c => c.Name == "GroupSignSheen");
            Assert.True(((LinearGradientBrush)sign.Fill!).StartPoint.Point.X > -1.2);
            var owned = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "OwnedRim" && ((ExclusiveCardRow)b.DataContext!).IsMine);
            Assert.InRange(owned.Opacity, 0.32, 0.95);
            s.PerformanceMode = true; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            view.HoverCard(card, true); clock.Now += TimeSpan.TicksPerSecond; view.FlairFrame();
            Assert.Equal(1.02, transform.Children.OfType<ScaleTransform>().Single().ScaleX, 6);
            Assert.Equal(0, card.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "HoverGlow").BoxShadow.Count);
            s.PerformanceMode = false;
            s.MotionLevel = MotionLevel.Reduced; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Equal(AmbientFxLayers.VaultMotes, fx.Layers);
            for (int i = 0; i < 50; i++) fx.StepVault(0.1);
            Assert.InRange(fx.VaultMoteCount, 1, 14);
            view.ClickCard(card, (ExclusiveCardRow)card.DataContext!);
            Assert.Equal("haptics", shell.CurrentTab); // Reduced has no burst delay
        }
        finally { shell?.Close(); FxAdorner.Time = oldClock; (s.MotionLevel, s.PerformanceMode) = old; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task VaultRepaintChecksDailyFreeAndLetsTheServiceThrottle() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var old = AvApp.DailyFree;
        var service = new DailyFreeService(fetchOverride: true);
        var wire = new DailyWire();
        var httpField = typeof(DailyFreeService).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HttpClient)httpField.GetValue(service)!).Dispose();
        using var http = new HttpClient(wire);
        httpField.SetValue(service, http);
        try
        {
            AvApp.DailyFree = service;
            var view = new ExclusivesTabView();
            Assert.Equal(1, wire.Reads);
            view.RefreshVault(); view.RefreshVault();
            Assert.Equal(1, wire.Reads); // WPF's 10-minute failed-attempt gate
        }
        finally
        {
            AvApp.DailyFree = old;
            ((Timer)typeof(DailyFreeService).GetField("_dayRollTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!).Dispose();
        }
        return Task.CompletedTask;
    });
    private sealed class DailyWire : HttpMessageHandler
    {
        internal int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Reads++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
    }

    private sealed class Clock : TimeProvider
    { public long Now; public override long GetTimestamp() => Now; public override long TimestampFrequency => TimeSpan.TicksPerSecond; }
}

// No text is created: this regression can run on a worker without usable system fonts.
public sealed class ExclusivesPalettePublicationTests
{
    [Fact]
    public Task StartModsAndARealModSwitchPublishAllFourFxSlots() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldMods = AvApp.Mods;
        var oldId = CoreSettings.Current.ActiveModId;
        try
        {
            AvApp.StartMods();
            Check();
            AvApp.Mods!.ActivateMod(AvApp.Mods.ActiveModId == BuiltInMods.InfectionControlId ? BuiltInMods.CCPDefaultId : BuiltInMods.InfectionControlId);
            Dispatcher.UIThread.RunJobs(); Check();
            void Check()
            {
                var m = AvApp.Mods!;
                foreach (var (key, rgb) in new[] { ("FxGlow", m.GetGlowColorRgb()), ("FxMist", m.GetMistColorRgb()), ("FxParticle", m.GetParticleColorRgb()), ("FxFlashTint", m.GetFlashTintRgb()) })
                {
                    var expected = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                    Assert.Equal(expected, Application.Current!.Resources[key + "Color"]);
                    Assert.Equal(expected, ((ISolidColorBrush)Application.Current.Resources[key + "Brush"]!).Color);
                }
            }
        }
        finally
        {
            AvApp.Mods?.ActivateMod(oldId);
            typeof(AvApp).GetProperty(nameof(AvApp.Mods), BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, oldMods);
            CoreSettings.Current.ActiveModId = oldId;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    // StartModsTests calls StartMods off the UI thread; a live control then threw in ActualThemeVariant.
    [Fact]
    public Task OffThreadPublishMarshalsToTheUiThread() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var res = Application.Current!.Resources;
        var border = new Border();
        var window = new Window { Content = border };
        try
        {
            window.Show();
            border.Bind(Border.BackgroundProperty, border.GetResourceObservable("FxGlowBrush"));   // what a DynamicResource does
            res["FxGlowColor"] = Colors.Lime;
            Assert.Null(Record.Exception(() => Task.Run(FxTheme.ApplyForActiveMod).GetAwaiter().GetResult()));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(Colors.Lime, res["FxGlowColor"]);
        }
        finally { window.Close(); FxTheme.ApplyForActiveMod(); }
        return Task.CompletedTask;
    });
}
