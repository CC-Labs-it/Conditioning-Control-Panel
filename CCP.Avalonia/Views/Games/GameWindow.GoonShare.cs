using System;
using System.IO;
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
        Interlocked.Increment(ref _goonInboxGeneration);
        QueueGoonInbox(() => TransferInboxStore.Instance.PurgeCommittedSafe("page boot"));
    }

    private void CloseGoonInbox()
    {
        Interlocked.Increment(ref _goonInboxGeneration);
        QueueGoonInbox(() => TransferInboxStore.Instance.PurgeCommittedSafe("window closed"));
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
                // A cancelled commit may already own a partial; dispose it before a new session starts.
                if (type == "goon-recv-commit")
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
