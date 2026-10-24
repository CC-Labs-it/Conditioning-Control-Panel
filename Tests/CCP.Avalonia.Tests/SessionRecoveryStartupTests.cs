using System;
using System.IO;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SessionRecoveryStartupTests
{
    [Fact]
    public void RecoveryRunsAfterSettingsLoadAndBeforeModsReadThem()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CCP.Avalonia", "App.axaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "CCP.Avalonia", "App.axaml.cs"));
        var load = source.IndexOf("Settings = new SettingsService();", StringComparison.Ordinal);
        var heal = source.IndexOf("CoreSessionSettingsJournal.HealOnLaunch(CorePaths.UserData, Settings.Current)", StringComparison.Ordinal);
        var save = source.IndexOf("CoreSettings.Save();", heal < 0 ? 0 : heal, StringComparison.Ordinal);
        Assert.True(load >= 0 && heal > load && save > heal);
        // WPF's startup contract: no service/mod consumes session overrides from the last run.
        var language = source.IndexOf("LocalizationManager.Instance.SetLanguage(Settings.Current.Language)", load, StringComparison.Ordinal);
        Assert.True(language > save);
    }
}
