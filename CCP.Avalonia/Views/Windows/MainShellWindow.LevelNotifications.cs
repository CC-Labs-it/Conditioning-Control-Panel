using System;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private static Action? _stopLevelUpSound;

    // Cancel work overtaken by panic or shell close; fresh level events still follow WPF.
    private void PostLevelNotice(Action show)
    {
        var panic = PanicAftermath.PanicCount;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_heroClosed || panic != PanicAftermath.PanicCount) return;
            show();
        });
    }
    // WPF MainWindow.xaml.cs:762-777: localized player notice, then audio.
    internal static void NotifyPlayerLevelUp(int level)
    {
        Platform.OsNotifications.Show(Loc.Get("toast_level_up_title"), Loc.GetF("toast_level_up_body", level));
        PlayLevelUpSound();
    }

    // WPF MainWindow.Companion.cs:48-77: mod-aware names, max takes precedence over tens.
    internal static void NotifyCompanionLevelUp(CompanionId id, int level)
    {
        bool max = level == CompanionProgress.MaxLevel;
        if (!max && level % 10 != 0) return;
        var name = CoreMods.MakeModAware(CompanionDefinition.GetById(id).Name);
        Platform.OsNotifications.Show(Loc.Get(max ? "toast_companion_max_title" : "toast_companion_level_title"),
            Loc.GetF(max ? "toast_companion_max_body" : "toast_companion_level_body", name, level));
        PlayLevelUpSound();
    }

    // WPF MainWindow.xaml.cs:784-830 PlayLevelUpSound: same file precedence and volume curve/floor.
    internal static void PlayLevelUpSound(string? baseDirectory = null)
    {
        try
        {
            var root = baseDirectory ?? AppContext.BaseDirectory;
            var path = new[]
            {
                Path.Combine(root, "Resources", "sounds", "lvup.mp3"),
                Path.Combine(root, "Resources", "lvlup.mp3"),
                Path.Combine(root, "assets", "sounds", "lvlup.mp3"),
            }.FirstOrDefault(File.Exists);
            if (path == null) return; // WPF keeps the previous clip if no replacement exists.
            StopLevelUpSound();
            var volume = (float)Math.Pow(CoreSettings.Current.MasterVolume / 100f, 1.5) * 0.2625f;
            _stopLevelUpSound = CoreAudio.PlayStoppable(path, Math.Max(0.01f, volume), "level-up");
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to play level up sound"); }
    }

    internal static void StopLevelUpSound()
    {
        try { Interlocked.Exchange(ref _stopLevelUpSound, null)?.Invoke(); }
        catch (Exception ex) { Log.Debug(ex, "Failed to stop level up sound"); }
    }
}
