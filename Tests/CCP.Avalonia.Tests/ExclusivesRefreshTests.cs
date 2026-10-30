using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesRefreshTests(ITestOutputHelper output)
{
    [Fact]
    public Task UnchangedVisitRetainsDecodedArtAndRealizedShelf() => WithVault((view, shelf, _) =>
    {
        var host = (Window)view.Parent!;
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
    });

    [Fact]
    public Task GateChangesRetainArtAndRealizedCards() => WithVault((view, shelf, _) =>
    {
        var row = Fyp(shelf);
        var banner = view.FindControl<Image>("SpotArtImage")!.Source;
        var containers = ((WrapPanel)shelf.ItemsPanelRoot!).Children.ToArray();
        var group = shelf.Items.OfType<ExclusiveGroupRow>().First();
        var count = group.Count;
        var samples = new double[20];
        for (int i = 0; i < samples.Length; i++)
        {
            view.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            bool gifted = i % 2 == 0;
            CoreEntitlement.IsFreeTodayProvider = key => gifted && key == "fyp";
            var clock = Stopwatch.StartNew();
            view.IsVisible = true;
            view.RefreshVault();
            ((Window)view.Parent!).UpdateLayout();
            samples[i] = clock.Elapsed.TotalMilliseconds;
            Dispatcher.UIThread.RunJobs();
        }
        Array.Sort(samples);
        output.WriteLine($"Headless changed-gate refresh + layout (20 visits): median={(samples[9] + samples[10]) / 2:F3} ms; p95={samples[18]:F3} ms");
        Assert.Same(row, Fyp(shelf));
        Assert.Same(banner, view.FindControl<Image>("SpotArtImage")!.Source);
        Assert.Equal(containers, ((WrapPanel)shelf.ItemsPanelRoot!).Children.ToArray());
        CoreEntitlement.HasPremiumProvider = () => true;
        view.RefreshVault();
        Assert.False(row.IsLocked);
        Assert.False(row.FreeToday);
        Assert.NotEqual(count, group.Count);
        var padlock = view.GetVisualDescendants().OfType<Control>()
            .Single(c => c.Name == "VeilLock" && ReferenceEquals(c.DataContext, row));
        Assert.False(padlock.IsEffectivelyVisible);
        CoreEntitlement.HasPremiumProvider = () => false;
        CoreEntitlement.IsFreeTodayProvider = key => key == "remote";
        view.RefreshVault();
        Assert.True(padlock.IsEffectivelyVisible);
        var order = shelf.Items.OfType<ExclusiveCardRow>().Select(r => r.Feature.Key).ToList();
        Assert.True(order.IndexOf("remotecontrol") < order.IndexOf("fyp"));
        ExclusiveFeature.ArcademyDoorProvider = () => true;
        view.RefreshVault();
        var arcademy = shelf.Items.OfType<ExclusiveCardRow>().Single(r => r.Feature.Key == "arcademy");
        ExclusiveFeature.ArcademyDoorProvider = () => false;
        view.RefreshVault();
        Assert.DoesNotContain(arcademy, shelf.Items.OfType<ExclusiveCardRow>());
        ExclusiveFeature.ArcademyDoorProvider = () => true;
        view.RefreshVault();
        Assert.Same(arcademy, shelf.Items.OfType<ExclusiveCardRow>().Single(r => r.Feature.Key == "arcademy"));
    });

    private static ExclusiveCardRow Fyp(ItemsControl shelf) => shelf.Items.OfType<ExclusiveCardRow>().Single(r => r.Feature.Key == "fyp");

    [Fact]
    public Task RefreshStillObservesGatesAndRoster() => WithVault((view, shelf, _) =>
    {
        Assert.True(Fyp(shelf).IsLocked);
        CoreEntitlement.IsFreeTodayProvider = key => key == "fyp";
        view.RefreshVault();
        Assert.True(Fyp(shelf).FreeToday);
        Assert.False(view.FindControl<Border>("SpotVeil")!.IsVisible);
        CoreEntitlement.HasPremiumProvider = () => true;
        view.RefreshVault();
        Assert.False(Fyp(shelf).FreeToday);
        Assert.False(Fyp(shelf).IsLocked);
        ExclusiveFeature.ArcademyDoorProvider = () => true;
        view.RefreshVault();
        Assert.Contains(shelf.Items.OfType<ExclusiveCardRow>(), r => r.Feature.Key == "arcademy");
    });

    [Fact]
    public Task RefreshStillObservesLanguage() => WithVault((view, shelf, language) =>
    {
        var before = shelf.ItemsSource;
        LocalizationManager.Instance.SetLanguage(language == "de" ? "en" : "de");
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(before, shelf.ItemsSource);
    });

    [Fact]
    public Task SameIdModReloadRepaints() => WithVault((view, shelf, _) =>
    {
        var before = shelf.ItemsSource;
        var art = Fyp(shelf).Art;
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest { Id = CoreMods.ActiveModId }, null, true));
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(before, shelf.ItemsSource);
        Assert.NotNull(art);
        Assert.NotSame(art, Fyp(shelf).Art);
        before = shelf.ItemsSource;
        view.RefreshVault();
        Assert.Same(before, shelf.ItemsSource);
    });

    [Fact]
    public Task SameIdModReloadWhileDetachedRepaintsOnNextShow() => WithVault((view, shelf, _) =>
    {
        var host = (Window)view.Parent!;
        var before = shelf.ItemsSource;
        host.Content = null;
        Dispatcher.UIThread.RunJobs();
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest { Id = CoreMods.ActiveModId }, null, true));
        Dispatcher.UIThread.RunJobs();
        host.Content = view;
        Dispatcher.UIThread.RunJobs();
        view.RefreshVault();
        Assert.NotSame(before, shelf.ItemsSource);
    });

    /// <summary>A shown vault with every gate shut; restores the providers and language afterwards.</summary>
    private static Task WithVault(Action<ExclusivesTabView, ItemsControl, string> body) => AvaloniaTestDispatcher.RunAsync(() =>
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
            body(view, view.FindControl<ItemsControl>("ExclusivesShelf")!, language);
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
        global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
