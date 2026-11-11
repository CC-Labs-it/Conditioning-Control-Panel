using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SingleInstanceProcessTests
{
    // WPF App.xaml.cs:2147-2154,4157-4217: kill this build, reclaim the mutex, answer relaunches.
    // The linked production gate runs in child apphosts. KillStale is never replaced.
    [Fact]
    public async Task RealTakeoverKillsOnlyTheSameBuildAndProfile()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "SingleInstanceProbe");
        var root = Path.Combine(Path.GetTempPath(), "ccp-instance-" + Guid.NewGuid().ToString("N"));
        var children = new List<Process>();
        var app = OperatingSystem.IsWindows() ? "CCP.Avalonia.exe" : "CCP.Avalonia";
        Directory.CreateDirectory(root);
        string CopyBuild(string name)
        {
            var dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.GetFiles(source))
            {
                var dest = Path.Combine(dir, Path.GetFileName(file));
                File.Copy(file, dest);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dest, File.GetUnixFileMode(file));
            }
            return Path.Combine(dir, app);
        }
        async Task<Process> Start(string executable, string profile, string mode, string expected = "READY")
        {
            var info = new ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false };
            info.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../.."));
            info.ArgumentList.Add(mode);
            info.Environment["CCP_USERDATA_DIR"] = profile;
            var proc = Process.Start(info)!;
            children.Add(proc);
            Assert.Equal(expected, await proc.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
            return proc;
        }
        try
        {
            var build = CopyBuild("build");
            var otherBuild = CopyBuild("other-build");
            var profile = Path.Combine(root, "profile-a");
            var primary = await Start(build, profile, "wedged");
            var otherProfile = await Start(build, Path.Combine(root, "profile-b"), "healthy");
            var unrelatedBuild = await Start(otherBuild, profile, "marker");
            var unidentified = await Start(build, profile, "unknown");
            var takeover = await Start(build, profile, "healthy");
            if (!primary.WaitForExit(5000))
            {
                takeover.Kill();
                takeover.WaitForExit(5000);
                Assert.Fail("The stale primary was not terminated: " + await takeover.StandardError.ReadToEndAsync());
            }
            Assert.False(otherProfile.HasExited);
            Assert.False(unrelatedBuild.HasExited);
            Assert.False(unidentified.HasExited);
            var relaunch = await Start(build, profile, "healthy", "ACKED");
            Assert.True(relaunch.WaitForExit(5000));
            Assert.False(takeover.HasExited); // The replacement owns the gate and answers the relaunch.
        }
        finally
        {
            foreach (var child in children)
            {
                if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); }
                child.Dispose();
            }
            Directory.Delete(root, recursive: true);
        }
    }
}
