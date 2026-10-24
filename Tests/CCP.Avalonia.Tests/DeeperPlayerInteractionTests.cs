using System;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class DeeperPlayerAudioTests
{
    private static void KeyDown(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    private static void KeyUp(Control control, Key key) =>
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
    private static void Press(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        KeyDown(control, key, modifiers);
        KeyUp(control, key);
    }
    private static void Set(object obj, string field, object value) =>
        obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);

    [Fact]
    public Task KeyboardTransportClampsSeeksSuppressesToggleRepeatsAndPreservesMutedVolume() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var saved = CoreSettings.Current.DeeperPlayerVolume;
        CoreSettings.Current.DeeperPlayerVolume = 37;
        var audio = new Audio();
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        var win = new EnhancementPlayerWindow(null, null);
        try
        {
            win.Show();
            win.OpenLocalMediaFile("keyboard.mp3");
            Dispatcher.UIThread.RunJobs();
            Assert.True(audio.Playing);
            KeyDown(win, Key.Space);
            Assert.False(audio.Playing);
            KeyDown(win, Key.Space);
            Assert.False(audio.Playing);
            KeyUp(win, Key.Space);
            Press(win, Key.Space);
            Assert.True(audio.Playing);
            Press(win, Key.Right);
            Assert.Equal(5, audio.PositionSeconds);
            Press(win, Key.Right, KeyModifiers.Shift);
            Assert.Equal(35, audio.PositionSeconds);
            audio.PositionSeconds = 94;
            Press(win, Key.Right);
            Assert.Equal(95, audio.PositionSeconds);
            audio.PositionSeconds = 2;
            Press(win, Key.Left, KeyModifiers.Shift);
            Assert.Equal(0, audio.PositionSeconds);
            KeyDown(win, Key.M);
            KeyDown(win, Key.M);
            Assert.Equal(0, audio.Volume);
            Assert.Equal(37, CoreSettings.Current.DeeperPlayerVolume); // muted zero never persists
            KeyUp(win, Key.M);
            Press(win, Key.M);
            Assert.Equal(37, audio.Volume);
            Press(win, Key.M);
            win.FindControl<Slider>("SliderVolume")!.Value = 23; // manual adjustment ends mute
            Press(win, Key.M);
            Assert.Equal(0, audio.Volume);
            Press(win, Key.M);
            Assert.Equal(23, audio.Volume);
            win.FindControl<Slider>("SliderVolume")!.Value = 98;
            Press(win, Key.Up);
            Assert.Equal(100, audio.Volume);
            win.FindControl<Slider>("SliderVolume")!.Value = 2;
            Press(win, Key.Down);
            Assert.Equal(0, audio.Volume);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; CoreSettings.Current.DeeperPlayerVolume = saved; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EscapeTapKeepsPausedAudioAndHoldStopsItWithStrictLockAndPopupGuards() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var strict = CoreSettings.Current.StrictLockEnabled;
        var audio = new Audio();
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        var win = new EnhancementPlayerWindow(null, null);
        try
        {
            CoreSettings.Current.StrictLockEnabled = false;
            win.Show();
            win.OpenLocalMediaFile("escape.mp3");
            Dispatcher.UIThread.RunJobs();
            Press(win, Key.Space);
            Press(win, Key.Escape);
            Assert.True(win.IsVisible);
            Assert.False(audio.Disposed);
            KeyDown(win, Key.Escape);
            Assert.True(win.FindControl<StackPanel>("HoldEscHint")!.IsVisible);
            // Exercise the production timer callback without waiting three real seconds.
            Set(win, "_escHoldStart", DateTime.UtcNow.AddSeconds(-3.1));
            Invoke(win, "EscHoldTimer_Tick", null, EventArgs.Empty);
            Assert.True(audio.Disposed);
            Assert.False(win.FindControl<StackPanel>("HoldEscHint")!.IsVisible);
            Assert.Equal(Loc.Get("deeper_player_status_escape_stopped"), win.FindControl<TextBlock>("TxtStatus")!.Text);
            KeyUp(win, Key.Escape);
            Assert.True(win.IsVisible); // the hold consumed the release
            CoreSettings.Current.StrictLockEnabled = true;
            Press(win, Key.Escape);
            Assert.True(win.IsVisible);
            Assert.False(win.FindControl<StackPanel>("HoldEscHint")!.IsVisible);
            CoreSettings.Current.StrictLockEnabled = false;
            Click(win, "BtnChange");
            Press(win, Key.Escape);
            Assert.True(win.IsVisible);
            Assert.False(win.FindControl<global::Avalonia.Controls.Primitives.Popup>("ChangePopup")!.IsOpen);
            Press(win, Key.Escape);
            Assert.False(win.IsVisible);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; CoreSettings.Current.StrictLockEnabled = strict; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TextInputKeepsItsKeysAndPanicCancelsTheHoldHint() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var audio = new Audio();
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        var win = new EnhancementPlayerWindow(null, null);
        try
        {
            win.Show();
            win.OpenLocalMediaFile("focus.mp3");
            Dispatcher.UIThread.RunJobs();
            var input = new TextBox();
            ((Grid)win.Content!).Children.Add(input);
            Assert.True(input.Focus());
            Press(input, Key.Space);
            Press(input, Key.Right);
            Assert.True(audio.Playing);
            Assert.Equal(0, audio.PositionSeconds);
            win.FindControl<Button>("BtnPlayPause")!.Focus();
            KeyDown(win, Key.Escape);
            Assert.True(win.FindControl<StackPanel>("HoldEscHint")!.IsVisible);
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All
                .Single(s => s.Id == "deeper-player-audio").Stop(null);
            Assert.False(win.FindControl<StackPanel>("HoldEscHint")!.IsVisible);
            Assert.True(audio.Disposed);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TimelineHoverFormatsAndClampsWithoutSeekingAndCloseCancelsEscapeTimer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var audio = new Audio();
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        var win = new EnhancementPlayerWindow(new Enhancement { MediaType = MediaTypes.Audio }, "test");
        try
        {
            win.Show();
            win.OpenLocalMediaFile("hover.mp3");
            Dispatcher.UIThread.RunJobs();
            win.UpdateLayout();
            var canvas = win.FindControl<Canvas>("MiniTimelineCanvas")!;
            var width = canvas.Bounds.Width;
            Assert.True(width > 0);
            var label = win.FindControl<Border>("MiniHoverLabel")!;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            // Real pointer path: PointerMoved shows the label, PointerExited hides it.
            win.MouseMove(canvas.TranslatePoint(new Point(width / 2, canvas.Bounds.Height / 2), win)!.Value, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(label.IsVisible);
            Assert.Equal("0:47", win.FindControl<TextBlock>("TxtMiniHoverTime")!.Text);
            win.MouseMove(canvas.TranslatePoint(new Point(width / 2, -40), win)!.Value, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(label.IsVisible);
            Invoke(win, "UpdateMiniHoverLabel", width * 2);
            Assert.Equal("1:35", win.FindControl<TextBlock>("TxtMiniHoverTime")!.Text);
            Invoke(win, "UpdateMiniHoverLabel", -1d);
            Assert.Equal("0:00", win.FindControl<TextBlock>("TxtMiniHoverTime")!.Text);
            Assert.Equal(0, audio.PositionSeconds);
            KeyDown(win, Key.Escape);
            win.Close();
            Assert.False(win.FindControl<StackPanel>("HoldEscHint")!.IsVisible);
            Assert.True(audio.Disposed);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; }
        return Task.CompletedTask;
    });
}
