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
    public async Task Retained_apps_are_shown_forgotten_pruned_and_wiped_through_the_card()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-awareness-privacy-").FullName;
        var path = Path.Combine(dir, "awareness_ledger.json");
        var now = DateTime.Today.AddHours(12);
        using var ledger = new ActivityLedger(path, () => now, () => CoreSettings.Current.AwarenessRetentionDays);
        var old = (AwarenessLive.Ledger, AwarenessLive.Memory, CoreSettings.Current.AwarenessRetentionDays);
        AwarenessLive.Ledger = ledger;
        AwarenessLive.Memory = null;
        CoreSettings.Current.AwarenessRetentionDays = 30;
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
            ledger.Start();
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
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
                    ledger.NoteFocus("panic-app", "test", ActivityCategory.Unknown, now);
                    now = now.AddMinutes(2);
                    Assert.Equal(2, ledger.Snapshot("panic-app", now).MinutesToday);
                    PanicSurfaces.All.Single(s => s.Id == "awareness-ledger").Stop(null);
                    now = now.AddMinutes(5);
                    ledger.Heartbeat(now);
                    Assert.Equal(2, ledger.Snapshot("panic-app", now).MinutesToday);
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
        }
        finally
        {
            (AwarenessLive.Ledger, AwarenessLive.Memory, CoreSettings.Current.AwarenessRetentionDays) = old;
            ledger.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }
}
