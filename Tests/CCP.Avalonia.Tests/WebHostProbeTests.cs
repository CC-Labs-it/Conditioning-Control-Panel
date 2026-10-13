using System.Threading.Tasks;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WebHost's availability probe must mirror the adapter Avalonia.Controls.WebView 12.1 actually picks
/// (WebViewAdapter.CreateFactory): on Linux WPE (compositor host) when installed, else WebKitGTK. 12.1 reports
/// WebKitGtk as NativeDialog and WPE as OffscreenRenderer, so a NativeControlHost-only check refused every Linux box.
/// Linux defaults to WebKitGTK (webview-render): the live "rAF stalls" were WebKitWebProcess aborting without GStreamer's
/// autoaudiosink, so a missing sink draws the panel; WPE is opt-in and only where libWPEWebKit still has the libwpe API.
/// </summary>
public sealed class WebHostProbeTests
{
    private static DetailedWebViewAdapterInfo Info(WebViewAdapterType type, bool installed, WebViewEmbeddingScenario scen, string? reason = null, bool supported = true) =>
        new(type, WebViewEngine.WebKit, installed ? "2.52.6" : null, supported, installed, reason, scen);

    private static readonly DetailedWebViewAdapterInfo GtkMissing =
        Info(WebViewAdapterType.WebKitGtk, false, WebViewEmbeddingScenario.None, "Install webkit2gtk 4.0+ package.");
    private static readonly DetailedWebViewAdapterInfo WpeMissing =
        Info(WebViewAdapterType.WpeWebKit, false, WebViewEmbeddingScenario.None, "WPE WebKit libraries are not installed.");
    private static readonly DetailedWebViewAdapterInfo HeadlessAdapter =
        Info(WebViewAdapterType.Headless, true, WebViewEmbeddingScenario.OffscreenRenderer);

