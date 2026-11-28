using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The installer finds a running CCP by its single-instance mutex. A wrong name makes the check
/// dead: the force-kill re-check asked '{#MyAppName}_Mutex', which no build ever created, so a
/// hidden running app was never closed and setup failed on the locked exe (7.4.0 cut trap).
/// </summary>
public class InstallerMutexTests
{
    [Fact]
    public void Every_installer_mutex_check_names_the_apps_real_mutex()
    {
        var root = RepoRoot();
        var app = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "App.xaml.cs"));
        var appName = Regex.Match(app, "const string MutexName = \"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(appName));

        var iss = File.ReadAllText(Path.Combine(root, "installer.iss"));
        var define = Regex.Match(iss, "#define MyAppMutex \"([^\"]+)\"").Groups[1].Value;
        Assert.Equal(appName, define);

        Assert.Contains("AppMutex={#MyAppMutex}", iss);
        var checks = Regex.Matches(iss, @"CheckForMutexes\(([^)]*)\)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(2, checks.Count);   // the first look and the force-kill re-check
        Assert.All(checks, c => Assert.Equal("'{#MyAppMutex}'", c));
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
