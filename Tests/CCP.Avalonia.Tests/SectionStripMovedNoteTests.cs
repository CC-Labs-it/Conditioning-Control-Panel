using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF MainWindow.SectionChrome.cs:68 TryRedirectMovedTab: the old "together" key lands on
/// Social > Lobby and, the first three times across the app's life, the strip says "Moved".
/// </summary>
public sealed class SectionStripMovedNoteTests
{
    private static void SetUp()
    {
        if (Application.Current is not null) return;
        // The same bare builder the sibling shell tests use (SectionStripChoosePressTests).
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    [Fact]
    public async Task OldTogetherKeyLandsOnLobbyAndSaysMovedUntilTheLimit()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetUp();
            var s = CoreSettings.Current;
            var hitsBefore = s.NavMovedToastHits;
            var motionBefore = s.MotionLevel;
            var w = new MainShellWindow();
            NavGlow.Time = new SteppedClock();   // the glow outlives a slow runner (NavRailPaintTests)
            try
            {
                s.NavMovedToastHits = MainShellWindow.NavMovedNoteLimit - 2;
                s.MotionLevel = MotionLevel.Full;
                w.Show();
                w.ShowTab("presets");
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;
                strip.MotionOverride = MotionLevel.Full;
                Dispatcher.UIThread.RunJobs();
                Assert.False(strip.MovedNoteShown);

                // Hit one, at Full: the new home glows (WPF SectionChrome.cs:89, posted after the note).
                w.ShowTab("together");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("availablesubjects", w.CurrentTab);
                Assert.True(strip.MovedNoteShown);
                var lobby = w.PageStrip!.PillFor(NavStripTable.ActivePill("availablesubjects") ?? "availablesubjects")!;
                Assert.Contains(AdornerLayer.GetAdornerLayer(lobby)!.Children.OfType<NavGlowRing>(),
                    r => AdornerLayer.GetAdornedElement(r) == lobby);

                // The hold ends above Off: a 300 ms fade, so the note is still up while it fades (WPF :522).
                strip.ExpireMovedNote();
                Assert.True(strip.MovedNoteShown);
                strip.HideNote();
                Assert.False(strip.MovedNoteShown);

                // Hit two, at Off.
                strip.MotionOverride = MotionLevel.Off;
                w.ShowTab("presets");
                w.ShowTab("together");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("availablesubjects", w.CurrentTab);
                Assert.Equal(MainShellWindow.NavMovedNoteLimit, s.NavMovedToastHits);
                Assert.True(strip.MovedNoteShown);
                Assert.Equal(1, strip.MovedNoteOpacity);
                var expected = string.Format(Loc.Get("nav_moved_toast"),
                    Loc.Get(NavSections.Find(NavSections.Social)!.LabelKey),
                    Loc.Get(NavStripTable.PageLabelKey("availablesubjects")!));
                Assert.Equal(expected, strip.MovedNoteTextForTests);
                var hue = NavStripRules.WithAlpha(NavStripRules.Accent(NavSections.Social), NavStripRules.NoteTextAlpha);
                Assert.Equal(hue, ((ISolidColorBrush)strip.MovedNoteBrush!).Color);

                // The hold ends: at Off the note goes at once (WPF :520).
                strip.ExpireMovedNote();
                Assert.False(strip.MovedNoteShown);

                // The limit is spent: still redirected, never said again.
                w.ShowTab("presets");
                w.ShowTab("together");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("availablesubjects", w.CurrentTab);
                Assert.Equal(MainShellWindow.NavMovedNoteLimit, s.NavMovedToastHits);
                Assert.False(strip.MovedNoteShown);
            }
            finally
            {
                NavGlow.Time = TimeProvider.System;
                s.MotionLevel = motionBefore;
                s.NavMovedToastHits = hitsBefore;
                w.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public async Task FadeTimerActuallyHidesTheNote_AndANewNoteCancelsTheOldFade(MotionLevel level)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetUp();
            var clock = new SteppedTimers();
            var strip = new SectionTabStrip { MotionOverride = level, After = clock.After };
            var w = new global::Avalonia.Controls.Window { Content = strip };
            try
            {
                w.Show();
                strip.Show(NavSections.Studio, "presets");
                strip.ShowMovedNote("first");
                clock.Advance(SectionTabStrip.MovedNoteHold);   // the hold timer starts the fade
                Assert.True(strip.MovedNoteShown);
                clock.Advance(SectionTabStrip.MovedNoteFade);
                Assert.False(strip.MovedNoteShown); // the scheduled fade timer, not HideNote's seam

                strip.ShowMovedNote("second");
                strip.ExpireMovedNote();
                strip.ShowMovedNote("replacement");
                clock.Advance(SectionTabStrip.MovedNoteFade);
                Assert.True(strip.MovedNoteShown);
                Assert.Equal("replacement", strip.MovedNoteTextForTests);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>One-shot timers on a stepped clock: Advance fires what is due and not disposed.</summary>
    private sealed class SteppedTimers
    {
        private readonly System.Collections.Generic.List<Pending> _pending = new();
        private TimeSpan _now;

        public IDisposable After(TimeSpan span, Action act)
        {
            var p = new Pending(_now + span, act);
            _pending.Add(p);
            return p;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
            foreach (var p in _pending.Where(p => !p.Disposed && p.Due <= _now).OrderBy(p => p.Due).ToList())
            {
                p.Disposed = true;
                p.Act();
            }
        }

        private sealed class Pending(TimeSpan due, Action act) : IDisposable
        {
            public TimeSpan Due { get; } = due;
            public Action Act { get; } = act;
            public bool Disposed { get; set; }
            public void Dispose() => Disposed = true;
        }
    }

    private sealed class SteppedClock : TimeProvider
    {
        public override long GetTimestamp() => 1_000_000;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
