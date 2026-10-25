using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF LoomHostService's bridge subset. GameWindow supplies singleton focus, origin fencing
    // and the existing PanicSurfaces "games" registration; the Core store owns all file writes.
    private void HandleLoom(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "ready":
                IsReady = true;
                PostLoomList();
                break;
            case "loom-save":
                var (ok, slug, error) = DtrhLoomStore.Save((string?)o["name"],
                    (string?)o["gifBase64"], o["params"], (bool?)o["overwrite"] ?? false);
                Post(new { type = "loom-result", op = "save", ok, slug, error });
                if (ok) PostLoomList();
                break;
            case "loom-delete":
                var id = (string?)o["slug"];
                var result = DtrhLoomStore.Delete(id);
                Post(new { type = "loom-result", op = "delete", ok = result.Ok, slug = id, error = result.Error });
                if (result.Ok) PostLoomList();
                break;
            case "loom-reveal":
                var path = DtrhLoomStore.GifPathFor((string?)o["slug"]);
                if (path != null) _ = RevealLoom(path);
                break;
            case "sfx":
                var name = (string?)o["name"];
                if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\', '.' }) >= 0) break;
                var cue = ContentLocator.Resolve(Path.Combine("Resources", "sounds", "chaos", name + ".mp3"));
                // ponytail: stock cue resolution, as in ChaosOverlayWindow; mod audio override is unported.
                if (File.Exists(cue)) CoreAudio.PlayOneShot(cue,
                    Math.Clamp(CoreSettings.Current.MasterVolume / 100f * ((float?)o["scale"] ?? 0.45f), 0, 1), "chaos-sfx");
                break;
        }
    }

    private async System.Threading.Tasks.Task RevealLoom(string path)
    {
        try
        {
            // ponytail: Avalonia's portable launcher opens the containing folder; it cannot select a file.
            await Platform.ExternalOpener.OpenAsync(this, Path.GetDirectoryName(path));
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Loom: reveal failed"); }
    }

    private void PostLoomList() => Post(new
    {
        type = "loom-list",
        spirals = DtrhLoomStore.List().Select(s => new
        {
            slug = s.Slug,
            url = _server?.Url("ccp.spirals/loom_" + s.Slug + ".gif"),
            @params = ParseLoomParams(s.ParamsJson),
        }),
    });

    private static JObject? ParseLoomParams(string? json)
    {
        try { return json == null ? null : JObject.Parse(json); }
        catch { return null; }
    }
}
