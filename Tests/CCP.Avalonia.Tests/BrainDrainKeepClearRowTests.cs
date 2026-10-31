using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BrainDrainFeatureControl.xaml:220-230 + .xaml.cs:105,326-335: the "Keep pictures clear"
/// row shows BrainDrainKeepPicturesClear on load and writes it back when toggled.</summary>
public sealed class BrainDrainKeepClearRowTests
{
    [Fact]
    public Task RowLoadsAndWritesTheSetting() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var s = CoreSettings.Current;
        var was = s.BrainDrainKeepPicturesClear;
        Window? host = null;
        try
        {
            s.BrainDrainKeepPicturesClear = true;
            var card = new BrainDrainFeatureControl();
            host = new Window { Content = card };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var chk = card.FindControl<CheckBox>("ChkKeepClear")!;

            Assert.True(chk.IsChecked);
            chk.IsChecked = false;
            Assert.False(s.BrainDrainKeepPicturesClear);
            chk.IsChecked = true;
            Assert.True(s.BrainDrainKeepPicturesClear);
        }
        finally
        {
            host?.Close();
            s.BrainDrainKeepPicturesClear = was;
        }
        return Task.CompletedTask;
    });

    /// <summary>The Studio tab keeps the card alive, so a Reset / cloud restore (CurrentReplaced)
    /// must repaint the toggle, as WPF's settings rebind hook does (.xaml.cs:47,69-73).</summary>
    [Fact]
    public Task RowRepaintsWhenSettingsAreReplaced() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        var saved = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        Window? host = null;
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.BrainDrainKeepPicturesClear = false;
            var card = new BrainDrainFeatureControl();
            host = new Window { Content = card };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var chk = card.FindControl<CheckBox>("ChkKeepClear")!;
            Assert.False(chk.IsChecked);

            service.RestoreFrom(new AppSettings { BrainDrainKeepPicturesClear = true });
            Dispatcher.UIThread.RunJobs();
            Assert.True(chk.IsChecked);
        }
        finally
        {
            host?.Close();
            CoreSettings.ServiceProvider = null;
            if (saved is null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, saved);
        }
        return Task.CompletedTask;
    });
}
