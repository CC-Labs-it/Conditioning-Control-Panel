using System;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>Where the Llama Expo download stands on this PC.</summary>
internal enum TcExpoPhase { Absent, Fetching, Ready, Failed }

/// <summary>One reading of the Expo download: <paramref name="Pct"/> is what is on disk (0-100), kept
/// through a cancel or a failure; <paramref name="Reason"/> is set only when Failed.</summary>
internal sealed record TcExpoState(TcExpoPhase Phase, int Pct = 0, string? Reason = null)
{
    public static readonly TcExpoState Absent = new(TcExpoPhase.Absent);
}

/// <summary>
/// The Expo delivery as pure rules (Contract v2 TRANSMISSION: <c>expo-fetch</c>, <c>expo-cancel</c>,
/// <c>expo-progress</c>, <c>expo-ready</c>, <c>expo-failed</c>). The service that downloads, verifies and
/// unpacks asks these before every frame it sends. Rules: only a subscriber may start; a cancel keeps
/// what is on disk; a ready Expo stays ready; progress goes out at most twice a second.
/// </summary>
internal static class TcExpoRules
{
    public const string ReasonOffline = "offline";
    public const string ReasonDisk = "disk";
    public const string ReasonRefused = "refused";

    /// <summary>At most two <c>expo-progress</c> frames a second.</summary>
    public static readonly TimeSpan ProgressEvery = TimeSpan.FromMilliseconds(500);

    /// <summary>The reading at launch: a ready stamp (written last, after the sha check and the unpack)
    /// is the only proof of ready, so a crash mid-download or mid-unpack reads as absent with the
    /// partial bytes kept.</summary>
    public static TcExpoState FromDisk(bool readyStamp, long partialBytes, long totalBytes)
        => readyStamp ? new TcExpoState(TcExpoPhase.Ready, 100) : new TcExpoState(TcExpoPhase.Absent, Percent(partialBytes, totalBytes));

    /// <summary>Whole percent of a download, 0-100; 100 only when every byte is there.</summary>
    public static int Percent(long done, long total)
    {
        if (total <= 0 || done <= 0) return 0;
        if (done >= total) return 100;
        return (int)Math.Clamp(done * 100 / total, 0, 99);
    }

    /// <summary><c>expo-fetch</c> (YES on the transmission card). A non-subscriber or offline mode is
    /// refused; a ready or running download is left as it is.</summary>
    public static TcExpoState Start(TcExpoState s, bool subscriber, bool offlineMode)
    {
        if (s.Phase is TcExpoPhase.Ready or TcExpoPhase.Fetching) return s;
        if (!subscriber) return new TcExpoState(TcExpoPhase.Failed, s.Pct, ReasonRefused);
        if (offlineMode) return new TcExpoState(TcExpoPhase.Failed, s.Pct, ReasonOffline);
        return new TcExpoState(TcExpoPhase.Fetching, s.Pct);
    }

    /// <summary>Bytes arrived. Only a running download moves; a sha retry may restart from zero.</summary>
    public static TcExpoState Progress(TcExpoState s, int pct)
        => s.Phase == TcExpoPhase.Fetching ? s with { Pct = Math.Clamp(pct, 0, 99) } : s;

    /// <summary><c>expo-cancel</c>: back to absent, the partial bytes stay on disk.</summary>
    public static TcExpoState Cancel(TcExpoState s)
        => s.Phase == TcExpoPhase.Fetching ? new TcExpoState(TcExpoPhase.Absent, s.Pct) : s;

    /// <summary>The download stopped on an error; an unknown reason reads as refused.</summary>
    public static TcExpoState Fail(TcExpoState s, string? reason)
        => s.Phase == TcExpoPhase.Fetching ? new TcExpoState(TcExpoPhase.Failed, s.Pct, CleanReason(reason)) : s;

    /// <summary>Checked and unpacked, stamp written.</summary>
    public static TcExpoState Complete(TcExpoState s)
        => s.Phase == TcExpoPhase.Fetching ? new TcExpoState(TcExpoPhase.Ready, 100) : s;

    public static string CleanReason(string? reason) => reason switch
    {
        ReasonOffline => ReasonOffline,
        ReasonDisk => ReasonDisk,
        _ => ReasonRefused,
    };

    /// <summary>Send this progress reading now? Only a changed percent, and not within
    /// <see cref="ProgressEvery"/> of the last one.</summary>
    public static bool ShouldSendProgress(DateTime? lastSentUtc, int lastPct, DateTime nowUtc, int pct)
    {
        if (pct == lastPct) return false;
        return lastSentUtc is not { } last || nowUtc - last >= ProgressEvery;
    }

    /// <summary><c>init.caps.expo</c>: ready with its base, else absent with the size the card shows.</summary>
    public static JObject Cap(TcExpoState s, string expoBase, int? mb)
    {
        if (s.Phase == TcExpoPhase.Ready) return new JObject { ["state"] = "ready", ["base"] = expoBase };
        var cap = new JObject { ["state"] = "absent" };
        if (mb is > 0) cap["mb"] = mb.Value;
        return cap;
    }

    /// <summary>The host-to-page frame for a reading (null = nothing to say: absent, after a cancel).</summary>
    public static JObject? Frame(TcExpoState s, string expoBase) => s.Phase switch
    {
        TcExpoPhase.Fetching => new JObject { ["type"] = "expo-progress", ["pct"] = s.Pct },
        TcExpoPhase.Ready => new JObject { ["type"] = "expo-ready", ["base"] = expoBase },
        TcExpoPhase.Failed => new JObject { ["type"] = "expo-failed", ["reason"] = CleanReason(s.Reason) },
        _ => null,
    };
}
