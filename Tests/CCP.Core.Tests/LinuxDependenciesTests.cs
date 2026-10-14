using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// docs/avalonia-linux-install.md: the missing-dependency message names the exact command per distro
// family (fake /etc/os-release), and only for libraries the (fake) probe cannot load.
public sealed class LinuxDependenciesTests
{
    private static bool NoVlcNoSecret(string lib) => lib is not ("libvlc.so.5" or "libsecret-1.so.0");

    [Theory]
    [InlineData("NAME=\"CachyOS Linux\"\nID=cachyos\nID_LIKE=arch\n", "sudo pacman -S --needed vlc libsecret")]
    [InlineData("ID=endeavouros\nID_LIKE=arch\n", "sudo pacman -S --needed vlc libsecret")]
    [InlineData("ID=ubuntu\nID_LIKE=debian\n", "sudo apt install libvlc5 vlc-plugin-base libsecret-1-0")]
    [InlineData("ID=linuxmint\nID_LIKE=\"ubuntu debian\"\n", "sudo apt install libvlc5 vlc-plugin-base libsecret-1-0")]
    [InlineData("ID=fedora\n", "sudo dnf install vlc-libs vlc-plugins-base vlc-plugin-ffmpeg vlc-plugin-pulseaudio libsecret")]
    [InlineData("ID=\"rocky\"\nID_LIKE=\"rhel centos fedora\"\n", "sudo dnf install vlc-libs vlc-plugins-base vlc-plugin-ffmpeg vlc-plugin-pulseaudio libsecret")]
    public void MessageNamesTheDistroCommandForWhatIsMissing(string osRelease, string command)
    {
        var msg = LinuxDependencies.Message(LinuxDependencies.Missing(NoVlcNoSecret), osRelease)!.Value;
        Assert.Equal(command, msg.Command);
        Assert.Contains(command, msg.Text);
        Assert.Contains("audio and video, sign-in token store", msg.Text);
    }

    [Fact]
    public void UnknownDistroFallsBackToLibraryNamesWithoutACommand()
    {
        var msg = LinuxDependencies.Message(LinuxDependencies.Missing(NoVlcNoSecret), "ID=opensuse-tumbleweed\nID_LIKE=\"opensuse suse\"\n")!.Value;
        Assert.Null(msg.Command);
        Assert.Contains("libvlc.so.5, libsecret-1.so.0", msg.Text);
        Assert.Null(LinuxDependencies.Message(LinuxDependencies.Missing(NoVlcNoSecret), null)!.Value.Command);
    }

    [Fact]
    public void WebKitEitherSonameSatisfiesWebViewsButWpeNeedsAllThree()
    {
        Assert.Empty(LinuxDependencies.Missing(_ => true));
        Assert.Null(LinuxDependencies.Message(LinuxDependencies.Missing(_ => true), "ID=arch"));
        // Only 4.0 loads: still enough for web views.
        Assert.Empty(LinuxDependencies.Missing(l => !l.StartsWith("libwebkit2gtk") || l == "libwebkit2gtk-4.0.so.37"));
        Assert.Equal(new[] { "web views" },
            LinuxDependencies.Missing(l => !l.StartsWith("libwebkit2gtk")).Select(d => d.Need));
        Assert.Equal(new[] { "web views (WPE)" },
            LinuxDependencies.Missing(l => l != "libWPEWebKit-2.0.so.1").Select(d => d.Need));
        Assert.Equal(new[] { "overlays and panic key" },
            LinuxDependencies.Missing(l => l != "libXi.so.6").Select(d => d.Need));
    }

