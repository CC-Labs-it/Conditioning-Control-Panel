using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StudioRackRebindTests
{
    [Theory]
    [InlineData("PanelBubbleCount", "BubbleCountEnabled", "ChkEnable")]
    [InlineData("PanelLockCard", "LockCardEnabled", "ChkEnable")]
    [InlineData("PanelMindWipe", "MindWipeEnabled", "ChkEnable")]
    [InlineData("PanelSubliminal", "SubliminalEnabled", "ChkEnable")]
    [InlineData("PanelRamp", "IntensityRampEnabled", "ChkEnabled")]
    public Task RestoreRebindsHostedPanel(string panelName, string propertyName, string boxName) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var property = typeof(AppSettings).GetProperty(propertyName)!;
            property.SetValue(service.Current, false);
            var rack = new StudioTabView();
            var host = new Window { Content = rack };
            try
            {
                host.Show();
                rack.PreselectRackEntry(panelName switch
                {
                    "PanelBubbleCount" => "bubblecount",
                    "PanelLockCard" => "lockcard",
                    "PanelMindWipe" => "mindwipe",
                    "PanelSubliminal" => "subliminal",
                    _ => "ramp"
                });
                HeadlessFrame.Publish(host);
                Dispatcher.UIThread.RunJobs();
                var panel = rack.FindControl<Control>(panelName)!;
                if (panel is RampRackPanel ramp) panel = ramp.Inner;
                var box = panel.FindControl<CheckBox>(boxName)!;
                Assert.False(box.IsChecked);
                var old = service.Current;
                var replacement = new AppSettings();
                property.SetValue(replacement, true);
                service.RestoreFrom(replacement);
                Dispatcher.UIThread.RunJobs();
                Assert.True(box.IsChecked);
                property.SetValue(service.Current, false);
                Dispatcher.UIThread.RunJobs();
                Assert.False(box.IsChecked);
                property.SetValue(old, true);
                Dispatcher.UIThread.RunJobs();
                Assert.False(box.IsChecked);
            }
            finally { host.Close(); service.SealForReset(); }
            return Task.CompletedTask;
        });
}
