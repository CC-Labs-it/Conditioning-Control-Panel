using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Scrolller licence on this head (WPF App.xaml.cs:2567-2576 + Services/Fyp/Online/ScrolllerCopySweeper.cs), the
/// same on Windows and Linux: the server switch (GET /config/scrolller) read at launch and every 30 minutes, the device
/// cache's books loaded in the background, and the hourly sweep that keeps every Scrolller copy this head holds under the
/// 24 h day limit. The off switch DELETES every copy (Core <see cref="ScrolllerPolicyService.Accept"/> calls
/// <see cref="PurgeAll"/>). The copies on this head: the device cache, DtRH's <c>&lt;assets&gt;/.temp/ccp_temp_remote_*</c>
/// copies, and any other <c>ccp_temp_remote_*</c> file in the temp folders. Each step is guarded on its own.
/// <para>Not here (WPF has them): the away watcher (no fresh fetch while the screen is locked), the wallpaper and
/// the in-memory RemoteMediaCache (this head has neither surface).</para>
/// </summary>
internal static class ScrolllerHead
{
    private static int _started;

    /// <summary>Points the Core device cache's sweep and purge at this head's copies. Idempotent.</summary>
    internal static void SeedHooks()
    {
        ScrolllerDiskCache.SweepAllCopies = SweepAll;
        ScrolllerDiskCache.PurgeAllCopies = PurgeAll;
    }

    /// <summary>Launch wiring. Call after the app version is seeded (the honest user-agent carries it). Never throws.</summary>
    public static void Start()
    {
        if (System.Threading.Interlocked.Exchange(ref _started, 1) != 0) return;
        SeedHooks();
        Step(ScrolllerPolicyService.Start);
        Step(() => _ = ScrolllerDiskCache.WarmAsync());
        Step(ScrolllerDiskCache.StartSweeper);   // first pass once the books are loaded, then hourly
    }

    /// <summary>App exit: the share's ledger and the cache index have debounced writes (WPF App.OnExit:6284-6285).</summary>
    public static void Stop()
    {
        Step(ScrolllerAllowance.Flush);
        Step(ScrolllerDiskCache.Flush);
    }

    /// <summary>Folders that can hold a <c>ccp_temp_remote_</c> copy: the library's .temp (DtRH), the first-run
    /// show's own folder, and the system temp folder. Replaceable by tests.</summary>
    internal static Func<IEnumerable<string?>> TempDirs = () => new[]
    {
        SafePath(() => Path.Combine(CorePaths.EffectiveAssets, ".temp")),
        SafePath(() => Views.Windows.WelcomeShow.FirstShowStageWindow.TempDir),
        SafePath(Path.GetTempPath),
    };

    /// <summary>The hourly pass (WPF ScrolllerCopySweeper.SweepAll). Never throws.</summary>
    public static void SweepAll()
    {
        Step(() => ScrolllerDiskCache.SweepExpired());
        Step(() => DtrhRemoteMedia.SweepLocalCopies(DtrhRemoteMedia.AssetsTempCopier.Release));
        // One period ahead, as the device cache: nothing waits past the day for the next sweep.
        Step(() => ScrolllerDiskCache.SweepTempCopies(TempDirs(), ScrolllerDiskCache.TempPrefix,
            ScrolllerDiskCache.MaxAge - ScrolllerDiskCache.SweepPeriod));
        Step(() => LegacyWebViewCaches.SweepAll());
    }

    /// <summary>The off switch: every copy goes now (WPF ScrolllerCopySweeper.PurgeAll). Idempotent, never throws.</summary>
    public static void PurgeAll(string reason)
    {
        Step(() => ScrolllerDiskCache.PurgeAll(reason));
        Step(() => DtrhRemoteMedia.SweepLocalCopies(DtrhRemoteMedia.AssetsTempCopier.Release, all: true));
        Step(() => ScrolllerDiskCache.SweepTempCopies(TempDirs(), ScrolllerDiskCache.TempPrefix, TimeSpan.Zero));
        Step(() => LegacyWebViewCaches.SweepAll());
    }

    private static string? SafePath(Func<string> path)
    {
        try { return path(); }
        catch { return null; }
    }

    private static void Step(Action step)
    {
        try { step(); }
        catch (Exception ex) { Log.Debug("[scrolller sweep] step failed: {E}", ex.Message); }
    }
}
