using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF GoonHostService.cs:868-963: the page's disk-backed received-artifact protocol.
    private bool HandleGoonInbox(JObject o)
    {
        var type = (string?)o["type"] ?? "";
        if (type is not ("goon-recv-begin" or "goon-recv-chunk" or "goon-recv-commit" or "goon-recv-abort" or "goon-recv-drop")) return false;
        var id = (string?)o["id"] ?? "";
        var store = TransferInboxStore.Instance;
        void Reply(bool ok, string? url = null, long bytes = 0, string? error = null) =>
            Post(new { type = "goon-recv-result", id, ok, url, bytes, error });
        try
        {
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
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var r = store.Commit(id);
                            long bytes = 0;
                            if (r.Ok) { try { bytes = new FileInfo(Path.Combine(store.RecvDir, r.Sha + "." + r.Ext)).Length; } catch { } }
                            Dispatcher.UIThread.Post(() => { if (!_closing) Reply(r.Ok, r.Url, bytes, r.Error); });
                        }
                        catch { Dispatcher.UIThread.Post(() => { if (!_closing) Reply(false, error: "io-failed"); }); }
                    });
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
        return true;
    }
}
