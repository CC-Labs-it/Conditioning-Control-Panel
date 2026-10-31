using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

// Windows tools run in release CI; these source guards pin the head and upgrade identity on Linux too.
public sealed class WindowsInstallerContractTests
{
    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
    private static string Read(string file) => File.ReadAllText(Path.Combine(Root(), file));

    [Fact]
    public void PublishesTheAvaloniaProfileAtTheCurrentReleaseVersion()
    {
        var batch = Read("build-installer.bat");
        var version = XDocument.Parse(Read("Version.props")).Descendants("Version").Single().Value;
        Assert.Contains("set VERSION=" + version, batch);
        Assert.Contains("set PROJECT_DIR=CCP.Avalonia", batch);
        Assert.Contains(@"set PUBLISH_DIR=%PROJECT_DIR%\bin\publish\win-x64", batch);
        Assert.Contains("set APP_EXE=CCP.Avalonia.exe", batch);
        Assert.Contains(@"publish %PROJECT_DIR%\CCP.Avalonia.csproj -c Release -p:PublishProfile=win-x64", batch);
        var profile = XDocument.Parse(Read("CCP.Avalonia/Properties/PublishProfiles/win-x64.pubxml"));
        Assert.Equal(@"bin\publish\win-x64\", profile.Descendants("PublishDir").Single().Value);
        Assert.Contains("#define MyAppVersion \"" + version + "\"", Read("installer.iss"));
    }

    [Fact]
    public void RefusesUnsignedAvaloniaBeforeStaging()
    {
        var batch = Read("build-installer.bat");
        var check = batch.IndexOf("Get-AuthenticodeSignature", StringComparison.Ordinal);
        var stage = batch.IndexOf("robocopy ", StringComparison.Ordinal);
        Assert.True(check >= 0 && stage > check, "signature refusal must precede the staging copy");
        var refusal = batch[check..stage];
        Assert.Contains("%PUBLISH_DIR%\\%APP_EXE%", refusal);
        Assert.Contains(".Status -eq 'NotSigned'", refusal);
        Assert.Contains("if errorlevel 1 (", refusal);
        Assert.Contains("exit /b 1", refusal);
    }

    [Fact]
    public void InstallsRenamedAvaloniaWithTheExistingUpgradeIdentity()
    {
        var installer = Read("installer.iss");
        Assert.Contains("#define SourceExeName \"CCP.Avalonia.exe\"", installer);
        Assert.Contains("#define PublishDir \"CCP.Avalonia\\bin\\publish\\win-x64\"", installer);
        Assert.Contains("SetupIconFile=Assets\\app.ico", installer);
        Assert.Contains("Source: \"{#PublishDir}\\{#SourceExeName}\"; DestDir: \"{app}\"; DestName: \"{#MyAppExeName}\"", installer);
        Assert.Contains("Excludes: \"\\{#SourceExeName},", installer);
        Assert.Contains("#define MyAppExeName \"ConditioningControlPanel.exe\"", installer);
        Assert.Contains("AppId={{A7B9C3D1-E5F2-4A8B-9C1D-2E3F4A5B6C7D}", installer);
        Assert.Contains("UsePreviousAppDir=yes", installer);
        Assert.Contains("UsePreviousPrivileges=yes", installer);
        Assert.Contains("AppMutex=ConditioningControlPanel_SingleInstance_Mutex", installer);
        Assert.Contains("Type: files;      Name: \"{app}\\WebView2Loader.dll\"", installer);
        Assert.DoesNotContain("Name: \"fileassoc\";", installer);
        Assert.Contains("    CleanupFileAssociations();", installer);
    }
}
