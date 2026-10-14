using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The shortcut rules as they run on Linux (the WPF suite covers them on Windows only):
/// one name whichever OS wrote it, and the desktop entry that stands in for the .lnk.</summary>
public class LauncherShortcutsCoreTests
{
    [Fact]
    public void Names_drop_windows_unsafe_characters_on_every_os_and_the_desktop_entry_matches_the_lnk()
    {
        Assert.Equal(("CC Labs - Back Room.lnk", "--game backroom"), LauncherShortcuts.Describe(" backroom ", "Back: Room?"));
        Assert.Equal(("Conditioning Control Panel.lnk", "--panel"), LauncherShortcuts.Describe("PANEL", "x"));
        Assert.Null(LauncherShortcuts.IconFileName("a:b"));
        Assert.Equal("dtrh.ico", LauncherShortcuts.IconFileName("dtrh"));

        // The desktop entry carries the same arguments as the .lnk.
        var (file, text) = LauncherShortcuts.DescribeDesktopEntry("BackRoom", "The Back\nRoom", "\"/opt/ccp\"", "/opt/r/backroom.ico");
        Assert.Equal("cclabs-backroom.desktop", file);
        Assert.Equal("[Desktop Entry]\nType=Application\nName=CC Labs - The BackRoom\n" +
                     "Exec=\"/opt/ccp\" --game BackRoom\nIcon=/opt/r/backroom.ico\nTerminal=false\nCategories=Game;\n", text);

        var (panelFile, panel) = LauncherShortcuts.DescribeDesktopEntry(null, null, "ccp", null);
        Assert.Equal("cclabs-panel.desktop", panelFile);
        Assert.Contains("Name=Conditioning Control Panel\nExec=ccp --panel\nTerminal", panel);
    }
}
