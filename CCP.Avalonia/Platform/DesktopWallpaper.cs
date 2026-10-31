using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ConditioningControlPanel.Services;
namespace ConditioningControlPanel.Avalonia.Platform;
public sealed class LinuxWallpaperPlatform : IWallpaperPlatform
{
    private readonly Func<string, string[], string?> _run;
    private readonly string _desktop;
    private string? _dbus;
    public LinuxWallpaperPlatform(Func<string, string[], string?>? run = null, string? desktop = null)
    {
        _run = run ?? Run;
        _desktop = desktop ?? Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
    }
    private sealed record Snapshot(string Backend, Dictionary<string, string> Values);
    private static readonly string[] GnomeKeys = ["picture-uri", "picture-uri-dark", "picture-options"];
    private const string Schema = "org.gnome.desktop.background";
    public string? Capture()
    {
        if (_desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase))
        {
            var values = new Dictionary<string, string>();
            foreach (var key in GnomeKeys)
            {
                var value = _run("gsettings", ["get", Schema, key]);
                if (value == null) return null;
                values[key] = value.Trim();
            }
            if (values["picture-options"] == "'none'") return null;
            if (!LocalImage(values["picture-uri"].Trim('\'')) || !LocalImage(values["picture-uri-dark"].Trim('\''))) return null;
            return JsonSerializer.Serialize(new Snapshot("gnome", values));
        }
        if (_desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var executable in new[] { "qdbus6", "qdbus" })
            {
                var result = _run(executable, ["org.kde.plasmashell", "/PlasmaShell", "org.kde.PlasmaShell.evaluateScript",
                    "print(JSON.stringify(desktops().map(d => { d.currentConfigGroup = ['Wallpaper', 'org.kde.image', 'General']; return [String(d.id), d.wallpaperPlugin === 'org.kde.image' ? d.readConfig('Image', '') : '']; })));"]);
                if (result == null) continue;
                var entries = JsonSerializer.Deserialize<string[][]>(result);
                if (entries == null || entries.Length == 0 || entries.Any(e => e.Length != 2 || !LocalImage(e[1]))) return null;
                _dbus = executable;
                return JsonSerializer.Serialize(new Snapshot("kde", entries.ToDictionary(e => e[0], e => e[1])));
            }
        }
        return null;
    }
    public bool Apply(string path)
    {
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        if (_desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase))
            return KdeWrite(null, uri);
        return GnomeWrite(new Dictionary<string, string> { ["picture-uri"] = Quote(uri), ["picture-uri-dark"] = Quote(uri), ["picture-options"] = "'zoom'" });
    }
    public bool Restore(string snapshot)
    {
        var state = JsonSerializer.Deserialize<Snapshot>(snapshot);
        if (state == null) return false;
        if (state.Backend == "gnome") return GnomeWrite(state.Values);
        if (state.Backend != "kde") return false;
        var success = true;
        foreach (var entry in state.Values) success &= KdeWrite(entry.Key, entry.Value);
        return success;
    }
    private bool GnomeWrite(Dictionary<string, string> values)
    {
        var success = true;
        foreach (var entry in values) success &= _run("gsettings", ["set", Schema, entry.Key, entry.Value]) != null;
        return success;
    }
    private bool KdeWrite(string? id, string uri)
    {
        var script = "desktops().forEach(d => { " + (id == null ? "" : $"if (String(d.id) !== {JsonSerializer.Serialize(id)}) return; ") +
            "d.wallpaperPlugin = 'org.kde.image'; d.currentConfigGroup = ['Wallpaper', 'org.kde.image', 'General']; d.writeConfig('Image', " + JsonSerializer.Serialize(uri) + "); });";
        foreach (var executable in _dbus == null ? new[] { "qdbus6", "qdbus" } : new[] { _dbus })
            if (_run(executable, ["org.kde.plasmashell", "/PlasmaShell", "org.kde.PlasmaShell.evaluateScript", script]) != null) return true;
        return false;
    }
    private static bool LocalImage(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile && File.Exists(uri.LocalPath);
    private static string Quote(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    private static string? Run(string executable, string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000)) { process.Kill(entireProcessTree: true); return null; }
            return process.ExitCode == 0 ? output.GetAwaiter().GetResult().Trim() : null;
        }
        catch { return null; }
    }
}
public sealed class WindowsWallpaperPlatform : IWallpaperPlatform
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int SystemParametersInfo(int action, int parameter, StringBuilder value, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int SystemParametersInfo(int action, int parameter, string value, int flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SendNotifyMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public string? Capture()
    {
        var buffer = new StringBuilder(4096);
        return SystemParametersInfo(0x73, buffer.Capacity, buffer, 0) != 0 && File.Exists(buffer.ToString()) ? buffer.ToString() : null;
    }
    public bool Apply(string path)
    {
        if (SystemParametersInfo(0x14, 0, path, 1) == 0) return false;
        SendNotifyMessage(new IntPtr(0xffff), 0x1a, new IntPtr(0x14), IntPtr.Zero);
        return true;
    }
    public bool Restore(string snapshot) => File.Exists(snapshot) && Apply(snapshot);
}
internal static class WallpaperHead
{
    internal static WallpaperHost? Service;
    internal static void Initialize()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"))) return;
        Service = new WallpaperHost(OperatingSystem.IsWindows() ? new WindowsWallpaperPlatform() : new LinuxWallpaperPlatform());
        _ = Service.RestoreStaleAsync();
    }
}
