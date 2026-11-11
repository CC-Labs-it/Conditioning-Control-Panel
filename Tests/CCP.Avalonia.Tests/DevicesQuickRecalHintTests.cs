using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class DevicesQuickRecalHintTests
{
    [Fact]
    public void TooltipRefreshesOnOpenAndLanguageChange() => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        var oldHint = WebcamQuickRecalWindow.HotkeyHint;
        var section = new DevicesSettingsSection();
        var window = new Window { Content = section };
        var button = section.FindControl<Button>("BtnWebcamDebugQuickRecal")!;
        try
        {
            loc.SetLanguage("en");
            WebcamQuickRecalWindow.HotkeyHint = () => "Ctrl+Alt+G / Ctrl+Alt+C";
            window.Show();
            section.OnSectionShown();
            var english = Loc.Get("tooltip_devices_quick_recal");
            Assert.Equal(english + "\n\nCtrl+Alt+G / Ctrl+Alt+C", ToolTip.GetTip(button));
            section.OnSectionShown();
            Assert.Equal(english + "\n\nCtrl+Alt+G / Ctrl+Alt+C", ToolTip.GetTip(button));
            WebcamQuickRecalWindow.HotkeyHint = () => null;
            section.OnSectionShown();
            Assert.Equal(english, ToolTip.GetTip(button));
            WebcamQuickRecalWindow.HotkeyHint = () => "Ctrl+Alt+G / Ctrl+Alt+C";
            loc.SetLanguage("de");
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(english, Loc.Get("tooltip_devices_quick_recal"));
            Assert.Equal(Loc.Get("tooltip_devices_quick_recal") + "\n\nCtrl+Alt+G / Ctrl+Alt+C", ToolTip.GetTip(button));
        }
        finally
        {
            window.Close();
            WebcamQuickRecalWindow.HotkeyHint = oldHint;
            loc.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
        }
    });
}
