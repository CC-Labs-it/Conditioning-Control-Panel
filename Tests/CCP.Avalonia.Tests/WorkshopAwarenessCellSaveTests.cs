using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Nov 4 review of #3334: the Workshop awareness cell's intensity stops saved only when the cell sat in
/// MainShellWindow. Hosted anywhere else a stop showed as checked and nothing was written.</summary>
public sealed class WorkshopAwarenessCellSaveTests
{
    [Fact]
    public Task IntensityStopSavesWhenTheCellIsHostedOutsideTheShell() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var cell = new WorkshopAwarenessCell();
        var host = new Window { Content = cell, Width = 600, Height = 800 };
        try
        {
            var s = CoreSettings.Current;
            s.AwarenessIntensity = AwarenessIntensity.Off;
            s.AwarenessIntensityMigrated = false;
            host.Show();
            cell.SyncIntensity();
            Dispatcher.UIThread.RunJobs();

            cell.FindControl<RadioButton>("RadioIntensityChatty")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(AwarenessIntensity.Chatty, s.AwarenessIntensity);
            Assert.True(s.AwarenessIntensityMigrated);   // a later start-up must not overwrite the choice
            Assert.True(cell.FindControl<RadioButton>("RadioIntensityChatty")!.IsChecked);
        }
        finally
        {
            host.Close();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
