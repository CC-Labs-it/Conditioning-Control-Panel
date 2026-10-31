using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF GlobalKeyboardHook.cs:115 and MainWindow.xaml.cs:900 preserve the bound Key name.
/// Linux must translate names that Xlib does not recognize instead of silently disabling panic.</summary>
public sealed class PanicKeyNameParityTests
{
    [Theory]
    [InlineData("OemBackslash", "")]
    [InlineData("Oem102", "")]
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
        if (OperatingSystem.IsLinux())
        {
            var sym = X11PanicKey.KeysymOf(stored);
            if (expected.Length == 0) Assert.Equal(0UL, sym);
            else Assert.NotEqual(0UL, sym);
        }
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(string display);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, ulong sym);

    // A private X server keeps layout changes away from the desktop and other tests.
    // Linux test dependencies: Xvfb, setxkbmap and the standard xkb-data package.
    [Theory]
    [InlineData("gb", 59)]
    [InlineData("de", 94)]
    public async Task Oem102UsesTheActiveIsoKeyInsteadOfTheLessSymbol(string layout, int lessCode)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var server = Process.Start(new ProcessStartInfo("Xvfb", "-displayfd 1 -noreset -nolisten tcp")
        { RedirectStandardOutput = true, UseShellExecute = false })!;
        IntPtr display = IntPtr.Zero;
        try
        {
            var number = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(int.TryParse(number, out _), "Xvfb did not report a display number");
            var address = ":" + number;
            display = XOpenDisplay(address);
            Assert.NotEqual(IntPtr.Zero, display);
            foreach (var current in new[] { layout, layout == "gb" ? "de" : "gb" })
            {
                using var setup = Process.Start(new ProcessStartInfo("setxkbmap")
                {
                    ArgumentList = { "-display", address, "-layout", current }, UseShellExecute = false
                })!;
                await setup.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(0, setup.ExitCode);
                // Reopen after each map switch so Xlib cannot reuse its cached symbol map.
                XCloseDisplay(display);
                display = XOpenDisplay(address);
                Assert.NotEqual(IntPtr.Zero, display);
                // Positive control proves that the two active maps really differ at keysym level.
                var expectedLess = current == layout ? lessCode : current == "gb" ? 59 : 94;
                Assert.Equal(expectedLess, XKeysymToKeycode(display, X11PanicKey.XStringToKeysym("less")));
                foreach (var alias in new[] { "OemBackslash", "Oem102", " oem102 " })
                    Assert.Equal(94, X11PanicKey.KeycodeOf(display, alias));
                Assert.Equal(59, X11PanicKey.KeycodeOf(display, "OemComma"));
                // Exercise the listener's actual routing: comma never panics, ISO down/up do.
                var saved = X11PanicKey.BoundKeycode;
                var release = X11PanicKey.PanicReleased;
                try
                {
                    int presses = 0, releases = 0;
                    X11PanicKey.BindKeycodeForTest(X11PanicKey.KeycodeOf(display, "Oem102"));
                    X11PanicKey.PanicReleased = () => releases++;
                    Assert.False(X11PanicKey.Route(true, 59, () => presses++));
                    Assert.True(X11PanicKey.Route(true, 94, () => presses++));
                    Assert.True(X11PanicKey.Route(false, 94, () => presses++));
                    Assert.Equal(1, presses);
                    Assert.Equal(1, releases);
                }
                finally
                {
                    X11PanicKey.BindKeycodeForTest(saved);
                    X11PanicKey.PanicReleased = release;
                }
            }
        }
        finally
        {
            if (display != IntPtr.Zero) XCloseDisplay(display);
            if (!server.HasExited) server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

}
