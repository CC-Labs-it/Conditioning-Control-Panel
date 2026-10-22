using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Focus Gaze (WPF GazeFocusService + MainWindow.LabTab.cs:816-1001): the dwell engine pops
/// an ambient bubble after 600 ms of gaze or on a blink, only while armed; the Play switch refuses ON
/// without the Lab tier and the status line follows the engine.</summary>
public sealed class FocusGazeTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int ms) => Now = Now.AddMilliseconds(ms);
    }

    [Fact]
    public void DwellPopsAfterSixHundredMsBlinkPopsAndDisarmedNeverPops() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var s = CoreSettings.Current;
        var (oldBubbleGaze, oldFlashGaze) = (s.BubbleGazePopEnabled, s.FlashGazePopEnabled);
        var clock = new SteppedClock();
        var popped = new List<AmbientBubble>();
        var gaze = new GazeFocus { Clock = clock, CanRun = () => true };
        gaze.PopBubble = b => { popped.Add(b); b.Popping = true; };
        var mine = new List<AmbientBubble>();
        var bubble = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
        BubbleOverlay.Field.Bubbles.Add(bubble); mine.Add(bubble);
        try
        {
            s.BubbleGazePopEnabled = false;
            gaze.MasterEnabled = true;
            Assert.True(gaze.IsActive);

            gaze.HandleGazeMove(new Point(150, 150));
            gaze.Tick();
            clock.Advance(300);
            gaze.Tick();
            Assert.Equal(1.125, bubble.Scale, 3);   // WPF dwell inflate: 1 + 0.25 t
            Assert.Empty(popped);
            gaze.HandleFaceLost();   // face lost: the dwell resets (WPF keeps a held target sticky otherwise)
            gaze.Tick();
            Assert.Equal(1, bubble.Scale);
            clock.Advance(1000);
            gaze.Tick();
            Assert.Empty(popped);
            gaze.HandleFaceFound();
            gaze.Tick();
            clock.Advance(599);
            gaze.Tick();
            Assert.Empty(popped);
            clock.Advance(1);
            gaze.Tick();
            Assert.Equal(new[] { bubble }, popped);

            // Blink while looking near a fresh bubble pops it, after the cooldown.
            var second = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
            BubbleOverlay.Field.Bubbles.Add(second); mine.Add(second);
            gaze.HandleBlink();
            Assert.Single(popped);   // still cooling down
            clock.Advance(GazeFocus.CooldownMs);
            gaze.HandleBlink();
            Assert.Equal(2, popped.Count);

            // Disarmed (no master, no "stare to pop"): a bubble never reacts to gaze.
            var third = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
            BubbleOverlay.Field.Bubbles.Add(third); mine.Add(third);
            gaze.MasterEnabled = false;
            s.FlashGazePopEnabled = true;   // another consumer keeps the engine alive
            gaze.EvaluateDesiredState();
            Assert.True(gaze.IsActive);
            clock.Advance(1000);
            gaze.Tick();
            clock.Advance(1000);
            gaze.Tick();
            gaze.HandleBlink();
            Assert.Equal(2, popped.Count);

            gaze.CanRun = () => false;   // the camera went away: the engine follows it down
            gaze.EvaluateDesiredState();
            Assert.False(gaze.IsActive);
        }
        finally
        {
            gaze.Stop();
            BubbleOverlay.Field.Bubbles.RemoveAll(mine.Contains);
            (s.BubbleGazePopEnabled, s.FlashGazePopEnabled) = (oldBubbleGaze, oldFlashGaze);
        }
    });

    [Fact]
    public void SwitchRefusesOnWithoutLabAndStatusFollowsTheEngine() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var s = CoreSettings.Current;
        var oldEnabled = s.FocusGazeEnabled;
        var (premium, lab, denied) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.ShowDeniedHandler);
        var engine = GazeFocus.Instance;
        var oldCanRun = engine.CanRun;
        var refusals = 0;
        Window? host = null;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.HasLabProvider = () => false;
            CoreEntitlement.ShowDeniedHandler = _ => refusals++;
            s.FocusGazeEnabled = false;
            var play = new PlayTabView();
            host = new Window { Content = play };
            host.Show();
            var chk = play.FindControl<CheckBox>("ChkPlayFocusGaze")!;
            var status = play.FindControl<TextBlock>("TxtPlayFocusGazeStatus")!;

            chk.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, refusals);
            Assert.False(chk.IsChecked);
            Assert.False(s.FocusGazeEnabled);
            Assert.False(engine.MasterEnabled);
            Assert.Equal("", status.Text);

            // A saved intent with the engine held back reads "waiting"; the engine starting says "active".
            engine.CanRun = () => false;
            play.SyncFocusGazeToggle(true);
            Assert.True(chk.IsChecked);
            Assert.Equal(Loc.Get("label_focus_gaze_waiting"), status.Text);
            engine.CanRun = () => true;
            engine.MasterEnabled = true;
            Assert.Equal(Loc.Get("label_focus_gaze_active"), status.Text);

            chk.IsChecked = false;   // OFF is never gated and releases the master
            Dispatcher.UIThread.RunJobs();
            Assert.False(engine.MasterEnabled);
            Assert.False(s.FocusGazeEnabled);
            Assert.Equal("", status.Text);
        }
        finally
        {
            host?.Close();
            engine.MasterEnabled = false;
            engine.CanRun = oldCanRun;
            engine.Stop();
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.ShowDeniedHandler) = (premium, lab, denied);
            s.FocusGazeEnabled = oldEnabled;
        }
    });
}
