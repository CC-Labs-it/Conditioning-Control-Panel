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
            Assert.Null(plans.FindControl<Image>("SpotArtImage")!.Source);
            plans.RefreshVault();
            Assert.Null(plans.FindControl<Image>("SpotArtImage")!.Source);
            // WPF ExclusivesTabView.xaml:48 and .xaml.cs:133 retain the room in PlansMode.
            var backdrop = plans.FindControl<Image>("VaultBackdrop")!;
            Assert.True(backdrop.IsEffectivelyVisible);
            Assert.NotNull(backdrop.Source);
            Assert.NotNull(plans.InvitesPanel);
            Assert.False(plans.FindControl<Border>("SpotlightCard")!.IsVisible);
            Assert.Empty(plans.FindControl<ItemsControl>("ExclusivesShelf")!.Items);
            Assert.Null(plans.FindControl<ScrollViewer>("ContentScroll")!.Parent);
            Assert.True(plans.FindControl<Border>("InvitesWrap")!.IsVisible);
            Assert.Equal(Loc.Get("plans_header_title"), plans.FindControl<TextBlock>("TxtVaultTitle")!.Text);
            LocalizationManager.Instance.SetLanguage("de"); Settle();
            Assert.Equal(Loc.Get("plans_header_sub"), plans.FindControl<TextBlock>("TxtVaultSub")!.Text);
            Assert.Null(plans.FindControl<Image>("SpotArtImage")!.Source);
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
            Assert.NotNull(page.FindControl<Image>("SpotArtImage")!.Source);
            shell.OpenInvitesCard();
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.True(plans.MotionRunning);
            Assert.Null(plans.FindControl<Image>("SpotArtImage")!.Source);
            shell.OpenAppSettingsSection("general"); Settle();
            Assert.False(plans.MotionRunning);
        }
        finally { shell?.Close(); LocalizationManager.Instance.SetLanguage(lang); (s.MotionLevel, s.PerformanceMode) = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ModSwitchReusesHeaderBrushAndMaskAndKeepsCommerceArt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var oldMods = AvApp.Mods;
        var oldId = CoreSettings.Current.ActiveModId;
        Window? host = null;
        try
        {
            AvApp.StartMods();
            var view = new ExclusivesTabView();
            host = new Window { Content = view }; host.Show(); Settle();
            var plate = view.FindControl<Border>("VaultHeroPlate")!;
            var brush = Assert.IsType<ImageBrush>(plate.Background);
            var mask = plate.OpacityMask;
            var tier = view.FindControl<Border>("TierPlate1")!.Child;
            foreach (var id in new[] { BuiltInMods.BambiSleepId, BuiltInMods.DronificationId })
            {
                AvApp.Mods!.ActivateMod(id); Settle();
                Assert.Same(brush, plate.Background);
                Assert.Same(mask, plate.OpacityMask);
                Assert.Same(ConditioningControlPanel.Avalonia.Helpers.ModArt.FirstOf(
                    ConditioningControlPanel.Services.UI.ModTileArt.Candidates("vault", id), 480), brush.Source);
                Assert.Same(tier, view.FindControl<Border>("TierPlate1")!.Child);
            }
        }
        finally
        {
            host?.Close();
            AvApp.Mods?.ActivateMod(oldId);
            typeof(AvApp).GetProperty(nameof(AvApp.Mods), BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, oldMods);
            CoreSettings.Current.ActiveModId = oldId;
            Settle();
        }
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

}
