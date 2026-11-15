using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LinuxRaceHostSandboxProbeTests
{
    private static async Task<string?> Run(string body, CancellationToken token = default)
    {
        var script = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(script, body);
            return await LinuxRaceHost.ProbeEngineAsync(script, "/tmp", token);
        }
        finally { File.Delete(script); }
    }

    [Theory]
    [InlineData("exit 0", null)]
    [InlineData("echo --no-sandbox", "--no-sandbox")]
    public async Task ReturnsOnlyTheDocumentedArgument(string script, string? expected)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Race engine helper runs on Linux only.");
        Assert.Equal(expected, await Run(script));
    }

    [Fact]
    public async Task RefusesFailedLicenceCheck()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Race engine helper runs on Linux only.");
        await Assert.ThrowsAsync<IOException>(() => Run("echo missing >&2; exit 1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopsHelperAndChildOnDeadlineOrCancellation(bool cancelEarly)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Race engine helper runs on Linux only.");
        var directory = Path.Combine(Path.GetTempPath(), "ccp-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "check.sh");
        var pidsFile = Path.Combine(directory, "pids");
        int helper = 0, child = 0;
        Task<string?>? probe = null;
        using var cancel = new CancellationTokenSource();
        try
        {
            await File.WriteAllTextAsync(script, "sleep 30 &\nchild=$!\nprintf '%s %s\\n' \"$$\" \"$child\" > \"$1/pids.tmp\"\nmv \"$1/pids.tmp\" \"$1/pids\"\nwait \"$child\"\n");
            var watch = Stopwatch.StartNew();
            probe = LinuxRaceHost.ProbeEngineAsync(script, directory, cancel.Token);
            while (!File.Exists(pidsFile) && watch.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
            Assert.True(File.Exists(pidsFile), "Helper did not report its processes");
            var pids = (await File.ReadAllTextAsync(pidsFile)).Trim().Split(' ');
            helper = int.Parse(pids[0]); child = int.Parse(pids[1]);
            Assert.True(IsRunning(helper));
            Assert.True(IsRunning(child));
            if (cancelEarly) cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
            if (!cancelEarly) Assert.InRange(watch.Elapsed.TotalSeconds, 3, 8);
            var stopped = Stopwatch.StartNew();
            while ((IsRunning(helper) || IsRunning(child)) && stopped.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
            Assert.False(IsRunning(helper), "Helper survived the probe");
            Assert.False(IsRunning(child), "Helper child survived the probe");
        }
        finally
        {
            cancel.Cancel();
            if (probe != null) { try { await probe; } catch (Exception) { } }
            foreach (var pid in new[] { child, helper })
            {
                if (pid == 0) continue;
                try { using var process = Process.GetProcessById(pid); process.Kill(entireProcessTree: true); }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    // A zombie has exited even if the container's init has not reaped it yet.
    private static bool IsRunning(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[stat.LastIndexOf(')') + 2] != 'Z';
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
