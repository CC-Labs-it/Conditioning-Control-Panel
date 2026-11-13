using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using AwarenessIntensity = ConditioningControlPanel.Avalonia.Views.Controls.Companion.AwarenessIntensity;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Z5 "What she can see" over real settings (WPF AwarenessPrivacyRuntimeVm), driven from a
/// shown shell: the dial's ON edge goes entitlement -> consent dialog, decline leaves her eyes shut,
/// accept turns awareness on and raises the older-pipeline band; chips, pause and wipe write for real.
/// The desktop is never read: X11ActiveWindow is disabled for the whole test assembly.</summary>
public sealed class AwarenessPrivacyCardTests
{
    /// <summary>Only fail-capable where DISPLAY is set (local runs): without an X display ReadTitle
    /// returns "" anyway, so on headless CI this passes whether or not the guard is in place.</summary>
    [Fact]
    public void TestsNeverReadTheRealDesktop() => Assert.Equal("", X11ActiveWindow.ReadTitle());

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 400 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
    }

    private static async Task<bool> PressDial(MainShellWindow shell, AwarenessPrivacyViewModel vm,
        AwarenessIntensity stop, string? answer)
    {
        vm.Intensity = stop;
        if (answer != null)
        {
            await Until(() => shell.OwnedWindows.OfType<AwarenessConsentDialog>().Any());
            shell.OwnedWindows.OfType<AwarenessConsentDialog>().Single().FindControl<Button>(answer)!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        await vm.Pending;
        Dispatcher.UIThread.RunJobs();
        return CoreSettings.Current.AwarenessModeEnabled;
    }

    [Fact]
    public Task PublishedAdultFrameUsesProjection() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previousProvider = CoreSettings.ServiceProvider;
        var previousObserver = AwarenessLive.ObserverEnabled;
        var previousPause = AwarenessPause.PausedUntil;
        // Publish assigns a new timestamp when CutAt is default, so preserve both fields exactly.
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var liveLock = typeof(AwarenessLive).GetField("Lock", flags)!.GetValue(null)!;
        var frameField = typeof(AwarenessLive).GetField("_lastFrame", flags)!;
        var timeField = typeof(AwarenessLive).GetField("_lastFrameAt", flags)!;
        object? previousFrame, previousTime;
        lock (liveLock) { previousFrame = frameField.GetValue(null); previousTime = timeField.GetValue(null); }
        try
        {
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = CoreSettings.Current;
            s.AwarenessModeEnabled = s.AwarenessConsentGiven = true;
            AwarenessPause.Resume();
            AwarenessLive.Clear();
            AwarenessLive.ObserverEnabled = false;
            var vm = new AwarenessPrivacyViewModel(() => null);
            Assert.True(vm.IsLegacyPipeline);
            Assert.False(vm.IsWireLive);
            AwarenessLive.Publish(new ContextFrame { AppCluster = AwarenessClusters.Adult, AppId = "private-app", ServiceName = "private-name", PageTitleSanitized = "private-title" });
            AwarenessLive.ObserverEnabled = true;
            vm.Sync();
            Assert.False(vm.IsLegacyPipeline);
            Assert.True(vm.IsWireLive);
            Assert.Contains(AwarenessClusters.Adult, vm.WireLine);
            Assert.Contains(AwarenessClusters.Adult, vm.WireJson);
            Assert.DoesNotContain("private", vm.WireLine);
            Assert.DoesNotContain("private", vm.WireJson);
            AwarenessLive.Publish(new ContextFrame { AppId = "editor", ServiceName = "Editor", PageTitleSanitized = "Contact user@example.com" });
            vm.Sync();
            Assert.DoesNotContain("user@example.com", vm.WireLine);
            Assert.DoesNotContain("user@example.com", vm.WireJson);
            Assert.Contains("Contact", vm.WireLine);
            Assert.Contains("Contact", vm.WireJson);
        }
        finally
        {
            lock (liveLock) { frameField.SetValue(null, previousFrame); timeField.SetValue(null, previousTime); }
            AwarenessLive.ObserverEnabled = previousObserver;
            AwarenessPause.Resume();
            if (previousPause is { } until) AwarenessPause.Pause(TimeSpan.FromTicks(1), until.AddTicks(-1));
            CoreSettings.ServiceProvider = previousProvider;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DialConsentBandChipsPauseAndWipeAreReal() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);   // no intro card over the shell (P52)
        s.AwarenessModeEnabled = false;
        s.AwarenessConsentGiven = false;
        s.AwarenessConsentShownV2 = false;
        s.UseAwarenessV2 = true;
        s.AwarenessTitleAllowList = new();
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var ledger = Path.Combine(CorePaths.UserData, "awareness_ledger.json");
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("awareness");   // v2: the Awareness tab hosts the room's privacy card
            Dispatcher.UIThread.RunJobs();
            var card = shell.GetLogicalDescendants().OfType<AwarenessPrivacyView>().Single();
            var vm = card.ViewModel!;
            Assert.Same(shell, TopLevel.GetTopLevel(card));
            Assert.Equal(AwarenessIntensity.Off, vm.Intensity);
            Assert.False(vm.IsLegacyPipeline);

            // No entitlement: the door is refused before any privacy explanation.
            CoreEntitlement.HasPremiumProvider = () => false;
            Assert.False(await PressDial(shell, vm, AwarenessIntensity.BroadStrokes, null));
            Assert.Empty(shell.OwnedWindows.OfType<AwarenessConsentDialog>());

            // Entitled: the consent dialog asks; declining writes nothing.
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.False(await PressDial(shell, vm, AwarenessIntensity.BroadStrokes, "BtnDecline"));
            Assert.False(s.AwarenessConsentGiven);
            Assert.Equal(AwarenessIntensity.Off, vm.Intensity);

            // Accepting opens her eyes, and the card says straight that the v2 protections are not here.
            Assert.True(await PressDial(shell, vm, AwarenessIntensity.BroadStrokes, "BtnAccept"));
            AvApp.WindowAwareness.Stop();
            Assert.Equal(AwarenessIntensity.BroadStrokes, vm.Intensity);
            Assert.True(vm.IsLegacyPipeline);
            Assert.False(vm.IsWireLive);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("companion_awareness_wire_legacy"), vm.WireLine);

            // Until the observer exists, the review action cannot switch on v2 protections.
            var reviewButton = Assert.Single(card.GetLogicalDescendants().OfType<Button>(),
                button => ReferenceEquals(button.Command, vm.ReviewConsentCommand));
            Assert.False(vm.CanReviewConsent);
            Assert.False(reviewButton.IsVisible);
            s.UseAwarenessV2 = false;
            vm.Sync();
            Assert.False(vm.CanReviewConsent);
            Assert.False(reviewButton.IsVisible);
            s.UseAwarenessV2 = true;
            vm.Sync();

            // The retained command still asks again. Decline changes nothing; accept bounces the poll.
            var calls = new System.Collections.Generic.List<bool>();
            var lifecycle = ConditioningControlPanel.Services.WindowAwarenessService.V2Lifecycle;
            try
            {
                ConditioningControlPanel.Services.WindowAwarenessService.V2Lifecycle = calls.Add;
                AvApp.WindowAwareness.Start();
                calls.Clear();
                vm.ReviewConsentCommand.Execute(null);
                await Until(() => shell.OwnedWindows.OfType<AwarenessConsentDialog>().Any());
                shell.OwnedWindows.OfType<AwarenessConsentDialog>().Single().FindControl<Button>("BtnDecline")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await vm.Pending;
                Assert.True(s.AwarenessConsentShownV2);
                Assert.Empty(calls);
                vm.ReviewConsentCommand.Execute(null);
                await Until(() => shell.OwnedWindows.OfType<AwarenessConsentDialog>().Any());
                shell.OwnedWindows.OfType<AwarenessConsentDialog>().Single().FindControl<Button>("BtnAccept")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await vm.Pending;
                Assert.Equal(new[] { false, true }, calls);
            }
            finally { ConditioningControlPanel.Services.WindowAwarenessService.V2Lifecycle = lifecycle; AvApp.WindowAwareness.Stop(); }

            // Deny chips are the effective (seeded) list; removing one writes it.
            var first = vm.DenyList.First();
            int before = AwarenessPrivacyRules.EffectiveDenyList(s).Count;
            first.RemoveCommand.Execute(null);
            Assert.Equal(before - 1, AwarenessPrivacyRules.EffectiveDenyList(s).Count);
            Assert.Equal(before - 1, vm.DenyList.Count);

            // Pause is a check that lifts again.
            vm.PauseCommand.Execute(null);
            Assert.True(AwarenessPause.IsPaused());
            Assert.True(vm.IsPaused);
            vm.PauseCommand.Execute(null);
            Assert.False(AwarenessPause.IsPaused());

            // Wipe only through the two-step confirm.
            File.WriteAllText(ledger, "{}");
            card.WipeConfirm.ConfirmCommand.Execute(null);
            Assert.True(File.Exists(ledger));
            card.WipeConfirm.ArmCommand.Execute(null);
            card.WipeConfirm.ConfirmCommand.Execute(null);
            Assert.False(File.Exists(ledger));

            // Off is never gated.
            CoreEntitlement.HasPremiumProvider = () => false;
            Assert.False(await PressDial(shell, vm, AwarenessIntensity.Off, null));
            Assert.False(vm.IsLegacyPipeline);
        }
        finally
        {
            CoreEntitlement.HasPremiumProvider = oldPremium;
            AwarenessPause.Resume();
            AvApp.WindowAwareness.Stop();
            s.AwarenessModeEnabled = false;
            s.AwarenessConsentGiven = false;
            File.Delete(ledger);
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
    });

    /// <summary>Live S23: a declined consent must snap the RENDERED dial back to Off, and a second real
    /// click must ask again (WPF AwarenessPrivacyRuntimeVm.cs:84-119 re-syncs and raises Intensity).
    /// Drives the RadioButtons with the mouse, because the bug lived in the TwoWay binding, not the VM.</summary>
    [Fact]
    public Task DeclinedConsentSnapsTheClickedDialBackToOff() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.AwarenessModeEnabled = false;
        s.AwarenessConsentGiven = false;
        s.AwarenessConsentShownV2 = false;
        s.AwarenessTitleAllowList = new();
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        CoreEntitlement.HasPremiumProvider = () => true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("awareness");
            Dispatcher.UIThread.RunJobs();
            var card = shell.GetLogicalDescendants().OfType<AwarenessPrivacyView>().Single();
            var vm = card.ViewModel!;
            var dial = card.GetLogicalDescendants().OfType<RadioButton>()
                .Where(r => r.GroupName == "CmpAwarenessDial").ToList();
            Assert.Equal(3, dial.Count);
            RadioButton off = dial[0], broad = dial[1];

            async Task ClickAndDecline(RadioButton segment)
            {
                segment.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                var at = segment.TranslatePoint(new Point(segment.Bounds.Width / 2, segment.Bounds.Height / 2), shell)!.Value;
                shell.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
                shell.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
                await Until(() => shell.OwnedWindows.OfType<AwarenessConsentDialog>().Any());
                var dialog = Assert.Single(shell.OwnedWindows.OfType<AwarenessConsentDialog>());
                dialog.FindControl<Button>("BtnDecline")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await vm.Pending;
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(off.IsChecked);
            await ClickAndDecline(broad);
            Assert.False(s.AwarenessModeEnabled);
            Assert.Equal(AwarenessIntensity.Off, vm.Intensity);
            Assert.True(off.IsChecked, "declined consent must snap the dial back to Off");
            Assert.False(broad.IsChecked, "the clicked segment must not stay selected after a decline");

            // A second click on the same segment asks again rather than doing nothing.
            await ClickAndDecline(broad);
            Assert.False(s.AwarenessModeEnabled);
            Assert.True(off.IsChecked);
            Assert.False(broad.IsChecked);
        }
        finally
        {
            CoreEntitlement.HasPremiumProvider = oldPremium;
            s.AwarenessModeEnabled = false;
            s.AwarenessConsentGiven = false;
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public Task PanicKeepsHostedReadoutRefreshingAndDisarmsWipe() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("awareness");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.AwarenessTabView>("AwarenessTab")!;
            var card = Assert.IsType<AwarenessPrivacyView>(tab.FindControl<ContentControl>("CompanionPrivacyHost")!.Content);
            AwarenessPause.Resume();
            AwarenessPause.Pause(AwarenessPause.DefaultDuration);
            var vm = card.ViewModel!;
            vm.Sync();
            Assert.True(vm.IsPaused);
            var pausedLabel = vm.PauseLabel;
            var blink = typeof(AwarenessPrivacyView).GetField("_blink",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            // No invented observer frames: explicitly exercise the public clock start seam.
            card.StartCursorBlink();
            card.WipeConfirm.ArmCommand.Execute(null);
            Assert.True(card.IsRefreshing);
            Assert.NotNull(blink.GetValue(card));
            Assert.True(card.WipeConfirm.IsArmed);

            var stop = PanicSurfaces.All.Single(s => s.Id == "awareness-companion").Stop;
            stop(shell);
            Assert.Null(blink.GetValue(card));
            Assert.False(card.WipeConfirm.IsArmed);
            stop(shell); // safe when already stopped
            stop(null); // safe off the desktop path

            // Expire through the shared clock seam. Only the card's real refresh timer may
            // update the VM afterward: no Sync, navigation, or visibility edge here.
            Assert.False(AwarenessPause.IsPaused(AwarenessPause.PausedUntil!.Value.AddSeconds(1)));
            Assert.True(vm.IsPaused);
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (vm.IsPaused && deadline.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(25);
            Assert.False(vm.IsPaused, "Pause expiry must update the visible card after panic without changing tabs.");
            Assert.NotEqual(pausedLabel, vm.PauseLabel);
            Assert.True(card.IsEffectivelyVisible);
            Assert.True(card.IsRefreshing);
        }
        finally
        {
            AwarenessPause.Resume();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task RefreshTicksOnlyWhileTheCardIsShown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var card = shell.GetLogicalDescendants().OfType<AwarenessPrivacyView>().Single();
            // v2: the room is collapsed and the Awareness tab hosts the card (WPF HostCompanionPrivacy).
            shell.ShowTab("awareness");
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.AwarenessTabView>("AwarenessTab")!.IsVisualAncestorOf(card));
            Assert.True(card.IsRefreshing);
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.IsRefreshing);
            // Companion -> Awareness: the collapsed room's edges must not park the adopted card.
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.IsRefreshing);
            shell.ShowTab("awareness");
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsRefreshing);
            // Not just an accident of the tab table's order: the Companion tab hiding AFTER the
            // Awareness tab showed still leaves the card running.
            var companion = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.CompanionTabView>("CompanionTab")!;
            companion.IsVisible = true;
            companion.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsRefreshing);
        }
        finally
        {
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });
}