    /// <summary>The real probe under the headless test platform: never an engine, even on a box with WPE/WebKitGTK
    /// installed (this dev box), so --render-all and every test draw the fallback panel.</summary>
    [Fact]
    public async Task TheHeadlessPlatformBuildsNoEngine() =>
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Assert.False(WebHost.IsAvailable);
            Assert.False(new WebHost().HasEngine);
            return Task.CompletedTask;
        });

    [Fact]
    public void LinuxWithBothEnginesAsReportedBy12_1IsAvailable() => Assert.Null(WebHost.DecideUnavailableReason(new[]
    {
        Info(WebViewAdapterType.WebKitGtk, true, WebViewEmbeddingScenario.NativeDialog),
        Info(WebViewAdapterType.WpeWebKit, true, WebViewEmbeddingScenario.OffscreenRenderer),
        HeadlessAdapter,
    }, isLinux: true, headless: false, linuxEngine: "wpe"));

    [Fact]
    public void LinuxWithOnlyWpeIsAvailable() => Assert.Null(WebHost.DecideUnavailableReason(new[]
    {
        GtkMissing, Info(WebViewAdapterType.WpeWebKit, true, WebViewEmbeddingScenario.OffscreenRenderer), HeadlessAdapter,
    }, isLinux: true, headless: false, linuxEngine: "wpe"));

    [Fact]
    public void LinuxWithOnlyWebKitGtkIsAvailable() => Assert.Null(WebHost.DecideUnavailableReason(new[]
    {
        Info(WebViewAdapterType.WebKitGtk, true, WebViewEmbeddingScenario.NativeDialog), WpeMissing, HeadlessAdapter,
    }, isLinux: true, headless: false, linuxEngine: "wpe"));

    [Fact]
    public void LinuxWithNeitherEngineNamesTheGtkInstallReason() => Assert.Equal("Install webkit2gtk 4.0+ package.",
        WebHost.DecideUnavailableReason(new[] { GtkMissing, WpeMissing, HeadlessAdapter }, isLinux: true, headless: false, linuxEngine: "wpe"));

    [Fact]
    public void TheHeadlessAdapterNeverCounts() => Assert.Equal(WebHost.NoEngineReason,
        WebHost.DecideUnavailableReason(new[] { HeadlessAdapter }, isLinux: true, headless: false, linuxEngine: "wpe"));

    [Fact]
    public void AHeadlessPlatformDrawsTheFallbackEvenWithAnEngineInstalled() => Assert.NotNull(WebHost.DecideUnavailableReason(new[]
    {
        Info(WebViewAdapterType.WpeWebKit, true, WebViewEmbeddingScenario.OffscreenRenderer),
        Info(WebViewAdapterType.WebView2, true, WebViewEmbeddingScenario.NativeControlHost),
    }, isLinux: true, headless: true, linuxEngine: "wpe"));

    [Fact]
    public void OffLinuxAnOffscreenOnlyAdapterDoesNotCount() => Assert.Equal(WebHost.NoEngineReason, WebHost.DecideUnavailableReason(new[]
    {
        Info(WebViewAdapterType.WpeWebKit, true, WebViewEmbeddingScenario.OffscreenRenderer),
        Info(WebViewAdapterType.WebKitGtk, true, WebViewEmbeddingScenario.NativeDialog),
    }, isLinux: false, headless: false));

    [Fact]
    public void WindowsWebView2WithNativeControlHostIsAvailable() => Assert.Null(WebHost.DecideUnavailableReason(new[]
    {
        Info(WebViewAdapterType.WebView2, true, WebViewEmbeddingScenario.NativeControlHost | WebViewEmbeddingScenario.OffscreenRenderer),
    }, isLinux: false, headless: false));

    [Fact]
    public void AnUnsupportedAdapterNeverCounts() => Assert.Equal(WebHost.NoEngineReason, WebHost.DecideUnavailableReason(new[]
    {
        Info(WebViewAdapterType.WpeWebKit, true, WebViewEmbeddingScenario.OffscreenRenderer, supported: false),
    }, isLinux: true, headless: false, linuxEngine: "wpe"));

    private static readonly DetailedWebViewAdapterInfo[] BothEngines =
    {
        Info(WebViewAdapterType.WebKitGtk, true, WebViewEmbeddingScenario.NativeDialog),
        Info(WebViewAdapterType.WpeWebKit, true, WebViewEmbeddingScenario.OffscreenRenderer),
        HeadlessAdapter,
    };

    [Fact]
    public void LinuxDefaultsToWebKitGtk() => Assert.Null(WebHost.DecideUnavailableReason(BothEngines, isLinux: true, headless: false));

    [Fact]
    public void LinuxOffKeepsTheFallbackEvenWithBothEngines()
    {
        Assert.Equal(WebHost.LinuxOffReason, WebHost.DecideUnavailableReason(BothEngines, isLinux: true, headless: false, linuxEngine: "off"));
        // 3cb80f05a's intent, on the WebKitGTK default: an installed engine that is off names the switch, not "no web view".
        Assert.Contains("CCP_LINUX_WEBVIEW", WebHost.LinuxOffReason);
    }

    [Fact]
    public void AMissingAudioSinkDrawsThePanelNotACrashingPage()
    {
        Assert.Equal(WebHost.NoAudioSinkReason, WebHost.DecideUnavailableReason(BothEngines, isLinux: true, headless: false, audioSink: false));
        Assert.Equal(WebHost.NoAudioSinkReason, WebHost.DecideUnavailableReason(BothEngines, isLinux: true, headless: false, linuxEngine: "wpe", audioSink: false));
        // Windows/macOS engines bring their own audio output.
        Assert.Null(WebHost.DecideUnavailableReason(new[] { Info(WebViewAdapterType.WebView2, true, WebViewEmbeddingScenario.NativeControlHost) },
            isLinux: false, headless: false, audioSink: false));
    }

    // oracle-deep fix A: an explicit CCP_LINUX_WEBVIEW=gtk overrides the sink probe (a false negative must not block every page
    // for good); the unset default stays gated.
    [Theory]
    [InlineData("gtk", true)]
    [InlineData(" GTK ", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("wpe", false)]
    [InlineData("off", false)]
    public void OnlyAnExplicitGtkSkipsTheAudioSinkCheck(string? value, bool skips) =>
        Assert.Equal(skips, WebHost.SkipsAudioSinkCheck(value));

    // The wiring ProbeUnavailableReason uses: the override turns a missing sink into ready; the default stays gated.
    [Fact]
    public void TheGtkOverrideReachesTheProbe()
    {
        Assert.True(WebHost.AudioSinkReady(linux: true, headless: false, probe: () => false, env: "gtk"));
        Assert.False(WebHost.AudioSinkReady(linux: true, headless: false, probe: () => false, env: null));
        Assert.True(WebHost.AudioSinkReady(linux: true, headless: false, probe: () => true, env: null));
        Assert.True(WebHost.AudioSinkReady(linux: false, headless: false, probe: () => false, env: null));
    }

    [Theory]
    [InlineData(null, true, "gtk")]
    [InlineData(null, false, "gtk")]
    [InlineData("", true, "gtk")]
    [InlineData("gtk", true, "gtk")]
    [InlineData("webkit", true, "gtk")]
    [InlineData(" WPE ", true, "wpe")]
    [InlineData("wpe", false, "gtk")]   // wpewebkit 2.54 dropped webkit_web_view_backend_new: 12.1's WPE adapter would crash the app
    [InlineData("off", true, "off")]
    [InlineData(" Off", false, "off")]
    public void TheEnvVarPicksTheEngineAndWpeNeedsItsLegacyApi(string? value, bool wpeLegacyApi, string expected)
    {
        var asked = false;
        Assert.Equal(expected, WebHost.LinuxEngine(value, () => { asked = true; return wpeLegacyApi; }));
        // libWPEWebKit is only dlopened when WPE was asked for.
        Assert.Equal(value?.Trim().ToLowerInvariant() == "wpe", asked);
    }

    [Fact]
    public void TheGtkOptInNeedsWebKitGtkNotJustWpe()
    {
        var wpeOnly = new[] { GtkMissing, Info(WebViewAdapterType.WpeWebKit, true, WebViewEmbeddingScenario.OffscreenRenderer) };
        Assert.Equal("Install webkit2gtk 4.0+ package.", WebHost.DecideUnavailableReason(wpeOnly, isLinux: true, headless: false, linuxEngine: "gtk"));
        Assert.Null(WebHost.DecideUnavailableReason(new[] { Info(WebViewAdapterType.WebKitGtk, true, WebViewEmbeddingScenario.NativeDialog), WpeMissing },
            isLinux: true, headless: false, linuxEngine: "gtk"));
    }
}
