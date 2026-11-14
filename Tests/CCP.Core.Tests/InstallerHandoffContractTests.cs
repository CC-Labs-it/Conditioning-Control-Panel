using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
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
    private static string Read(string path)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), path));
        if (path == "installer-avalonia.iss")
            text = Regex.Replace(text, "#include \"(installer-avalonia-code-[123456]\\.iss)\"",
                m => File.ReadAllText(Path.Combine(RepoRoot(), m.Groups[1].Value)));
        return text;
    }
    private static string RepoPath(string path) => Path.Combine(RepoRoot(), path);
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
    public void AvaloniaCodeFragmentsAreVerbatimWpfBaseExcerpts()
    {
        var wpf = Read("installer.iss");
        for (var i = 1; i <= 6; i++)
        {
            var fragment = Read($"installer-avalonia-code-{i}.iss");
            Assert.NotEmpty(fragment);
            Assert.Contains(fragment, wpf);
        }
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
        Assert.Contains("Name: \"fileassoc\"", ava);
        Assert.Contains("Tasks: fileassoc", ava);
        Assert.Contains("WizardIsTaskSelected('fileassoc')", ava);
        Assert.DoesNotContain("Type: files; Name: \"{app}\\WebView2Loader.dll\"", wpf);
        Assert.Matches(@"Type: files;\s+Name: ""\{app\}\\WebView2Loader.dll""", ava);
    }

    [Fact]
    public void ExplorerAssociationsMatchWpfRegistryContract()
    {
        var wpf = Read("installer.iss");
        var ava = Read("installer-avalonia.iss");
        static string[] Rows(string text) => text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("Root: HKCU;") && line.Contains("Tasks: fileassoc"))
            .OrderBy(line => line, StringComparer.Ordinal).ToArray();
        var expected = Rows(wpf);
        Assert.Equal(34, expected.Length);
        Assert.Equal(expected, Rows(ava));
        var task = ava.Split('\n').Single(line => line.StartsWith("Name: \"fileassoc\""));
        Assert.Contains("Flags: unchecked", task);
        Assert.Matches(@"if not WizardIsTaskSelected\('fileassoc'\) then\s+CleanupFileAssociations\(\);", ava);
    }

    // WPF csproj:146,945-953 ships LibVLC 3.0.21 under {app}\libvlc; Avalonia
    // csproj:81,311-315 ships 3.0.24 there. These are the old-only publish paths.
    [Fact]
    public void UpgradeRemovesOnlyRetiredWpfLibVlcFiles()
    {
        Assert.Contains("VideoLAN.LibVLC.Windows\" Version=\"3.0.21\"", Read("ConditioningControlPanel/ConditioningControlPanel.csproj"));
        Assert.Contains("VideoLAN.LibVLC.Windows\" Version=\"3.0.24\"", Read("CCP.Avalonia/CCP.Avalonia.csproj"));
        var installer = Read("installer-avalonia.iss");
        var section = installer.Replace("\r\n", "\n").Split("\n[InstallDelete]\n")[1].Split("\n[Icons]\n")[0];
        var rows = Regex.Matches(section, @"(?im)^Type:\s*([^;]+);\s*Name:\s*""(\{app\}\\libvlc(?:\\[^""\r\n]*)?)""")
            .Select(m => (Type: m.Groups[1].Value.Trim(), Path: m.Groups[2].Value)).ToArray();
        var expected = new[]
        {
            @"{app}\libvlc\lua\playlist\youtube.luac",
            @"{app}\libvlc\plugins\access\libbluray-awt-j2se-1.3.2.jar",
            @"{app}\libvlc\plugins\access\libbluray-j2se-1.3.2.jar",
            @"{app}\libvlc\plugins\codec\libschroedinger_plugin.dll",
            @"{app}\libvlc\plugins\codec\libsdl_image_plugin.dll",
            @"{app}\libvlc\win-x64\lua\playlist\youtube.luac",
            @"{app}\libvlc\win-x86\lua\playlist\youtube.luac",
        };
        Assert.All(rows, row => Assert.Equal("files", row.Type));
        Assert.Equal(expected.OrderBy(x => x), rows.Select(row => row.Path).OrderBy(x => x));
    }

    // Transcribed from origin/main aa9918833722f057079e455167cb3d1b14462340:
    // ConditioningControlPanel/Services/Update/UpdateService.cs:775-786 (ordered patterns + regex),
    // :1044 (relaunch exe), :1163 and :1197 (/DIR and silent arguments).
    // The published v7.1.15 tag b46f52ece11dc78a88c2c77f0a6f8922646e7d7f has the same
    // UpdateService.cs:775-786,1044,1163,1197 contract. No moving ref is fetched in tests;
    // a live Windows handoff and any later shipping release still need verification.
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

    // WPF BrowserService.cs:127-131 / :187 creates the old profile. The port's
    // WebViewProfile.cs:19-31 deliberately creates a separate one. Tell upgraders
    // before installation rather than silently losing their embedded web sessions.
    [Fact]
    public void InteractiveAvaloniaInstallerExplainsBrowserLoginResetBeforeInstalling()
    {
        var installer = Read("installer-avalonia.iss");
        var notice = Read(Directive(installer, "InfoBeforeFile"));
        Assert.Contains("Embedded browser logins reset when upgrading from WPF.", notice);
        Assert.Contains("Sign in again to websites opened inside CCP.", notice);
        Assert.Contains("browser_data_avalonia", notice);
        Assert.Contains("The old browser_data profile is kept.", notice);
        Assert.Contains("does not clear CCP account tokens, settings or progress.", notice);
        Assert.DoesNotContain("InfoBeforeFile=INSTALL_AVALONIA.txt", Read("installer.iss"));
        // Seen on the page, so the app's notice (AppUpdater.BrowserResetAcknowledged) is not owed again.
        Assert.Contains("if not WizardSilent and not IsAdminInstallMode and", installer);
        Assert.Contains(@"{localappdata}\ConditioningControlPanel\browser-reset.ack.json", installer);
    }

    // Restack b161: #3950's WPF installer.iss / build-installer.bat edits (net10 publish dir,
    // Assets icon) and their test are left out. The default installer and batch stay
    // byte-identical to origin/main (shell-installer ledger row), so that fix is a re-cut.

    [Fact]
    public void InstallerNoticeUsesTheSelectedLanguageAndMatchesCoreTranslations()
    {
        var languages = new[]
        {
            ("en", "english", "Default.isl", "INSTALL_AVALONIA.txt"),
            ("zh-CN", "chinesesimplified", @"Default.isl,installer-notices\zh-CN.isl", "installer-notices/zh-CN.txt"),
            ("ja", "japanese", @"Languages\Japanese.isl", "installer-notices/ja.txt"),
            ("ko", "korean", @"Default.isl,installer-notices\ko.isl", "installer-notices/ko.txt"),
            ("es", "spanish", @"Languages\Spanish.isl", "installer-notices/es.txt"),
            ("pt-BR", "brazilianportuguese", @"Languages\BrazilianPortuguese.isl", "installer-notices/pt-BR.txt"),
            ("fr", "french", @"Languages\French.isl", "installer-notices/fr.txt"),
            ("it", "italian", @"Languages\Italian.isl", "installer-notices/it.txt"),
            ("de", "german", @"Languages\German.isl", "installer-notices/de.txt"),
            ("ru", "russian", @"Languages\Russian.isl", "installer-notices/ru.txt"),
        };
        Assert.Equal(LocalizationManager.AvailableLanguages.Select(x => x.Code).OrderBy(x => x), languages.Select(x => x.Item1).OrderBy(x => x));
        var section = Read("installer-avalonia.iss").Split("[Languages]")[1].Split("[Tasks]")[0];
        Assert.Equal(languages.Length, section.Split('\n').Count(line => line.TrimStart().StartsWith("Name: \"")));
        var utf8Bom = new byte[] { 0xef, 0xbb, 0xbf };
        foreach (var (code, nativeName, id, page) in new[] { ("zh-CN", "简体中文", "$0804", "936"), ("ko", "한국어", "$0412", "949") })
        {
            var overlayFile = $"installer-notices/{code}.isl";
            Assert.True(File.ReadAllBytes(RepoPath(overlayFile)).AsSpan().StartsWith(utf8Bom));
            var overlay = Read(overlayFile);
            Assert.Contains("[LangOptions]", overlay);
            Assert.Contains("LanguageName=" + nativeName, overlay);
            Assert.Contains("LanguageID=" + id, overlay);
            Assert.Contains("LanguageCodePage=" + page, overlay);
        }
        string? english = null;
        foreach (var (code, name, messages, file) in languages)
        {
            Assert.Contains($"Name: \"{name}\"; MessagesFile: \"compiler:{messages}\"; InfoBeforeFile: \"{file.Replace('/', '\\')}\"", section);
            using var json = JsonDocument.Parse(Read($"CCP.Core/Localization/Languages/{code}.json"));
            var translation = json.RootElement.GetProperty("installer_avalonia_upgrade_notice").GetString();
            Assert.False(string.IsNullOrWhiteSpace(translation));
            if (code != "en") Assert.True(File.ReadAllBytes(RepoPath(file)).AsSpan().StartsWith(utf8Bom));
            Assert.Equal(translation!.Replace("\r\n", "\n").TrimEnd(), Read(file).Replace("\r\n", "\n").TrimEnd());
            Assert.Contains("browser_data_avalonia", translation);
            Assert.Contains("browser_data", translation);
            Assert.DoesNotContain("!", translation);
            Assert.DoesNotContain("—", translation);
            Assert.DoesNotContain("–", translation);
            if (code == "en") english = translation; else Assert.NotEqual(english, translation);
        }
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
