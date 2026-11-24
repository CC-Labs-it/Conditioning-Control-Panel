using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Chaos;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // All windows share the store. Queue its work in order so a late close purge cannot erase a
    // new session's media, and no disk operation can hold the UI or the panic stop pass.
    private static readonly object GoonInboxGate = new();
    private static Task _goonInboxTail = Task.CompletedTask;
    private int _goonInboxGeneration;
    internal static Action? GoonInboxBeforeCommit; // test seam for the close/commit race
    internal static Task GoonInboxIdle { get { lock (GoonInboxGate) return _goonInboxTail; } }

    private static void QueueGoonInbox(Action action)
    {
        lock (GoonInboxGate)
            _goonInboxTail = _goonInboxTail.ContinueWith(_ => action(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void BootGoonInbox()
    {
        var generation = Interlocked.Increment(ref _goonInboxGeneration);
        var server = _server;
        var root = CorePaths.EffectiveAssets;
        var settings = CoreSettings.Current;
        var disabled = settings.DisabledAssetPaths?.ToArray();
        QueueGoonInbox(() =>
        {
            var store = TransferInboxStore.Instance;
            store.PurgeCommittedSafe("page boot");
            IReadOnlyList<object> received;
            try { received = store.ListForManifest(); } catch { received = Array.Empty<object>(); }
            // The walk can be slow; do not hold the inbox queue or the UI while building it.
            _ = Task.Run(() =>
            {
                GameMediaManifest.Manifest media;
                try
                {
                    media = server == null ? new() : GameMediaManifest.Build(root, server.AssetUrl, disabled);
                    if (server != null) DtrhRemoteMedia.AppendTo(media, settings,
                        copy => server.AssetUrl(Path.GetRelativePath(root, copy)), DtrhCopier);
                }
                catch { media = new(); }
                Dispatcher.UIThread.Post(() =>
                {
                    if (_closing || generation != Volatile.Read(ref _goonInboxGeneration)) return;
                    Post(new { type = "manifest", images = media.Images.Select(e => new { name = e.Name, url = e.Url }),
                        videos = media.Videos.Select(e => new { name = e.Name, url = e.Url }),
                        skipped = media.Skipped, truncated = media.Truncated, received });
                });
            });
        });
    }

    private void CloseGoonInbox()
    {
        Interlocked.Increment(ref _goonInboxGeneration);
        QueueGoonInbox(() => TransferInboxStore.Instance.PurgeCommittedSafe("window closed"));
    }

    // WPF purges synchronously on close (GoonHostService.cs:2122); the pool dies with the process,
    // so exit cancels every live page's queued work, purges once more and waits for the queue.
    internal static bool DrainGoonInboxForExit(TimeSpan max)
    {
        GameWindow[] all;
        lock (Open) all = Open.ToArray();
        foreach (var w in all) if (w.Spec.Id == "goon") Interlocked.Increment(ref w._goonInboxGeneration);
        QueueGoonInbox(() => TransferInboxStore.Instance.PurgeCommittedSafe("app exit"));
        try { return GoonInboxIdle.Wait(max); } catch { return false; }
    }

    // WPF GoonHostService.cs:868-963: the page's disk-backed received-artifact protocol.
    private bool HandleGoonInbox(JObject o)
    {
        var type = (string?)o["type"] ?? "";
        if (type is not ("goon-recv-begin" or "goon-recv-chunk" or "goon-recv-commit" or "goon-recv-abort" or "goon-recv-drop")) return false;
        if (_closing) return true;
        var generation = Volatile.Read(ref _goonInboxGeneration);
        QueueGoonInbox(() =>
        {
            if (generation != Volatile.Read(ref _goonInboxGeneration))
            {
                // A cancelled chunk, commit or abort may own a partial; dispose it before a new
                // session can resume it from the wrong offset (WPF always runs abort, :918-921).
                if (type is "goon-recv-chunk" or "goon-recv-commit" or "goon-recv-abort")
                    try { TransferInboxStore.Instance.Abort((string?)o["id"]); } catch { }
                return;
            }
            var id = "";
            var store = TransferInboxStore.Instance;
            void Reply(bool ok, string? url = null, long bytes = 0, string? error = null) =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_closing && generation == Volatile.Read(ref _goonInboxGeneration))
                        Post(new { type = "goon-recv-result", id, ok, url, bytes, error });
                });
            try
            {
                id = (string?)o["id"] ?? "";
                switch (type)
                {
                    case "goon-recv-begin":
                        var begin = store.Begin(id, (string?)o["sha256"], (string?)o["mime"], (long?)o["bytes"] ?? 0, (string?)o["origin"]);
                        Reply(begin == null, error: begin);
                        break;
                    case "goon-recv-chunk":
                        var chunk = store.AppendChunk(id, (int?)o["seq"] ?? -1, (string?)o["b64"]);
                        Reply(chunk == null, error: chunk);
                        break;
                    case "goon-recv-commit":
                        GoonInboxBeforeCommit?.Invoke();
                        var r = store.Commit(id);
                        long bytes = 0;
                        if (r.Ok) { try { bytes = new FileInfo(Path.Combine(store.RecvDir, r.Sha + "." + r.Ext)).Length; } catch { } }
                        Reply(r.Ok, r.Ok ? "/__goon_recv__/" + r.Sha + "." + r.Ext : null, bytes, r.Error);
                        break;
                    case "goon-recv-abort":
                        store.Abort(id);
                        Reply(true);
                        break;
                    case "goon-recv-drop":
                        var sha = (string?)o["sha256"];
                        if (id.Length == 0) id = sha ?? "";
                        var ok = store.Drop(sha);
                        Reply(ok, error: ok ? null : "bad-name");
                        break;
                }
            }
            catch { Reply(false, error: "io-failed"); }
        });
        return true;
    }
}
