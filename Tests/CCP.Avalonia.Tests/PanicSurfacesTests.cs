using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>P06 made structural: every panic route stops the same registry, and every type that starts a
/// capture, a player or a host is in that registry (or allowlisted with the reason it needs no entry).</summary>
public sealed class PanicSurfacesTests
{
    [Fact]
    public void EachPanicRouteCallsStopAllExactlyOnce() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            // Shipped font setup: whichever class boots the process-wide app first decides fonts,
            // and the subliminal card (closed by panic) fails to resolve its face on a bare bootstrap.
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (enabled, key) = (s.PanicKeyEnabled, s.PanicKey);
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
        var prev = PanicSurfaces.All;
        var seen = new List<MainShellWindow?>();
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", sh => seen.Add(sh)) };
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            foreach (var (name, panic, owner) in new (string, Action, MainShellWindow?)[]
            {
                ("panic key", () => shell.HandlePanicKeyPress(new DateTime(2026, 1, 1)), shell),
                ("tray", MainShellWindow.StopEverything, MainShellWindow.Current),
                ("voice", shell.VoicePanic, shell),
            })
            {
                seen.Clear();
                panic();
                Assert.True(seen.Count == 1, $"{name} called StopAll {seen.Count} times");
                Assert.Same(owner, seen[0]);
            }
        }
        finally
        {
            PanicSurfaces.All = prev;
            shell.Close();
            (s.PanicKeyEnabled, s.PanicKey) = (enabled, key);
        }
    });

    [Fact]
    public void OneFailingStopNeverSkipsTheRest()
    {
        var prev = PanicSurfaces.All;
        var order = new List<string>();
        PanicSurfaces.All = new PanicSurfaces.Surface[]
        {
            new("a", _ => order.Add("a")),
            new("boom", _ => throw new InvalidOperationException()),
            new("b", _ => order.Add("b"), () => throw new InvalidOperationException()),
            new("game", _ => order.Add("game"), () => true),
        };
        try
        {
            Assert.True(PanicSurfaces.AnyOwnsTheScreen());
            PanicSurfaces.StopAll("test");
            Assert.Equal(new[] { "a", "b", "game" }, order);
        }
        finally { PanicSurfaces.All = prev; }
    }

    /// <summary>Surfaces a panic must never lose: the record independent of the registry, so deleting a
    /// registry line fails here. One id per line, sorted, so parallel additions merge cleanly. Ordering
    /// contracts live on the registry entries (<c>After</c>), not here.</summary>
    private static readonly string[] SafetyCritical =
    {
        "ai-followups",
        "attention-test",
        "blink-trainer",
        "camera",
        "chaos",
        "chaos-announcer",
        "chaos-cursor-glow",
        "chaos-dvd",
        "chaos-estim",
        "chaos-field-fx",
        "chaos-flash",
        "chaos-intro",
        "chaos-skia-fx",
        "chaos-vibe-trail",
        "companion-transcript",
        "corner-gif",
        "deeper-editor-audio",
        "engine",
        "first-show",
        "games",
        "gaze-minigame",
        "gif-cascade",
        "haptics",
        "intake",
        "knock-cards",
        "leash-task",
        "lock-cards",
        "mantra",
        "pink-rush",
        "possession",
        "program-session",
        "remote-haptics",
        "speak-cue",
        "takeover-diagnostic",
        "takeover",
        "tour",
        "tube-speech",
        "voice-capture",
    };

    [Fact]
    public void EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered()
    {
        var ids = PanicSurfaces.All.Select(x => x.Id).ToList();
        var dupes = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "Duplicate panic surface ids: " + string.Join(", ", dupes));
        var missing = SafetyCritical.Except(ids).ToList();
        Assert.True(missing.Count == 0, "Safety-critical surfaces missing: " + string.Join(", ", missing));
    }

    /// <summary>Reordering two constrained lines fails here. Intake first and camera last (decision C:
    /// fire-and-forget, nothing after it may wait on it) are exact; every <c>After</c> rule on an entry
    /// (WPF cite beside it in PanicSurfaces.cs) must name a registered surface that stops earlier.</summary>
    [Fact]
    public void TheOrderingConstraintsHold()
    {
        var ids = PanicSurfaces.All.Select(x => x.Id).ToList();
        Assert.Equal("intake", ids[0]);
        Assert.Equal("camera", ids[^1]);
        var broken = PanicSurfaces.All.SelectMany((s, i) => (s.After ?? Array.Empty<string>())
                .Where(f => !(ids.IndexOf(f) is >= 0 and var a && a < i))
                .Select(f => $"{f} before {s.Id}")).ToList();
        Assert.True(broken.Count == 0, "Panic order broken: " + string.Join("; ", broken));
    }

    /// <summary>"first>then" WPF/decision ordering pairs that must stay declared on the registry (cites beside
    /// each <c>After</c> in PanicSurfaces.cs): the record independent of the registry, so retargeting or deleting a
    /// rule fails here. One pair per line, sorted; new rules on entries need no edit here.</summary>
    private static readonly string[] PinnedOrder =
    {
        "ai-followups>engine",
        "blink-trainer>engine",
        "chaos>engine",
        "corner-gif>tube-speech",
        "engine>corner-gif",
        "engine>lock-cards",
        "engine>pink-rush",
        "first-show>ai-followups",
        "gaze-minigame>camera",
        "gaze-minigame>engine",
        "haptics>engine",
        "intake>voice-capture",
        "leash-task>engine",
        "leash-task>lock-cards",
        "mantra>engine",
        "remote-haptics>engine",
        "takeover>engine",
        "voice-capture>ai-followups",
    };

    [Fact]
    public void TheWpfOrderingRulesStayDeclared()
    {
        var rules = PanicSurfaces.All.SelectMany(s => (s.After ?? Array.Empty<string>()).Select(f => $"{f}>{s.Id}")).ToHashSet();
        var lost = PinnedOrder.Where(p => !rules.Contains(p)).ToList();
        Assert.True(lost.Count == 0, "Ordering rules no longer declared on PanicSurfaces.All: " + string.Join(", ", lost));
    }

    /// <summary>P23: comments and string literals never count as code.</summary>
    private static string Code(string src) => Regex.Replace(src,
        @"//[^\n]*|/\*.*?\*/|@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'", " ", RegexOptions.Singleline);

    /// <summary>What "starts something a panic must stop" looks like in source.</summary>
    private static readonly Regex Starts = new(
        @"\.Recognize\w*Async\(|[Tt]racker\.StartAsync\(|WebcamTracker\.Instance\.StartAsync\(|new MediaPlayer\(" +
        @"|new X11\w*(Overlay|Window)\w*\(|\bclass \w+(Host|HostService)\b", RegexOptions.Compiled);

    /// <summary>Types that start something but need no entry of their own, and why.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["LockCardWindow"] = "phrase mic + cards: the 'lock-cards' surface (StopLockCards) closes every card",
        ["DevicesSettingsSection"] = "camera preview/calibration: the 'camera' surface stops WebcamTracker",
        ["SettingsTabView"] = "the Home browser card's webcam pill: the 'camera' surface stops WebcamTracker",
        ["BlinkTrainerTabView"] = "camera: the 'camera' and 'blink-trainer' surfaces",
        ["LibVlcAudio"] = "the CoreAudio sink: Core services stop their clips on the 'engine' surface",
        ["MandatoryVideoOverlay"] = "Core video sink: the 'engine' surface (CoreEngine.Stop) closes it",
        ["BubbleCountWindow"] = "Core bubble-count sink: the 'engine' surface closes it",
        ["BubbleCountHost"] = "Core bubble-count sink: the 'engine' surface closes it",
        ["PopQuizHost"] = "OnEngineStopped closes it after the 'engine' surface",
        ["Program"] = "--speech-check CLI diagnostic: no window, exits when done",
        ["WebHost"] = "a control, not a surface: the window hosting it registers",
        ["LayeredAudio"] = "WPF parity (#668: only stopped when the master switch is off)",
        ["MiniPlayerWindow"] = "only --video-check (a CLI diagnostic) opens it",
        ["DeeperLocalAudio"] = "the Deeper editor's transport: the 'deeper-editor-audio' surface pauses every open editor",
        ["BillboardCardHost"] = "the Home Tonight Board deck: no audio (WPF c0c67ff47), no window; its hold stops off screen",
        ["ClipArtView"] = "WPF parity: the showcase card's muted, no-audio loop on Home (holds off screen, freed on shell close); WPF panic leaves the board alone",
        ["InlineLoopVideo"] = "WPF parity: the help card's muted, no-audio clip poster; it lives only while that card is open, and closing the card detaches and frees it",
        ["RaceTrackPlayer"] = "the race window owns it: the 'games' surface closes GameWindow, whose teardown disposes it (Race_TrackPick_ChartsPlaysAndPanicStops)",
        ["MediaHistoryWindow"] = "WPF parity: the Media Log's muted preview of a user-picked row; WPF panic leaves that window alone",
    };

    [Fact]
    public void EveryStarterIsRegisteredOrAllowlisted()
    {
        var root = RepoRoot();
        var registry = Code(File.ReadAllText(Path.Combine(root, "CCP.Avalonia/Views/Windows/PanicSurfaces.cs")));
        var starters = new HashSet<string>();
        var missing = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "CCP.Avalonia"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || f.EndsWith("PanicSurfaces.cs")) continue;
            var src = Code(File.ReadAllText(f));
            if (!Starts.IsMatch(src)) continue;
            // Every class the file declares, not only the first: the starter may be any of them.
            var types = Regex.Matches(src, @"\bclass (\w+)").Select(m => m.Groups[1].Value).ToList();
            starters.UnionWith(types);
            if (!types.Any(t => Allowed.ContainsKey(t) || Regex.IsMatch(registry, $@"\b{t}\b")))
                missing.Add($"{Path.GetFileName(f)} ({string.Join("/", types)})");
        }
        Assert.True(missing.Count == 0, "Not in PanicSurfaces.All and not allowlisted: " + string.Join(", ", missing));
        var stale = Allowed.Keys.Where(t => !starters.Contains(t)).ToList();
        Assert.True(stale.Count == 0, "Stale allowlist entries: " + string.Join(", ", stale));
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
