using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;
using CCP.Tests.Shared;

namespace CCP.Avalonia.Tests;

/// <summary>The installer's Content Folder pick reaches the Avalonia head (WPF ApplyInstallerAssetsPath).</summary>
public sealed class InstallerAssetsPathTests
{
    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    [Theory]
    [InlineData("installer.iss")]
    [InlineData("installer-avalonia.iss")]
    public void ReadsTheExactKeyAndValueTheInstallerWrites(string installer)
    {
        var iss = InstallerSource.Read(RepoRoot(), installer);
        string Define(string name) => Regex.Match(iss, "#define " + name + " \"([^\"]+)\"").Groups[1].Value;

        var write = Regex.Match(iss, @"RegWriteStringValue\(HKEY_CURRENT_USER,\s*'([^']+)',\s*'([^']+)',\s*SelectedAssetsPath\)");
        Assert.True(write.Success, "installer.iss no longer writes the Content Folder pick with RegWriteStringValue");
        var subKey = write.Groups[1].Value
            .Replace("{#MyAppPublisher}", Define("MyAppPublisher"))
            .Replace("{#MyAppName}", Define("MyAppName"));

        Assert.Equal(InstallerAssetsPath.SubKey, subKey);
        Assert.Equal(InstallerAssetsPath.ValueName, write.Groups[2].Value);
    }

    [Fact]
    public void AdoptsOnlyAnExistingNonDefaultFolderWhenTheUserHasNone()
    {
        const string Default = @"C:\Users\u\AppData\Local\ConditioningControlPanel\assets";
        Func<string, bool> yes = _ => true, no = _ => false;

        Assert.Equal(@"E:\ccp", InstallerAssetsPath.PathToAdopt(@"E:\ccp", Default, null, yes));
        Assert.Equal(@"E:\ccp", InstallerAssetsPath.PathToAdopt(@"E:\ccp", Default, "  ", yes));
        Assert.Null(InstallerAssetsPath.PathToAdopt(@"E:\ccp", Default, @"D:\mine", yes));   // a user-set path is never overwritten
        Assert.Null(InstallerAssetsPath.PathToAdopt(@"E:\ccp", Default, null, no));          // missing folder
        Assert.Null(InstallerAssetsPath.PathToAdopt(Default.ToUpperInvariant(), Default, null, yes)); // the default itself
        Assert.Null(InstallerAssetsPath.PathToAdopt("", Default, null, yes));
        Assert.Null(InstallerAssetsPath.PathToAdopt(null, Default, null, yes));
    }

    [Fact]
    public void RegistryValueIsAdoptedOnceThenDeleted()
    {
        if (!OperatingSystem.IsWindows()) return;
        var subKey = @"Software\CCP.Avalonia.Tests\" + Guid.NewGuid().ToString("N");
        var folder = Directory.CreateTempSubdirectory("ccp-installer-assets").FullName;
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey))
                k.SetValue(InstallerAssetsPath.ValueName, folder);

            string? custom = null;
            InstallerAssetsPath.Apply(subKey, @"C:\nowhere\assets", () => custom, p => custom = p);
            Assert.Equal(folder, custom);
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey))
                Assert.Null(k!.GetValue(InstallerAssetsPath.ValueName));

            // A user-set path stays, and the value still goes.
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey))
                k.SetValue(InstallerAssetsPath.ValueName, folder);
            custom = @"D:\mine";
            InstallerAssetsPath.Apply(subKey, @"C:\nowhere\assets", () => custom, p => custom = p);
            Assert.Equal(@"D:\mine", custom);
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey))
                Assert.Null(k!.GetValue(InstallerAssetsPath.ValueName));
        }
        finally
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\CCP.Avalonia.Tests", throwOnMissingSubKey: false); } catch { }
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    [Fact]
    public void StartUpAppliesItAfterSettingsLoadAndBeforeTheAssetsFolderResolves()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "App.axaml.cs"));
        var load = src.IndexOf("Settings = new SettingsService();", StringComparison.Ordinal);
        var apply = src.IndexOf("Platform.InstallerAssetsPath.Apply(Settings);", StringComparison.Ordinal);
        var resolve = src.IndexOf("CorePaths.EffectiveAssetsProvider = ResolveEffectiveAssetsPath;", StringComparison.Ordinal);
        Assert.True(load >= 0 && apply > load && resolve > apply, "InstallerAssetsPath.Apply must run after settings load and before EffectiveAssetsProvider is seeded");
    }
}
