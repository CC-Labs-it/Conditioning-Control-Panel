using System;
using System.Net.Http;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Race;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>B2 step 1. The first-race card will own progress and its Cancel button.</summary>
internal static class RaceEngineDownload
{
    internal static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
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
            try { return await fetch(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!linked.IsCancellationRequested) { throw new TimeoutException(); }
        }
        finally { lock (Sync) active = null; }
    }

    internal static void Cancel()
    {
        lock (Sync) active?.Cancel();
    }
}

internal sealed class RaceEngineFetchAdapter : IRaceEngineFetch
{
    private readonly Func<IProgress<RaceEngineProgress>, CancellationToken, Task<string>> fetch;
    private readonly Func<bool> installed;
    internal RaceEngineFetchAdapter() : this(RaceEngineDownload.FetchAsync, () =>
        new RaceEngineFetcher(RaceEngineDownload.Http, ConditioningControlPanel.CorePaths.UserData)
            .IsInstalled(RaceEngineManifest.For(RuntimeInformation.ProcessArchitecture))) { }
    internal RaceEngineFetchAdapter(Func<IProgress<RaceEngineProgress>, CancellationToken, Task<string>> fetch, Func<bool> installed)
    { this.fetch = fetch; this.installed = installed; }
    public bool IsInstalled => installed();
    public async Task<RaceEngineFetchResult> FetchAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            await fetch(new EngineProgress(progress), cancellationToken).ConfigureAwait(false);
            return RaceEngineFetchResult.Installed;
        }
        catch (PlatformNotSupportedException) { return RaceEngineFetchResult.Unsupported; }
        catch (HttpRequestException) { return RaceEngineFetchResult.Network; }
        catch (TimeoutException) { return RaceEngineFetchResult.Network; }
        catch (InvalidDataException) { return RaceEngineFetchResult.Checksum; }
        catch (IOException) { return RaceEngineFetchResult.Storage; }
    }
    private sealed class EngineProgress(IProgress<double> progress) : IProgress<RaceEngineProgress>
    {
        public void Report(RaceEngineProgress value) => progress.Report(value.Total is > 0
            ? Math.Clamp(100.0 * value.Received / value.Total.Value, 0, 100) : double.NaN);
    }
    internal static IRaceEngineFetch? Seed(bool linux, bool shell) => linux && shell ? new RaceEngineFetchAdapter() : null;
}
