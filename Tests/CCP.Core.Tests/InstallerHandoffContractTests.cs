using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// Text-level cross-head contract, not a substitute for a real Windows upgrade (part B).
public partial class InstallerHandoffContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "installer.iss"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
    private static string Read(string path) => File.ReadAllText(Path.Combine(RepoRoot(), path));
    private static string Define(string source, string name) =>
        Regex.Match(source, "#define " + name + " \"([^\"]+)\"").Groups[1].Value;
    private static string Directive(string source, string name) =>
        Regex.Match(source, "(?m)^" + name + "=(.+)$").Groups[1].Value.Trim();
    private static string Expand(string source, string text) =>
        Regex.Replace(text, @"\{#(\w+)\}", m => Define(source, m.Groups[1].Value));

    [Theory]
    [InlineData("installer.iss")]
    [InlineData("installer-avalonia.iss")]
    public void InstalledIdentityMatchesBothHeads(string file)
    {
        var iss = Read(file);
        Assert.Equal("{{A7B9C3D1-E5F2-4A8B-9C1D-2E3F4A5B6C7D}", Directive(iss, "AppId"));
        Assert.Equal(@"{autopf}\Conditioning Control Panel", Expand(iss, Directive(iss, "DefaultDirName")));
        Assert.Equal("yes", Directive(iss, "UsePreviousAppDir"));
        Assert.Equal("yes", Directive(iss, "UsePreviousPrivileges"));
        Assert.Equal("lowest", Directive(iss, "PrivilegesRequired"));
        Assert.Equal("dialog", Directive(iss, "PrivilegesRequiredOverridesAllowed"));
        Assert.DoesNotMatch(@"(?im)^\s*UninstallFilesDir\s*=", iss);
        Assert.Contains("Path.Combine(exeDir, \"unins000.exe\")", Read("CCP.Core/Services/Update/ReleaseFeed.cs"));
        var mutex = Directive(iss, "AppMutex");
        Assert.Equal("ConditioningControlPanel_SingleInstance_Mutex", mutex);
        foreach (var head in new[] { "ConditioningControlPanel/App.xaml.cs", "CCP.Avalonia/Platform/SingleInstance.cs" })
            Assert.Contains("const string MutexName = \"" + mutex + "\"", Read(head));
        if (file == "installer-avalonia.iss")
        {
            Assert.Equal("CCP.Avalonia.exe", Define(iss, "SourceExeName"));
            Assert.Contains("DestName: \"{#MyAppExeName}\"", iss);
            var checks = Regex.Matches(iss, @"CheckForMutexes\('([^']+)'\)");
            Assert.Equal(2, checks.Count);
            foreach (Match check in checks) Assert.Equal(mutex, check.Groups[1].Value);
        }
        var exe = Define(iss, "MyAppExeName");
        Assert.Equal("ConditioningControlPanel.exe", exe);
        foreach (var updater in new[] { "ConditioningControlPanel/Services/Update/UpdateService.cs", "CCP.Avalonia/Platform/AppUpdater.cs" })
        {
            Assert.Contains("Path.Combine(installPath, \"" + exe + "\")", Read(updater));
            Assert.Contains("ReleaseFeed.BuildUpdateHelperScript(", Read(updater));
        }
        Assert.Contains("Filename: \"{app}\\{#MyAppExeName}\"", iss);
        Assert.Contains("/IM {#MyAppExeName}", iss);
        var version = XDocument.Parse(Read("Version.props")).Descendants("Version").Single().Value;
        Assert.Equal(version, Define(iss, "MyAppVersion"));
        Assert.Contains("set VERSION=" + version, Read("build-installer.bat"));
        var asset = Expand(iss, Directive(iss, "OutputBaseFilename")) + ".exe";
        Assert.Equal($"ConditioningControlPanel-{version}-Setup.exe", asset);
        var url = "https://example.test/" + asset;
        var feed = JsonSerializer.Serialize(new { assets = new[] { new { name = asset, browser_download_url = url } } });
        Assert.Equal(url, ReleaseFeed.FindSetupAssetUrl(feed, version, "v" + version));
    }

    [Theory]
    [InlineData("installer.iss")]
    [InlineData("installer-avalonia.iss")]
    public void RegistryAndUserDataMatchBothHeads(string file)
    {
        var iss = Read(file);
        const string key = @"Software\CodeBambi\Conditioning Control Panel";
        foreach (var value in new[] { "InstallPath", "Version" })
        {
            var row = iss.Split('\n').Single(l => l.StartsWith("Root: HKCU;") && l.Contains("ValueName: \"" + value + "\""));
            Assert.Contains("Subkey: \"" + key + "\"", Expand(iss, row));
            Assert.Contains("ValueData: \"" + (value == "InstallPath" ? "{app}" : "{#MyAppVersion}") + "\"", row);
            Assert.Contains("GetValue(\"" + value + "\")", Read("ConditioningControlPanel/Services/Update/UpdateService.cs"));
        }
        Assert.Contains("OpenSubKey(@\"" + key + "\")", Read("ConditioningControlPanel/Services/Update/UpdateService.cs"));
        var updater = Read("CCP.Avalonia/Platform/AppUpdater.cs");
        Assert.Contains("OpenSubKey(@\"" + key + "\")", updater);
        Assert.Contains("GetValue(\"InstallPath\")", updater);
        var assets = Regex.Match(iss, @"RegWriteStringValue\(HKEY_CURRENT_USER,\s*'([^']+)',\s*'([^']+)',\s*SelectedAssetsPath\)");
        Assert.True(assets.Success);
        Assert.Equal(key, Expand(iss, assets.Groups[1].Value));
        Assert.Equal("AssetsPath", assets.Groups[2].Value);
        var reader = Read("CCP.Avalonia/Platform/InstallerAssetsPath.cs");
        Assert.Contains("const string SubKey = @\"" + key + "\"", reader);
        Assert.Contains("const string ValueName = \"AssetsPath\"", reader);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConditioningControlPanel"), CorePaths.UserDataFor(null));
        Assert.Contains("public static string UserDataPath => CorePaths.UserData;", Read("ConditioningControlPanel/App.xaml.cs"));
        Assert.Contains("Settings = new SettingsService();", Read("CCP.Avalonia/App.axaml.cs"));
        Assert.Contains("Path.Combine(CorePaths.UserData, \"settings.json\")", Read("CCP.Core/Services/Settings/SettingsService.cs"));
    }

    [Fact]
    public void StandaloneAvaloniaPreservesWpfAssociations()
    {
        var wpf = Read("installer.iss");
        var ava = Read("installer-avalonia.iss");
        Assert.DoesNotContain("#include \"installer.iss\"", ava);
        Assert.Equal(@"Assets\app.ico", Directive(ava, "SetupIconFile"));
        Assert.NotEmpty(Read(Directive(ava, "SetupIconFile").Replace('\\', '/')));
        Assert.Equal(@"CCP.Avalonia\bin\publish\win-x64", Define(ava, "PublishDir"));
        Assert.Contains("Name: \"fileassoc\"", wpf);
        Assert.Contains("Tasks: fileassoc", wpf);
        Assert.Contains("if not WizardIsTaskSelected('fileassoc') then", wpf);
        Assert.DoesNotContain("Name: \"fileassoc\"", ava);
        Assert.DoesNotContain("Tasks: fileassoc", ava);
        Assert.DoesNotContain("WizardIsTaskSelected('fileassoc')", ava);
        Assert.DoesNotContain("Type: files; Name: \"{app}\\WebView2Loader.dll\"", wpf);
        Assert.Matches(@"Type: files;\s+Name: ""\{app\}\\WebView2Loader.dll""", ava);
    }

    // Transcribed from origin/main aa9918833722f057079e455167cb3d1b14462340:
    // ConditioningControlPanel/Services/Update/UpdateService.cs:775-786 (ordered patterns + regex),
    // :1044 (relaunch exe), :1163 and :1197 (/DIR and silent arguments).
    // No moving remote ref is fetched during tests. Recheck the shipping release in Part B.
    [Fact]
    public void MainWpfUpdaterSelectsProductionButRejectsUnsignedCiArtifact()
    {
        var version = XDocument.Parse(Read("Version.props")).Descendants("Version").Single().Value;
        var patterns = new[] { $"-{version}-Setup.exe", $"-v{version}-Setup.exe", "Installer.exe", "Setup.exe" };
        string? MainSelect(string response)
        {
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(response, "\"browser_download_url\"\\s*:\\s*\"([^\"]*" + Regex.Escape(pattern) + "[^\"]*)\"", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups[1].Value;
            }
            return null;
        }
        var production = $"ConditioningControlPanel-{version}-Setup.exe";
        var unsigned = $"ConditioningControlPanel-{version}-Setup.UNSIGNED-avalonia.exe";
        foreach (var name in new[] { production, unsigned })
        {
            var url = "https://example.test/" + name;
            var feed = JsonSerializer.Serialize(new { assets = new[] { new { name, browser_download_url = url } } });
            Assert.Equal(name == production ? url : null, MainSelect(feed));
            Assert.Equal(MainSelect(feed), ReleaseFeed.FindSetupAssetUrl(feed, version, "v" + version));
        }
        Assert.Equal("ConditioningControlPanel.exe", Define(Read("installer-avalonia.iss"), "MyAppExeName"));
        var workflow = Read(".github/workflows/build.yml");
        Assert.Contains("Move-Item $setup -Destination $unsigned", workflow);
        Assert.Contains("ConditioningControlPanel-$version-Setup.UNSIGNED-avalonia.exe", workflow);
        Assert.Contains("path: installer-output/*-Setup.UNSIGNED-avalonia.exe", workflow);
    }

    [Fact]
    public void SharedHelperKeepsSilentArgumentsAndPreviousDirectoryFallback()
    {
        var script = ReleaseFeed.BuildUpdateHelperScript("setup.exe", "installed", 123, "app.exe", "log", "result", false);
        Assert.Contains("/SILENT /SUPPRESSMSGBOXES /NORESTART /DIR=\"installed\"", script);
        var fallback = ReleaseFeed.BuildUpdateHelperScript("setup.exe", null, 123, "app.exe", "log", "result", false);
        Assert.DoesNotContain("/DIR=", fallback);
    }
}
