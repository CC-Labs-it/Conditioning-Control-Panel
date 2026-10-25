using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ReleaseEvidenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ccp-release-" + Guid.NewGuid().ToString("N"));
    private static string Repo([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../.."));
    private string Write(string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
    private static string Run(string command, string directory, params string[] args)
    {
        var start = new ProcessStartInfo(command) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60000), "release command timed out");
        Assert.True(process.ExitCode == 0, stdout.Result + stderr.Result);
        return stdout.Result;
    }

    [Fact]
    public void LinuxTarballPlacesModelWhereSpeechEngineResolvesIt()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux packaging command");
        Write("packaging/linux/build-tarball.sh", File.ReadAllText(Path.Combine(Repo(), "packaging/linux/build-tarball.sh")));
        Write("Version.props", "<Version>1.2.3</Version>\n");
        Write("LICENSE", "fixture license");
        Write("packaging/linux/share/applications/test.desktop", "fixture desktop");
        // Only the expensive publish/download boundaries are substituted. The packaging script,
        // archive extraction and runtime model discovery are real.
        Write(".github/scripts/fetch-vosk-model.sh", "echo \"$(dirname \"$0\")/../../model/vosk-model-small-en-us-0.15\"\n");
        Write("model/vosk-model-small-en-us-0.15/am/final.mdl", "acoustic fixture");
        Write("model/vosk-model-small-en-us-0.15/conf/mfcc.conf", "config fixture");
        Write("bin/dotnet", "#!/bin/bash\nmkdir -p CCP.Avalonia/bin/publish/linux-x64\ntouch CCP.Avalonia/bin/publish/linux-x64/libvosk.so\n");
        Run("chmod", root, "+x", Path.Combine(root, "bin/dotnet"));
        var archive = Run("bash", root, "-c", "PATH=\"$PWD/bin:$PATH\" bash packaging/linux/build-tarball.sh").Trim();
        var extracted = Directory.CreateDirectory(Path.Combine(root, "extracted")).FullName;
        Run("tar", root, "xzf", archive, "-C", extracted);
        var install = Path.Combine(extracted, "ConditioningControlPanel");
        var relative = Path.GetRelativePath(AppContext.BaseDirectory, SpeechEngine.DefaultModelRoots[0]);
        Assert.Equal(Path.Combine("Resources", "Models", "vosk"), relative);
        var model = SpeechEngine.ResolveModelDir(Path.Combine(install, relative));
        Assert.NotNull(model);
        Assert.Equal("acoustic fixture", File.ReadAllText(Path.Combine(model!, "am/final.mdl")));
        Assert.True(File.Exists(Path.Combine(install, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(install, "libvosk.so")));
    }

    [Fact]
    public void ContentPackKeepsInstallPathsAndExactDeletionNames()
    {
        Write("Assets/sounds/flashes_audio/MixedCase.mp3", "audio fixture");
        Write("Assets/sounds/flashes_audio/manifest.json", "stays in box");
        Write("ConditioningControlPanel/DroneMod/drone-mode.ccpmod", "mod fixture");
        // Load the actual release functions with PowerShell's parser; avoid executing the
        // full multi-gigabyte release. Windows long-path and parent-path syntax are shimmed off Windows.
        var script = Write("check.ps1", """
            param($Source, $Root)
            $ErrorActionPreference = 'Stop'
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($Source, [ref]$null, [ref]$null)
            $ast.FindAll({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst]}, $false) | ForEach-Object { Invoke-Expression $_.Extent.Text }
            if (-not $IsWindows) {
                function Get-LongPath([string]$Path) { return $Path }
                function Split-Path([string]$Path, [switch]$Parent) {
                    $Path = $Path.Replace('/', '\')
                    return $Path.Substring(0, $Path.LastIndexOf('\'))
                }
            }
            $RepoRoot = $Root
            $ProjectDir = Join-Path $Root 'ConditioningControlPanel'
            $FixedStamp = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $ExtraDirIfEmpty = @()
            $spec = @{ Id='fixture'; Folders=@(@{Path='Resources\sounds\flashes_audio'; Ext=@('.mp3')}); Files=@(@{Path='DroneMod\drone-mode.ccpmod'; Entry='packs/drone-mode.ccpmod'}) }
            $PackSpecs = @($spec)
            $files = @(Resolve-PackFiles $spec)
            New-PackZip $spec (Join-Path $Root 'pack.zip') $files
            Write-InstallerDeletions ([ordered]@{fixture=$files}) (Join-Path $Root 'deletions.iss')
            """);
        Run("pwsh", root, "-NoProfile", "-File", script, Path.Combine(Repo(), "ConditioningControlPanel/Scripts/build-content-packs.ps1"), root);
        using var zip = ZipFile.OpenRead(Path.Combine(root, "pack.zip"));
        Assert.Equal(new[] { "Resources/sounds/flashes_audio/MixedCase.mp3", "packs/drone-mode.ccpmod" }, zip.Entries.Select(e => e.FullName));
        using var reader = new StreamReader(zip.Entries[0].Open());
        Assert.Equal("audio fixture", reader.ReadToEnd());
        Assert.All(zip.Entries, e => Assert.Equal(2000, e.LastWriteTime.Year));
        var deletions = File.ReadAllText(Path.Combine(root, "deletions.iss"));
        Assert.Contains("{app}\\Resources\\sounds\\flashes_audio\\MixedCase.mp3", deletions);
        Assert.Contains("{app}\\DroneMod\\drone-mode.ccpmod", deletions);
        Assert.DoesNotContain("manifest.json", deletions);
        Assert.DoesNotContain("*", deletions);
        Assert.DoesNotContain(root, deletions);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
