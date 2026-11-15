using System;
using System.IO;
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
        => Assert.Equal(expected, await Run(script));

    [Fact]
    public async Task RefusesFailedLicenceCheck()
        => await Assert.ThrowsAsync<IOException>(() => Run("echo missing >&2; exit 1"));

    [Fact]
    public async Task KillsHungCheckAtDeadline()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run("sleep 30"));
        Assert.InRange(watch.Elapsed.TotalSeconds, 3, 8);
    }

    [Fact]
    public async Task CancellationStopsCheck()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run("sleep 30", cancel.Token));
    }
}
