using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Main 8a89eaeb0 / 8fe2a80a0 / 36370021a on this head: the launch heals a journal a crashed
/// session left behind before anything reads the settings, the runner journals into the user-data
/// folder, and a Takeover pink pulse is settled before the session snapshot.</summary>
public sealed class SessionJournalHeadTests
{
    [Fact]
    public void The_launch_heals_before_anything_reads_the_settings_and_the_runner_journals()
    {
        var app = File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs"));
        var seeded = app.IndexOf("CoreSettings.ServiceProvider = () => Settings;", StringComparison.Ordinal);
        var heal = app.IndexOf("SessionSettingsJournal.HealOnLaunch(CorePaths.UserData, Settings.Current)", StringComparison.Ordinal);
        var language = app.IndexOf("LocalizationManager.Instance.SetLanguage(Settings.Current.Language);", StringComparison.Ordinal);
        Assert.True(seeded > 0 && heal > seeded && language > heal, "heal right after the settings load");
        Assert.Contains("JournalDir = CorePaths.UserData,", app);
        Assert.Contains("SettleBeforeSnapshot = () => Views.Windows.MainShellWindow.Current?.SettleAutonomyPulses(),", app);

        var autonomy = File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "Views", "Windows", "MainShellWindow.Autonomy.cs"));
        Assert.Contains("internal void SettleAutonomyPulses() => EndPinkPulse(cancelled: true);", autonomy);
    }

    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
