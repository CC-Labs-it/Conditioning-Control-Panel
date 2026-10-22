using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessLedgerPrivacyTests
{
    [Fact]
    public void Picker_candidates_fold_in_the_ledgers_switches_and_known_apps()
    {
        // WPF AwarenessAppCandidates.Gather: the ledger, not just the window in front, fills the picker.
        var dir = Directory.CreateTempSubdirectory("ccp-awareness-candidates-").FullName;
        var now = DateTime.Today.AddHours(12);
        var old = AwarenessLive.Ledger;
        using var ledger = new ActivityLedger(Path.Combine(dir, "awareness_ledger.json"), () => now, () => 30);
        try
        {
            ledger.Start();
            ledger.NoteFocus("first-app", "test", ActivityCategory.Unknown, now.AddMinutes(-5));
            ledger.NoteFocus("second-app", "test", ActivityCategory.Unknown, now.AddMinutes(-3));
            ledger.NoteFocus("listed-app", "test", ActivityCategory.Unknown, now.AddMinutes(-1));
            Assert.Contains(ledger.RecentTransitions, t => t.AppId == "first-app");
            var known = ledger.KnownAppIds.ToList();
            Assert.True(known.IndexOf("second-app") < known.IndexOf("first-app"));   // counters alone would offer second-app first
            AwarenessLive.Ledger = ledger;

            var picked = AwarenessPrivacyViewModel.Candidates(new[] { "LISTED-APP" });

            Assert.Equal(new[] { "first-app", "second-app" }, picked);   // switches in ring order first, de-duplicated, listed skipped
            AwarenessLive.Ledger = null;
            Assert.Empty(AwarenessPrivacyViewModel.Candidates(Array.Empty<string>()));
        }
        finally
        {
            AwarenessLive.Ledger = old;
            ledger.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Retained_apps_are_shown_forgotten_pruned_and_wiped_through_the_card()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-awareness-privacy-").FullName;
        var path = Path.Combine(dir, "awareness_ledger.json");
        var now = DateTime.Today.AddHours(12);
        using var ledger = new ActivityLedger(path, () => now, () => CoreSettings.Current.AwarenessRetentionDays);
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;   // P02: own settings, never the shared default
        var old = (AwarenessLive.Ledger, AwarenessLive.Memory, CoreSettings.Current.AwarenessRetentionDays);
        var oldHook = WindowAwarenessService.V2Lifecycle;
        var oldEntitlement = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        AwarenessLive.Memory = null;
        CoreSettings.Current.AwarenessRetentionDays = 30;
        CoreSettings.Current.AwarenessModeEnabled = true;
        CoreSettings.Current.AwarenessConsentGiven = true;
        try
        {
            // Seed fake app ids, then reopen so the unpersisted session ring is absent on this launch.
            using (var seed = new ActivityLedger(path, () => now, () => 30))
            {
                seed.Start();
                seed.NoteFocus("old-app", "test", ActivityCategory.Unknown, now.AddDays(-20));
                seed.NoteFocusEnd(now.AddDays(-20).AddMinutes(2));
                seed.NoteFocus("recent-app", "test", ActivityCategory.Unknown, now.AddDays(-1));
                seed.NoteFocusEnd(now.AddDays(-1).AddMinutes(2));
                seed.SaveNow();
            }
            // Startup with awareness off: WPF App.xaml.cs:2928 only sweeps the file, never starts the ledger.
            global::ConditioningControlPanel.Avalonia.App.InstallPrivacyLedger(ledger);
            Assert.Same(ledger, AwarenessLive.Ledger);
            Assert.Empty(ledger.KnownAppIds);                 // the sweep loads nothing into memory
            Assert.Contains("recent-app", File.ReadAllText(path));
            using (var awareness = new WindowAwarenessService(() => ""))
            {
                awareness.Start();                            // the awareness switch: the hook starts the ledger
                Assert.Contains("recent-app", ledger.KnownAppIds);
                ledger.NoteFocus("recent-app", "test", ActivityCategory.Unknown, now);
                var beforeStop = File.ReadAllText(path);
                awareness.Stop();                             // switch off: the hook stops (and flushes) it
                Assert.NotEqual(beforeStop, File.ReadAllText(path));
            }
            ledger.Start();                                   // the card below works on a running ledger
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var view = new AwarenessPrivacyView();
                var host = new Window { Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                try
                {
                    var vm = view.ViewModel!;
                    Assert.True(vm.HasKnownApps);
                    Assert.Equal(new[] { "recent-app", "old-app" }, vm.KnownApps.Select(c => c.Label));
                    var forget = view.GetVisualDescendants().OfType<Button>()
                        .Single(b => b.Content is TextBlock { Text: "recent-app" });
                    Assert.True(forget.IsEffectivelyVisible);
                    Assert.True(forget.IsEnabled);
                    Assert.NotNull(forget.Command);
                    forget.Command.Execute(forget.CommandParameter);
                    Assert.DoesNotContain("recent-app", ledger.KnownAppIds);
                    Assert.DoesNotContain("recent-app", File.ReadAllText(path));
                    vm.RetentionDays = 7;
                    Assert.Empty(ledger.KnownAppIds);
                    Assert.False(vm.HasKnownApps);
                    ledger.Start();
                    ledger.NoteFocus("wipe-app", "test", ActivityCategory.Unknown, now);
                    vm.Sync();
                    Assert.True(vm.HasKnownApps);
                    Assert.True(view.WipeConfirm.CanArm);
                    view.WipeConfirm.ArmCommand.Execute(null);
                    Assert.True(view.WipeConfirm.IsArmed);
                    view.WipeConfirm.ConfirmCommand.Execute(null);
                    Assert.Empty(ledger.KnownAppIds);
                    Assert.False(vm.HasKnownApps);
                    Assert.False(File.Exists(path));
                    Assert.False(File.Exists(path + ".tmp"));
                }
                finally { host.Close(); }
                return Task.CompletedTask;
            });
            global::ConditioningControlPanel.Avalonia.App.ReleasePrivacyLedger();   // exit: WPF App.OnExit:6285
            Assert.Null(AwarenessLive.Ledger);
        }
        finally
        {
            (AwarenessLive.Ledger, AwarenessLive.Memory, CoreSettings.Current.AwarenessRetentionDays) = old;
            WindowAwarenessService.V2Lifecycle = oldHook;
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = oldEntitlement;
            CoreSettings.Save();
            CoreSettings.ServiceProvider = oldProvider;
            ledger.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }
}
