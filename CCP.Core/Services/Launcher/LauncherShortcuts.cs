using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// Desktop shortcuts for the launcher's tiles. A game shortcut carries <c>--game &lt;id&gt;</c>
/// so the boot decision lands in the game; the panel shortcut carries <c>--panel</c>. This is the
/// pure half, shared by both heads; each head writes the file (WPF <c>LauncherShortcutWriter</c>
/// through <c>StartupManager</c>'s COM writer, the Avalonia head a .lnk on Windows and a
/// desktop entry on Linux).
/// </summary>
public static class LauncherShortcuts
{
    /// <summary>The id the panel answers to (WPF <c>LauncherCatalogue.PanelId</c>).</summary>
    public const string PanelId = "panel";

    public const string PanelFileName = "Conditioning Control Panel.lnk";
    private const string GamePrefix = "CC Labs - ";

    /// <summary>Windows' <c>Path.GetInvalidFileNameChars</c>, fixed: on Linux that list is two
    /// characters long, and a shortcut's name must not depend on the machine that wrote it.</summary>
    private static bool IsUnsafe(char c) => c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0;

    private static bool IsPanel(string? gameId) =>
        string.IsNullOrWhiteSpace(gameId) ||
        string.Equals(gameId.Trim(), PanelId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The pure half: file name and arguments for a tile. Null or "panel" means the panel.
    /// A title that is blank or unsafe on disk falls back to the id; characters a file name
    /// cannot carry are dropped rather than replaced, so the name stays readable.</summary>
    public static (string FileName, string Arguments) Describe(string? gameId, string? title)
    {
        if (IsPanel(gameId)) return (PanelFileName, "--panel");
        var id = gameId!.Trim();
        return (GamePrefix + SafeTitle(id, title) + ".lnk", "--game " + id);
    }

    private static string SafeTitle(string id, string? title)
    {
        var safe = new string((title ?? "").Where(c => !IsUnsafe(c)).ToArray()).Trim();
        return safe.Length == 0 ? id : safe;
    }

    /// <summary>Folder under the install directory that holds one <c>&lt;game id&gt;.ico</c> per tile.
    /// A shortcut cannot read an embedded resource, so the icons ship as plain files.</summary>
    public static readonly string IconFolder = Path.Combine("Resources", "launcher-icons");

    /// <summary>The pure half of the icon choice: the file name a game's shortcut wears, or null for the
    /// panel (it keeps the exe's own icon) and for an id that cannot name a file.</summary>
    public static string? IconFileName(string? gameId)
    {
        if (IsPanel(gameId)) return null;
        var id = gameId!.Trim();
        return id.Any(IsUnsafe) ? null : id + ".ico";
    }

    /// <summary>The icon beside the executable, or null when it did not ship.</summary>
    public static string? ResolveIcon(string? gameId, string exeDirectory)
    {
        var name = IconFileName(gameId);
        if (name == null) return null;
        var path = Path.Combine(exeDirectory, IconFolder, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The Linux twin of <see cref="Describe"/>: a freedesktop desktop entry. The file name is the
    /// stable id (<c>cclabs-backroom.desktop</c>), Name is the name the .lnk would have, and Exec
    /// is the head's quoted command plus the same arguments the .lnk carries.</summary>
    public static (string FileName, string Content) DescribeDesktopEntry(string? gameId, string? title,
        string execLine, string? iconPath)
    {
        var (lnk, arguments) = Describe(gameId, title);
        var (id, name) = (IsPanel(gameId) ? PanelId : gameId!.Trim(), Path.GetFileNameWithoutExtension(lnk));
        var fileId = new string(id.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (fileId.Length == 0) fileId = PanelId;

        var sb = new StringBuilder("[Desktop Entry]\nType=Application\n");
        sb.Append("Name=").Append(OneLine(name)).Append('\n');
        sb.Append("Exec=").Append(execLine).Append(' ').Append(arguments).Append('\n');
        if (!string.IsNullOrEmpty(iconPath)) sb.Append("Icon=").Append(OneLine(iconPath)).Append('\n');
        sb.Append("Terminal=false\nCategories=Game;\n");
        return ("cclabs-" + fileId.ToLowerInvariant() + ".desktop", sb.ToString());
    }

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ");
}
