using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Race;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>B2 step 1. The first-race card will own progress and its Cancel button.</summary>
internal static class RaceEngineDownload
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly object Sync = new();
    private static CancellationTokenSource? active;

    internal static Task<string> FetchAsync(IProgress<RaceEngineProgress>? progress, CancellationToken cancel)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        return RunAsync(token => new RaceEngineFetcher(Http, ConditioningControlPanel.CorePaths.UserData)
            .FetchAsync(RaceEngineManifest.For(RuntimeInformation.ProcessArchitecture), progress, token), cancel);
    }

    internal static async Task<string> RunAsync(Func<CancellationToken, Task<string>> fetch, CancellationToken cancel)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        lock (Sync)
        {
            if (active != null) throw new InvalidOperationException("Race engine download already active.");
            active = linked;
        }
        try
        {
            return await fetch(linked.Token).ConfigureAwait(false);
        }
        finally { lock (Sync) active = null; }
    }

    internal static void Cancel()
    {
        lock (Sync) active?.Cancel();
    }
}
