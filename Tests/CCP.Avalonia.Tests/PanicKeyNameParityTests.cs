using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF GlobalKeyboardHook.cs:115 and MainWindow.xaml.cs:900 preserve the bound Key name.
/// Linux must translate names that Xlib does not recognize instead of silently disabling panic.</summary>
public sealed class PanicKeyNameParityTests
{
    [Theory]
    [InlineData("OemBackslash", "less")]
    [InlineData("Oem102", "less")]
    [InlineData("Apps", "Menu")]
    [InlineData("LWin", "Super_L")]
    [InlineData("RWin", "Super_R")]
    [InlineData("Cancel", "Break")]
    [InlineData("Separator", "KP_Separator")]
    [InlineData("Oem8", "grave")]
    [InlineData("VolumeMute", "XF86AudioMute")]
    [InlineData("VolumeDown", "XF86AudioLowerVolume")]
    [InlineData("VolumeUp", "XF86AudioRaiseVolume")]
    [InlineData("MediaNextTrack", "XF86AudioNext")]
    [InlineData("MediaPreviousTrack", "XF86AudioPrev")]
    [InlineData("MediaStop", "XF86AudioStop")]
    [InlineData("MediaPlayPause", "XF86AudioPlay")]
    [InlineData("Sleep", "XF86Sleep")]
    [InlineData("BrowserBack", "XF86Back")]
    [InlineData("BrowserForward", "XF86Forward")]
    [InlineData("BrowserRefresh", "XF86Refresh")]
    [InlineData("BrowserStop", "XF86Stop")]
    [InlineData("BrowserSearch", "XF86Search")]
    [InlineData("BrowserFavorites", "XF86Favorites")]
    [InlineData("BrowserHome", "XF86HomePage")]
    [InlineData("LaunchMail", "XF86Mail")]
    [InlineData("SelectMedia", "XF86AudioMedia")]
    [InlineData("LaunchApplication1", "XF86MyComputer")]
    [InlineData("LaunchApplication2", "XF86Calculator")]
    [InlineData("Escape", "Escape")]
    [InlineData("F8", "F8")]
    [InlineData("A", "a")]
    [InlineData("D1", "1")]
    [InlineData("NumPad0", "KP_0")]
    public void StoredNamesResolveToXKeysyms(string stored, string expected)
    {
        Assert.Equal(expected, X11PanicKey.XKeyName(stored));
        // No display is needed: prove the translated spelling is accepted by Xlib itself.
        if (OperatingSystem.IsLinux()) Assert.NotEqual(0UL, X11PanicKey.KeysymOf(stored));
    }
}
