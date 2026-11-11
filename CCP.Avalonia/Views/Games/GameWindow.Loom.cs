// Adapted from parity/trunk 7c193a5cb2a0b4d8a43978ef1f91837d8dd5b7f0 and WPF LoomHostService.
using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF Services/Chaos/LoomHostService.cs:58-59: keep Loom browser state separate from games.
    private static void ConfigureLoomEnvironment(WebViewEnvironmentRequestedEventArgs e)
    {
        var folder = Path.Combine(CorePaths.UserData, "browser_data_loom");
        Directory.CreateDirectory(folder);
        switch (e)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs win:
                win.UserDataFolder = folder;
                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                gtk.BaseDataDirectory = folder;
                gtk.BaseCacheDirectory = Path.Combine(folder, "cache");
                break;
            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                wpe.DataDirectory = folder;
                wpe.CacheDirectory = Path.Combine(folder, "cache");
                break;
        }
    }

    private static void DirectoryForLoom()
    {
        Directory.CreateDirectory(DtrhLoomStore.SpiralsFolder);
        ConditioningControlPanel.Services.EmiDesk.EmiState.NoteUsage("loom");
    }

    // The shared GameWindow owns focus/reopen and panic (surface "games"). Ready sends no shell init.
    private void HandleLoom(JObject message)
    {
        switch ((string?)message["type"])
        {
            case "ready":
                IsReady = true;
                PostLoomList();
                break;
            case "loom-save":
                var (saved, slug, saveError) = DtrhLoomStore.Save((string?)message["name"],
                    (string?)message["gifBase64"], message["params"], (bool?)message["overwrite"] ?? false);
                Post(new { type = "loom-result", op = "save", ok = saved, slug, error = saveError });
                if (saved) PostLoomList();
                break;
            case "loom-delete":
                var name = (string?)message["slug"];
                var (deleted, deleteError) = DtrhLoomStore.Delete(name);
                Post(new { type = "loom-result", op = "delete", ok = deleted, slug = name, error = deleteError });
                if (deleted) PostLoomList();
                break;
            case "loom-reveal":
                var path = DtrhLoomStore.GifPathFor((string?)message["slug"]);
                // WPF Services/Chaos/LoomHostService.cs:111-119: reveal the store-validated file.
                if (path != null) ExternalOpener.Reveal(path);
                break;
            case "sfx":
                var cue = (string?)message["name"];
                if (string.IsNullOrWhiteSpace(cue) || cue.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) break;
                var relative = $"chaos/{cue}.mp3";
                var audio = CoreModArt.AudioOverridePath(relative)
                    ?? Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", relative);
                if (File.Exists(audio)) CoreAudio.PlayOneShot(audio,
                    Math.Clamp(CoreSettings.Current.MasterVolume / 100f * ((float?)message["scale"] ?? 0.45f), 0, 1), "chaos-sfx");
                break;
            case "boot-error":
                Log.Warning("LoomHost: page failed to boot");
                Close();
                break;
        }
    }

    private void PostLoomList() => Post(new
    {
        type = "loom-list",
        spirals = DtrhLoomStore.List().Select(s => new
        {
            slug = s.Slug,
            url = _server!.Url("__spirals__/loom_" + s.Slug + ".gif"),
            @params = LoomParams(s.ParamsJson),
        }),
    });

    private static JObject? LoomParams(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json); }
        catch { return null; }
    }
}
