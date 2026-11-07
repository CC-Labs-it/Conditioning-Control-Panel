using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Avalonia.Tests;

public class AwarenessPrivacyCandidatesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RetainedAppsCanBePickedWithoutKeywordTriggers(bool allow) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-candidates-").FullName;
        var path = Path.Combine(dir, "ledger.json");
        var now = DateTime.Now;
        var previous = AwarenessLive.Ledger;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.KeywordTriggersEnabled = false;
        CoreSettings.Current.AwarenessTitleAllowList = new();
        CoreSettings.Current.AwarenessDenyList = new();
        CoreSettings.Current.AwarenessDenySeeded = true;
        MainShellWindow? shell = null;
        try
        {
            using (var seed = new ActivityLedger(path, () => now, () => 30))
            {
                seed.Start();
                seed.NoteFocus("retained-app", "test", ActivityCategory.Unknown, now.AddMinutes(-5));
                seed.NoteFocusEnd(now.AddMinutes(-4));
                seed.SaveNow();
            }
            using var ledger = new ActivityLedger(path, () => now, () => 30);
            ledger.Start();
            AwarenessLive.Ledger = ledger;
            Assert.Empty(ledger.RecentTransitions); // cold launch, persisted source only
            shell = new MainShellWindow();
            shell.Show();
            var vm = new AwarenessPrivacyViewModel(() => shell);
            if (allow) vm.AllowPageTitles = true;
            else vm.AddDenyCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var picker = Assert.Single(shell.OwnedWindows.OfType<AwarenessAppPickerDialog>());
            var rows = picker.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<AwarenessPickRow>();
            var row = Assert.Single(rows, r => r.Raw == "retained-app");
            Assert.False(row.IsListed); // offering a candidate never widens either list
            row.IsListed = true;
            picker.FindControl<Button>("BtnSave")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await vm.Pending;
            for (var i = 0; i < 100 && !(allow ? CoreSettings.Current.AwarenessTitleAllowList : CoreSettings.Current.AwarenessDenyList).Contains("retained-app"); i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Assert.Contains("retained-app", allow ? CoreSettings.Current.AwarenessTitleAllowList : CoreSettings.Current.AwarenessDenyList);
        }
        finally
        {
            shell?.Close();
            AwarenessLive.Ledger = previous;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void TransitionCandidatesAreSanitizedExcludedDeduplicatedAndCapped()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-candidates-").FullName;
        var previous = AwarenessLive.Ledger;
        var now = DateTime.Now;
        using var ledger = new ActivityLedger(Path.Combine(dir, "ledger.json"), () => now, () => 30);
        try
        {
            ledger.Start();
            AwarenessLive.Ledger = ledger;
            for (var i = 0; i < 50; i++)
                ledger.NoteFocus($"app{i}.exe", "test", ActivityCategory.Unknown, now.AddSeconds(i));
            var result = AwarenessPrivacyViewModel.Candidates(new[] { "  APP0.EXE  " });
            Assert.Equal(Enumerable.Range(1, 40).Select(i => $"app{i}.exe"), result);
            var tail = AwarenessPrivacyViewModel.Candidates(Enumerable.Range(0, 45).Select(i => $"APP{i}.EXE"));
            Assert.Equal(Enumerable.Range(45, 5).Select(i => $"app{i}.exe"), tail);

        }
        finally { AwarenessLive.Ledger = previous; ledger.Dispose(); Directory.Delete(dir, true); }
    }
}