    // distro-check F2: Ubuntu 24.04/Fedora 44 ship no WPE libraries, Debian 12/Ubuntu 22.04 only WebKit 1.x, so WPE
    // never goes into their install command; the text says those pages are unavailable and offers no Flatpak (none ships).
    [Theory]
    [InlineData("ID=ubuntu\nID_LIKE=debian\n")]
    [InlineData("ID=fedora\n")]
    [InlineData("ID=opensuse-tumbleweed\n")]
    public void MissingWpeSaysUnavailableNotAPackageCommand(string osRelease)
    {
        var onlyWpe = LinuxDependencies.Message(LinuxDependencies.Missing(l => !l.Contains("wpe", System.StringComparison.OrdinalIgnoreCase)), osRelease)!.Value;
        Assert.Null(onlyWpe.Command);
        Assert.Contains("WPE WebKit 2.0", onlyWpe.Text);
        Assert.DoesNotContain("Flatpak", onlyWpe.Text);
        Assert.DoesNotContain("sudo", onlyWpe.Text);
        Assert.DoesNotContain("libwpe", onlyWpe.Text);

        var withVlc = LinuxDependencies.Message(LinuxDependencies.Missing(l => l != "libvlc.so.5" && !l.Contains("WPE")), osRelease)!.Value;
        Assert.Contains("WPE WebKit 2.0", withVlc.Text);
        Assert.DoesNotContain("wpe", withVlc.Command ?? "", System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("libWPE", withVlc.Text);
    }

    // Arch extra ships wpewebkit, wpebackend-fdo and libwpe (pacman -Si, 2026-10-10), matching packaging/aur/PKGBUILD.
    [Fact]
    public void ArchInstallsWpeWithPacman()
    {
        var msg = LinuxDependencies.Message(LinuxDependencies.Missing(l => !l.Contains("wpe", System.StringComparison.OrdinalIgnoreCase)), "ID=cachyos\nID_LIKE=arch\n")!.Value;
        Assert.Equal("sudo pacman -S --needed wpewebkit wpebackend-fdo libwpe", msg.Command);
        Assert.DoesNotContain("WPE WebKit 2.0", msg.Text);
    }

    [Fact]
    public void LibVlcThatLoadsButFailsToStartStillNamesTheAudioPackages()
    {
        // Fedora with vlc-libs alone: libvlc.so.5 loads, new LibVLC throws (no plugins).
        Assert.Empty(LinuxDependencies.Missing(_ => true));
        var missing = LinuxDependencies.Missing(_ => true, audioFailed: true);
        Assert.Equal(new[] { "audio and video" }, missing.Select(d => d.Need));
        Assert.Equal("sudo dnf install vlc-libs vlc-plugins-base vlc-plugin-ffmpeg vlc-plugin-pulseaudio",
            LinuxDependencies.Message(missing, "ID=fedora\n")!.Value.Command);
    }
}

// webview-render: without GStreamer's autoaudiosink (gst-plugins-good) WebKitWebProcess aborts on a page's first WebAudio
// sound and the page freezes on its last frame (~/ccp-port/evidence/webview-render/findings.md). The plugin is a file
// under a gstreamer-1.0 dir, not a soname, so it is probed by path.
public sealed class GstAudioSinkDependencyTests
{
    private static bool NoSink(string lib) => !lib.StartsWith(LinuxDependencies.GstPluginPrefix);

    [Theory]
    [InlineData("ID=cachyos\nID_LIKE=arch\n", "sudo pacman -S --needed gst-plugins-good")]
    [InlineData("ID=ubuntu\nID_LIKE=debian\n", "sudo apt install gstreamer1.0-plugins-good")]
    [InlineData("ID=fedora\n", "sudo dnf install gstreamer1-plugins-good")]
    public void AMissingAudioSinkNamesTheGoodPluginsPackage(string osRelease, string command)
    {
        var missing = LinuxDependencies.Missing(NoSink, LinuxDependencies.WebViews);
        Assert.Equal(new[] { LinuxDependencies.WebViewSound }, missing.Select(d => d.Need));
        var msg = LinuxDependencies.Message(missing, osRelease)!.Value;
        Assert.Equal(command, msg.Command);
        Assert.DoesNotContain("WPE", msg.Text);
    }

