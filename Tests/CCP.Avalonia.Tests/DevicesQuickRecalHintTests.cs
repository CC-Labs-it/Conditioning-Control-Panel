using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel;
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
        var settings = CoreSettings.Current;
        var camera = settings.CompanionPrompt;
        var old = (settings.WebcamQuickRecalHotkeyEnabled, camera.CameraShortcutGlobal, camera.CameraShortcutKey, camera.CameraShortcutModifiers);
        var section = new DevicesSettingsSection();
        var window = new Window { Content = section };
        var button = section.FindControl<Button>("BtnWebcamDebugQuickRecal")!;
        try
        {
            loc.SetLanguage("en");
            (settings.WebcamQuickRecalHotkeyEnabled, camera.CameraShortcutGlobal, camera.CameraShortcutKey, camera.CameraShortcutModifiers) = (false, false, "J", "Control,Shift");
            WebcamQuickRecalWindow.HotkeyHint = () => null;
            window.Show();
            section.OnSectionShown();
            var english = Loc.Get("tooltip_devices_quick_recal");
            Assert.Equal(english + "\n\n" + Loc.GetF("webcam_quick_recal_hotkey_hint", "Ctrl+Alt+G", "Ctrl+Shift+J"), ToolTip.GetTip(button));
            WebcamQuickRecalWindow.HotkeyHint = () => "Ctrl+Alt+G / Ctrl+Alt+C";
            section.OnSectionShown();
            Assert.Equal(english + "\n\n" + Loc.GetF("webcam_quick_recal_hotkey_hint", "Ctrl+Alt+G", "Ctrl+Shift+J"), ToolTip.GetTip(button));
            WebcamQuickRecalWindow.HotkeyHint = () => null;
            section.OnSectionShown();
            Assert.Equal(english + "\n\n" + Loc.GetF("webcam_quick_recal_hotkey_hint", "Ctrl+Alt+G", "Ctrl+Shift+J"), ToolTip.GetTip(button));
            WebcamQuickRecalWindow.HotkeyHint = () => "Ctrl+Alt+G / Ctrl+Alt+C";
            loc.SetLanguage("de");
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(english, Loc.Get("tooltip_devices_quick_recal"));
            Assert.Equal(Loc.Get("tooltip_devices_quick_recal") + "\n\n" + Loc.GetF("webcam_quick_recal_hotkey_hint", "Ctrl+Alt+G", "Ctrl+Shift+J"), ToolTip.GetTip(button));
        }
        finally
        {
            window.Close();
            WebcamQuickRecalWindow.HotkeyHint = oldHint;
            (settings.WebcamQuickRecalHotkeyEnabled, camera.CameraShortcutGlobal, camera.CameraShortcutKey, camera.CameraShortcutModifiers) = old;
            loc.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
        }
    });
}
