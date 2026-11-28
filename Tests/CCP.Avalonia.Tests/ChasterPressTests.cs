using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChasterPressTests
{
    [Fact]
    public void ButtonsSquishAndPanicStopsTheMotion() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldMotion = CoreSettings.Current.MotionLevel;
        var oldTime = BeatLoop.Time;
        var oldProbe = UiMotionPreference.OsProbe;
        var clock = new Clock();
        Window? window = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            UiMotionPreference.OsProbe = () => true;
            UiMotionPreference.CacheStamp = long.MinValue;
            BeatLoop.Time = clock;
            var view = new ChasterTabView();
            window = new Window { Width = 1200, Height = 1400, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var link = view.FindControl<Button>("BtnLink")!;
            var consent = view.FindControl<Button>("BtnConsentOk")!;
            link.IsEnabled = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            foreach (var button in new[] { link, consent })
            {
                if (button == consent)
                {
                    view.FindControl<Control>("FactRow")!.IsVisible = false;
                    view.FindControl<Control>("ConsentCard")!.IsVisible = true;
                    window.UpdateLayout();
                }
                button.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                Assert.True(button.IsEffectivelyVisible, button.Name);
                Assert.True(button.IsPointerOver, $"{button.Name} at {point}, bounds {button.Bounds}");
                window.MouseDown(point, MouseButton.Left);
                Assert.True(button.IsPressed, button.Name);
                clock.Advance(80);
                BeatLoop.StepAll();
                var scale = Assert.IsType<ScaleTransform>(button.RenderTransform);
                Assert.Equal(0.97, scale.ScaleX, 3);
                window.MouseMove(new Point(1, 1));
                window.MouseUp(new Point(1, 1), MouseButton.Left);
                clock.Advance(80);
                BeatLoop.StepAll();
                Assert.Equal(1, scale.ScaleX);
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                clock.Advance(40);
                BeatLoop.StepAll();
                Assert.True(scale.ScaleX < 1);
                PanicSurfaces.All.Single(s => s.Id == "chaster-trailer").Stop(null);
                Assert.Equal(1, scale.ScaleX);
                clock.Advance(100);
                BeatLoop.StepAll();
                Assert.Equal(1, scale.ScaleX);
                // Release away from the button so this test does not start linking or consent.
                window.MouseUp(new Point(1, 1), MouseButton.Left);
                clock.Advance(100);
                BeatLoop.StepAll();
                Assert.Equal(1, scale.ScaleX);
            }
        }
        finally
        {
            window?.Close();
            BeatLoop.Time = oldTime;
            UiMotionPreference.OsProbe = oldProbe;
            UiMotionPreference.CacheStamp = long.MinValue;
            CoreSettings.Current.MotionLevel = oldMotion;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
        }
    });

    private sealed class Clock : TimeProvider
    {
        private long _now = 1000000;
        public override long GetTimestamp() => _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void Advance(int ms) => _now += TimeSpan.FromMilliseconds(ms).Ticks;
    }
}
