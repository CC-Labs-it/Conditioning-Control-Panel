using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF App.xaml.cs:3060: FunScriptService follows the video playhead. Here the mandatory
/// video overlay's LibVLC TimeChanged is that playhead.</summary>
public sealed class FunScriptPlayheadTests
{
    [Fact]
    public void The_overlay_playhead_reaches_a_subscriber_until_it_unsubscribes()
    {
        long seen = -1;
        var unsubscribe = MandatoryVideoOverlay.SubscribePlaybackTime(ms => seen = ms);
        Assert.NotNull(unsubscribe);   // null would tell FunScriptService "no video service"
        try
        {
            MandatoryVideoOverlay.Instance.RaisePlaybackTime(1234);
            Assert.Equal(1234, seen);
        }
        finally { unsubscribe!(); }
        MandatoryVideoOverlay.Instance.RaisePlaybackTime(5678);
        Assert.Equal(1234, seen);
    }

    [Fact]
    public void Startup_seeds_the_seam_and_the_player_feeds_it()
    {
        Assert.Matches(@"FunScriptService\.SubscribePlaybackTime\s*=\s*[\w.]*MandatoryVideoOverlay\.SubscribePlaybackTime;",
            Src("App.axaml.cs"));
        Assert.Matches(@"_player\.TimeChanged \+=[^}]*?RaisePlaybackTime\(e\.Time\)",
            Src("Views", "Overlays", "MandatoryVideoOverlay.cs"));
    }

    [Fact]
    public void The_welcome_back_sheet_flushes_a_parked_media_offer_when_it_closes()
    {
        // WPF parks the offer behind every startup modal (App.xaml.cs:840-845); each one flushes on close.
        Assert.Matches(@"finally\s*\{\s*MainShellWindow\.IsStartupDialogShowing = false;\s*RemoteMediaOffer\.FlushPending\(\);",
            Src("Platform", "WelcomeBackOffer.cs"));
    }

    private static string Src(params string[] p) =>
        Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", Path.Combine(p))),
            @"//[^\n]*|/\*.*?\*/|'(?:[^'\\]|\\.)'|[$@]{0,2}""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
