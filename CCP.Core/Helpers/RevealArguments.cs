using System;
using Serilog;

namespace ConditioningControlPanel.Helpers;

/// <summary>
/// The command lines that show a file SELECTED in the OS file manager. Pure strings, one copy for both
/// heads: WPF's ExplorerLauncher launches the Explorer pair, the Avalonia head's ExternalOpener.Reveal
/// picks the one for the OS it runs on.
///
/// <para>Explorer (moved from WPF ExplorerLauncher, ccp-bugs #998 / #1108): the command line is built by
/// hand. ProcessStartInfo.ArgumentList wraps a token with whitespace in quotes, and it wraps the WHOLE
/// token, so <c>/select,D:\Conditioning Control Panel\...</c> came out with the switch trapped inside the
/// quotes; Explorer read that as one nonsense location and fell back to Documents. The switch stays
/// OUTSIDE the quotes and only the path is quoted. Windows filenames cannot contain a double quote, so a
/// path carrying one is refused rather than escaped - escaping could only ever point somewhere else.</para>
/// </summary>
public static class RevealArguments
{
    /// <summary>Explorer arguments that open <paramref name="fullPath"/>'s folder with the file selected,
    /// or null when the path cannot be expressed safely on a command line.</summary>
    public static string? ExplorerSelect(string fullPath) =>
        IsQuotable(fullPath) ? $"/select,\"{fullPath}\"" : null;

    /// <summary>Explorer arguments that open the folder <paramref name="fullPath"/>, or null as above.
    /// No switch here, so the quotes are only there for the spaces.</summary>
    public static string? ExplorerFolder(string fullPath) =>
        IsQuotable(fullPath) ? $"\"{fullPath}\"" : null;

    /// <summary>
    /// <c>dbus-send</c> arguments for the freedesktop FileManager1.ShowItems call (Nautilus, Dolphin, Nemo,
    /// Caja, Thunar...), the Linux spelling of Explorer's <c>/select</c>. <c>--print-reply</c> makes a
    /// desktop without that service exit non-zero, so the caller can fall back to opening the folder.
    /// The file goes as a URI; dbus-send splits <c>array:string:</c> on commas, so those are escaped too.
    /// </summary>
    public static string[] FreedesktopShowItems(string fullPath) => new[]
    {
        "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
        "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems",
        // Built per segment, not with new Uri(path): that throws on Windows for a rooted Unix path.
        "array:string:file://" + string.Join('/', fullPath.Split('/').Select(Uri.EscapeDataString)),
        "string:",
    };

    private static bool IsQuotable(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        if (fullPath.IndexOf('"') < 0) return true;
        Log.Warning("[Reveal] refusing a path containing a quote");
        return false;
    }
}
