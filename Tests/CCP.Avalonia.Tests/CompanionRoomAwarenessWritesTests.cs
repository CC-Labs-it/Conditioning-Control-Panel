using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using Intensity = ConditioningControlPanel.Services.Awareness.AwarenessIntensity;

namespace CCP.Avalonia.Tests;

public class CompanionRoomAwarenessWritesTests
{
    private static MainShellWindow Open()
    {
        if (Application.Current is null)
            AvAppBuilder();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        settings.Current.AwarenessModeEnabled = settings.Current.AwarenessConsentGiven = true;
        settings.Current.AwarenessConsentShownV2 = true;
        CoreEntitlement.HasPremiumProvider = () => true;
        var shell = new MainShellWindow();
        shell.Show();
        shell.ShowTab("awareness");
        shell.CompleteTabTransition();
        Dispatcher.UIThread.RunJobs();
        return shell;
    }

    private static void AvAppBuilder() => AppBuilder.Configure<AvApp>()
        .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();

    [Fact]
    public Task DialOffRefreshesHeroAndLegacySlidersWithoutTabSwitch() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var shell = Open();
        try
        {
            CoreSettings.Current.UseAwarenessV2 = false;
            var hero = shell.GetLogicalDescendants().OfType<CompanionHeroCard>().Single().ViewModel!;
            hero.Sync();
            var cell = shell.GetLogicalDescendants().OfType<WorkshopAwarenessCell>().Single();
            cell.SyncIntensity();
            Assert.True(cell.FindControl<Border>("AwarenessSettingsPanel")!.IsVisible);
            var privacy = shell.GetLogicalDescendants().OfType<AwarenessPrivacyView>().Single().ViewModel!;
            privacy.Intensity = ConditioningControlPanel.Avalonia.Views.Controls.Companion.AwarenessIntensity.Off;
            await privacy.Pending;
            Assert.False(CoreSettings.Current.AwarenessModeEnabled);
            Assert.False(hero.IsAwarenessOpen);
            Assert.False(cell.FindControl<Border>("AwarenessSettingsPanel")!.IsVisible);
        }
        finally { AvApp.WindowAwareness.Stop(); shell.Close(); }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LegacySlidersRequireEnabledLegacyPipeline(bool v2, bool enabled) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null) AvAppBuilder();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.UseAwarenessV2 = v2;
        settings.Current.AwarenessModeEnabled = enabled;
        var cell = new WorkshopAwarenessCell();
        Assert.Equal(!v2 && enabled, cell.FindControl<Border>("AwarenessSettingsPanel")!.IsVisible);
    });

    [Fact]
    public Task WorkshopRadioRefreshesOtherSeatedDialsImmediately() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Open();
        var mirror = new WorkshopAwarenessCell();
        foreach (var radio in mirror.GetLogicalDescendants().OfType<RadioButton>()) radio.GroupName = "MirrorIntensity";
        var host = new StackPanel();
        var content = (Control)shell.Content!;
        shell.Content = null;
        host.Children.Add(content);
        host.Children.Add(mirror);
        shell.Content = host;
        try
        {
            CoreSettings.Current.UseAwarenessV2 = true;
            CoreSettings.Current.AwarenessIntensity = Intensity.Chatty;
            shell.SyncAwareness();
            shell.GetLogicalDescendants().OfType<Expander>().Single(e => e.Name == "CompanionTuningFold").IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var cell = shell.GetLogicalDescendants().OfType<WorkshopAwarenessCell>().First(c => c != mirror);
            Assert.Same(shell, TopLevel.GetTopLevel(cell));
            cell.FindControl<RadioButton>("RadioIntensitySubtle")!.IsChecked = true;
            Assert.Equal(Intensity.Subtle, CoreSettings.Current.AwarenessIntensity);
            Assert.True(CoreSettings.Current.AwarenessIntensityMigrated);
            Assert.True(mirror.FindControl<RadioButton>("RadioIntensitySubtle")!.IsChecked);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task UpgraderRestartsOnlyAfterAcceptanceAndAsksOnce(bool accept) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var shell = Open();
        var oldLifecycle = WindowAwarenessService.V2Lifecycle;
        int starts = 0, stops = 0;
        try
        {
            var s = CoreSettings.Current;
            s.UseAwarenessV2 = true;
            s.AwarenessConsentShownV2 = false;
            AvApp.WindowAwareness.Stop();
            WindowAwarenessService.V2Lifecycle = running => { if (running) starts++; else stops++; };
            AvApp.WindowAwareness.Start();
            starts = 0;
            var pending = shell.EnsureAwarenessV2ConsentAsync();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<AwarenessConsentDialog>());
            dialog.FindControl<Button>(accept ? "BtnAccept" : "BtnDecline")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending;
            Assert.Equal(accept ? 1 : 0, starts);
            Assert.Equal(accept ? 1 : 0, stops);
            Assert.Equal(accept, s.AwarenessConsentShownV2);
            await shell.EnsureAwarenessV2ConsentAsync();
            Assert.Empty(shell.OwnedWindows.OfType<AwarenessConsentDialog>());
        }
        finally
        {
            AvApp.WindowAwareness.Stop();
            WindowAwarenessService.V2Lifecycle = oldLifecycle;
            foreach (var dialog in shell.OwnedWindows.ToArray()) dialog.Close();
            shell.Close();
        }
    });
}
