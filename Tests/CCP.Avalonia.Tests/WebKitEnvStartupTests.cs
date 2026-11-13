using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace CCP.Avalonia.Tests;

// live-safety S18: Program.Main must put WEBKIT_DISABLE_DMABUF_RENDERER=1 in the NATIVE environment (libc getenv is what
// WebKitGTK and its web process read; .NET keeps its own copy) on NVIDIA, and never over a value the user set.
// Runs the real Program.Main in a child process so the native setenv never touches this test host.
public class WebKitEnvStartupTests
{
    private static string Run(string nvidiaProbe, string? userValue)
    {
        var dir = AppContext.BaseDirectory;
        var info = new ProcessStartInfo(Path.GetFullPath(Path.Combine(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../..", "dotnet")))
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = dir };
        // The test's deps/runtimeconfig: CCP.Avalonia's own deps.json points at the NuGet cache, the test output holds the copies.
        foreach (var a in new[] { "exec", "--depsfile", Path.Combine(dir, "CCP.Avalonia.Tests.deps.json"),
                     "--runtimeconfig", Path.Combine(dir, "CCP.Avalonia.Tests.runtimeconfig.json") })
            info.ArgumentList.Add(a);
        info.ArgumentList.Add(Path.Combine(dir, "CCP.Avalonia.dll"));
        info.ArgumentList.Add("--webkit-env-check");
        info.Environment["CCP_USERDATA_DIR"] = Path.Combine(Path.GetTempPath(), "ccp-webkit-env-" + Guid.NewGuid().ToString("N"));
        info.Environment["CCP_NVIDIA_PROBE"] = "/nonexistent " + nvidiaProbe;
        info.Environment.Remove("WEBKIT_DISABLE_DMABUF_RENDERER");
        if (userValue != null) info.Environment["WEBKIT_DISABLE_DMABUF_RENDERER"] = userValue;
        using var proc = Process.Start(info)!;
        var stderr = proc.StandardError.ReadToEndAsync();
        var stdout = proc.StandardOutput.ReadToEnd();
        Assert.True(proc.WaitForExit(30000), "child did not exit");
        Assert.True(proc.ExitCode == 0, $"exit {proc.ExitCode}: {stderr.Result}");
        return stdout.Trim();
    }

    [Theory]
    [InlineData(true, null, "=1")]
    [InlineData(false, null, "unset")]
    [InlineData(true, "0", "=0")]
    [InlineData(true, "", "=")]
    public void StartupSetsTheNativeValueOnlyOnNvidiaAndKeepsTheUsers(bool nvidia, string? user, string expected)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("WebKitGTK and the libc environment are Linux only.");
        var probe = Path.GetTempFileName();
        try
        {
            if (!nvidia) File.Delete(probe);
            Assert.Equal(expected, Run(probe, user));
        }
        finally { File.Delete(probe); }
    }
}