    // Probed once per process (startup toast and WebHost panel share this text): an install needs a restart (oracle-deep nit).
    [Theory]
    [InlineData("ID=cachyos\nID_LIKE=arch\n")]
    [InlineData("ID=opensuse-tumbleweed\n")]
    public void TheMessageSaysToRestartAfterInstalling(string osRelease) =>
        Assert.Contains(LinuxDependencies.RestartHint, LinuxDependencies.Message(LinuxDependencies.Missing(NoSink, LinuxDependencies.WebViews), osRelease)!.Value.Text);

    [Fact]
    public void WebViewsAreWebKitGtkPlusTheSinkAndNeverWpe()
    {
        Assert.Equal(new[] { "web views", LinuxDependencies.WebViewSound }, LinuxDependencies.WebViews.Select(d => d.Need));
        // GTK is the Linux engine now; WPE is an opt-in, so neither the startup toast nor the panel may claim pages need it.
        Assert.DoesNotContain(LinuxDependencies.Required, d => d.Need.Contains("WPE"));
        Assert.Contains(LinuxDependencies.Required, d => d.Need == LinuxDependencies.WebViewSound);
    }

    private static System.Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        k => vars.FirstOrDefault(v => v.Key == k).Value;

    [Theory]
    [InlineData("/usr/lib/gstreamer-1.0/libgstautodetect.so")]                     // Arch
    [InlineData("/usr/lib64/gstreamer-1.0/libgstautodetect.so")]                   // Fedora
    [InlineData("/usr/lib/x86_64-linux-gnu/gstreamer-1.0/libgstautodetect.so")]    // Debian/Ubuntu
    [InlineData("/usr/lib/aarch64-linux-gnu/gstreamer-1.0/libgstautodetect.so")]
    public void TheSinkIsFoundInEachDistroPluginDir(string file) =>
        Assert.True(LinuxDependencies.GstPluginPresent("libgstautodetect.so", Env(), p => p == file));

    [Theory]
    [InlineData("GST_PLUGIN_SYSTEM_PATH_1_0")]
    [InlineData("GST_PLUGIN_SYSTEM_PATH")]
    [InlineData("GST_PLUGIN_PATH_1_0")]
    [InlineData("GST_PLUGIN_PATH")]
    public void TheSinkIsFoundOnAnyGstPluginPathVariable(string variable) =>
        Assert.True(LinuxDependencies.GstPluginPresent("libgstautodetect.so", Env((variable, "/nope::/opt/gst/plugins")),
            p => p == "/opt/gst/plugins/libgstautodetect.so"));

    // GStreamer's rule: GST_PLUGIN_SYSTEM_PATH(_1_0) REPLACES the built-in dirs (AppImage/Flatpak/Nix wrappers), so a plugin only
    // in /usr/lib is invisible to WebKit there; counting it would bring the crash back (oracle-deep fix B).
    [Theory]
    [InlineData("GST_PLUGIN_SYSTEM_PATH_1_0")]
    [InlineData("GST_PLUGIN_SYSTEM_PATH")]
    public void ASystemPathOverrideHidesTheDistroDirs(string variable) =>
        Assert.False(LinuxDependencies.GstPluginPresent("libgstautodetect.so", Env((variable, "/app/lib/gstreamer-1.0")),
            p => p == "/usr/lib/gstreamer-1.0/libgstautodetect.so"));

    [Theory]
    [InlineData(null, "/home/u/.local/share/gstreamer-1.0/plugins/libgstautodetect.so")]
    [InlineData("/xdg", "/xdg/gstreamer-1.0/plugins/libgstautodetect.so")]
    public void TheUserPluginDirCounts(string? xdgDataHome, string file) =>
        Assert.True(LinuxDependencies.GstPluginPresent("libgstautodetect.so",
            k => k switch { "XDG_DATA_HOME" => xdgDataHome, "HOME" => "/home/u", _ => null }, p => p == file));

    [Fact]
    public void NoSinkAnywhereIsMissing() =>
        Assert.False(LinuxDependencies.GstPluginPresent("libgstautodetect.so", Env(("GST_PLUGIN_PATH", "/opt/x")), _ => false));
}
