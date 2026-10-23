using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesRefreshTests(ITestOutputHelper output)
{
    [Fact]
    public Task UnchangedVisitRetainsDecodedArtAndRealizedShelf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var view = new ExclusivesTabView();
        var host = new Window { Width = 1400, Height = 900, Content = view };
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var shelf = view.FindControl<ItemsControl>("ExclusivesShelf")!;
            var source = shelf.ItemsSource;
            var containers = ((WrapPanel)shelf.ItemsPanelRoot!).Children.ToArray();
            var banner = view.FindControl<Image>("SpotArtImage")!.Source;
            var samples = new double[20];
            for (int i = 0; i < samples.Length; i++)
            {
                view.IsVisible = false;
                Dispatcher.UIThread.RunJobs();
                var clock = Stopwatch.StartNew();
                view.IsVisible = true;
                view.RefreshVault();
                host.UpdateLayout();
                samples[i] = clock.Elapsed.TotalMilliseconds;
                Dispatcher.UIThread.RunJobs();
            }
            Array.Sort(samples);
            output.WriteLine($"Headless refresh + layout (20 visits): median={(samples[9] + samples[10]) / 2:F3} ms; p95={samples[18]:F3} ms");
            Assert.Same(source, shelf.ItemsSource);
            Assert.Equal(containers, ((WrapPanel)shelf.ItemsPanelRoot!).Children.ToArray());
            Assert.Same(banner, view.FindControl<Image>("SpotArtImage")!.Source);
        }
        finally { host.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RefreshStillObservesGatesRosterLanguageAndSameIdModReload() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var premium = CoreEntitlement.HasPremiumProvider;
        var free = CoreEntitlement.IsFreeTodayProvider;
        var door = ExclusiveFeature.ArcademyDoorProvider;
        var language = LocalizationManager.Instance.CurrentLanguage;
        CoreEntitlement.HasPremiumProvider = () => false;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        ExclusiveFeature.ArcademyDoorProvider = () => false;
        var view = new ExclusivesTabView();
        var host = new Window { Width = 1400, Height = 900, Content = view };
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var shelf = view.FindControl<ItemsControl>("ExclusivesShelf")!;
            ExclusiveCardRow Fyp() => shelf.Items.OfType<ExclusiveCardRow>().Single(r => r.Feature.Key == "fyp");
            Assert.True(Fyp().IsLocked);
            CoreEntitlement.IsFreeTodayProvider = key => key == "fyp";
            view.RefreshVault();
            Assert.True(Fyp().FreeToday);
            Assert.False(view.FindControl<Border>("SpotVeil")!.IsVisible);
            CoreEntitlement.HasPremiumProvider = () => true;
            view.RefreshVault();
            Assert.False(Fyp().FreeToday);
            Assert.False(Fyp().IsLocked);
            ExclusiveFeature.ArcademyDoorProvider = () => true;
            view.RefreshVault();
            Assert.Contains(shelf.Items.OfType<ExclusiveCardRow>(), r => r.Feature.Key == "arcademy");
            var before = shelf.ItemsSource;
            LocalizationManager.Instance.SetLanguage(language == "de" ? "en" : "de");
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(before, shelf.ItemsSource);
            before = shelf.ItemsSource;
            var art = Fyp().Art;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest { Id = CoreMods.ActiveModId }, null, true));
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(before, shelf.ItemsSource);
            Assert.NotNull(art);
            Assert.NotSame(art, Fyp().Art);
            before = shelf.ItemsSource;
            view.RefreshVault();
            Assert.Same(before, shelf.ItemsSource);
        }
        finally
        {
            host.Close();
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.IsFreeTodayProvider = free;
            ExclusiveFeature.ArcademyDoorProvider = door;
            LocalizationManager.Instance.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
