using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;
using CCP.Tests.Shared;

namespace CCP.Core.Tests;

// Text-level cross-head contract, not a substitute for a real Windows upgrade (part B).
public class InstallerHandoffContractTests
{
    private static string Read(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "installer.iss"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return path.EndsWith(".iss")
            ? InstallerSource.Read(dir!.FullName, path)
            : File.ReadAllText(Path.Combine(dir!.FullName, path));
    }
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
        Assert.Equal("", Directive(iss, "UninstallFilesDir"));
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
    public void WpfPublishPathsMatchTheProjectAndIconExists()
    {
        var project = XDocument.Parse(Read("ConditioningControlPanel/ConditioningControlPanel.csproj"));
        var framework = project.Descendants("TargetFramework").Single().Value;
        var expected = @"ConditioningControlPanel\bin\Release\" + framework + @"\win-x64\publish";
        var iss = Read("installer.iss");
        Assert.Equal(expected, Define(iss, "PublishDir"));
        var batch = Read("build-installer.bat");
        Assert.Contains("set PROJECT_DIR=ConditioningControlPanel", batch);
        Assert.Contains("set PUBLISH_DIR=" + expected.Replace("ConditioningControlPanel", "%PROJECT_DIR%"), batch);
        Assert.Equal(@"Assets\app.ico", Directive(iss, "SetupIconFile"));
        // Read verifies the icon exists at the repository-relative path on every OS.
        Assert.NotEmpty(Read(Directive(iss, "SetupIconFile").Replace('\\', '/')));
    }

    [Fact]
    public void WrapperSelectsOnlyAvaloniaAndPreservesWpfAssociations()
    {
        var wpf = Read("installer.iss");
        var ava = Read("installer-avalonia.iss");
        Assert.Equal(@"CCP.Avalonia\bin\publish\win-x64", Define(ava, "PublishDir"));
        Assert.Contains("Name: \"fileassoc\"", wpf);
        Assert.Contains("Tasks: fileassoc", wpf);
        Assert.Contains("if not WizardIsTaskSelected('fileassoc') then", wpf);
        Assert.DoesNotContain("Name: \"fileassoc\"", ava);
        Assert.DoesNotContain("Tasks: fileassoc", ava);
        Assert.DoesNotContain("WizardIsTaskSelected('fileassoc')", ava);
        Assert.DoesNotContain("Type: files; Name: \"{app}\\WebView2Loader.dll\"", wpf);
        Assert.Contains("Type: files; Name: \"{app}\\WebView2Loader.dll\"", ava);
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
