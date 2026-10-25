using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // Adapted from the pinned WPF LoomHostService; GameWindow owns singleton/fence/panic lifetime.
    private void HandleLoom(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "ready":
                IsReady = true;
                PostLoomList();
                break;
            case "loom-save":
                var (ok, slug, error) = DtrhLoomStore.Save((string?)o["name"], (string?)o["gifBase64"],
                    o["params"], (bool?)o["overwrite"] ?? false);
                Post(new { type = "loom-result", op = "save", ok, slug, error });
                if (ok) PostLoomList();
                break;
            case "loom-delete":
                var deleted = (string?)o["slug"];
                var result = DtrhLoomStore.Delete(deleted);
                Post(new { type = "loom-result", op = "delete", ok = result.Ok, slug = deleted, error = result.Error });
                if (result.Ok) PostLoomList();
                break;
            case "loom-reveal":
                // ponytail: shared platform launcher opens the GIF; Explorer selection remains a parity gap.
                var path = DtrhLoomStore.GifPathFor((string?)o["slug"]);
                if (path != null) _ = ExternalOpener.OpenAsync(this, path);
                break;
            case "sfx":
                var name = (string?)o["name"];
                if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\', '.' }) >= 0) break;
                var rel = $"chaos/{name}.mp3";
                var cue = CoreModArt.AudioOverridePath(rel) ?? Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", rel);
                if (File.Exists(cue)) CoreAudio.PlayOneShot(cue,
                    Math.Clamp(CoreSettings.Current.MasterVolume / 100f * ((float?)o["scale"] ?? .45f), 0, 1), "chaos-sfx");
                break;
        }
    }

    private void PostLoomList() => Post(new
    {
        type = "loom-list",
        spirals = DtrhLoomStore.List().Select(s => new
        {
            slug = s.Slug,
            url = _server!.Url($"ccp.spirals/loom_{s.Slug}.gif"),
            @params = ParseLoomParams(s.ParamsJson),
        }),
    });

    private static JObject? ParseLoomParams(string? json)
    {
        try { return json == null ? null : JObject.Parse(json); }
        catch { return null; }
    }
}
