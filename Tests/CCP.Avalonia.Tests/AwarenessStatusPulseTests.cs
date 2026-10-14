using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessStatusPulseTests
{
    [Fact]
    public void LiveStatusBreathesBehindTheSteadyDotAndParksWithItsHost() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.KeywordTriggersEnabled, s.MotionLevel, s.PerformanceMode);
        var time = new SteppedTime();
        var oldTime = BeatLoop.Time;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        CoreEntitlement.HasPremiumProvider = () => true;
        BeatLoop.Time = time;
        var resources = Application.Current!.Resources;
        var hadLocalTint = resources.Keys.Contains("FxGlowColor");
        var oldTint = resources["FxGlowColor"];
        Window? host = null;
        try
        {
            s.KeywordTriggersEnabled = false;
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            var view = new AwarenessTabView();
            var parent = new Border { Child = view };
            host = new Window { Content = parent, Width = 1200, Height = 900 };
            host.Show(); host.Activate(); Dispatcher.UIThread.RunJobs();
            var dot = view.FindControl<Ellipse>("AwarenessStatusDot")!;
            var glow = view.FindControl<Border>("AwarenessStatusGlow");
            Assert.NotNull(glow);
            Assert.Equal(0, glow.Opacity);
            view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsChecked = true;
            Assert.True(s.KeywordTriggersEnabled); // real consent-gated master editor
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Live", view.FindControl<TextBlock>("TxtAwarenessStatus")!.Text);
            Assert.Null(dot.Effect);
            Assert.Equal(1, dot.Opacity);
            Assert.Same(dot.Parent, glow.Parent); // the moving glow is a sibling, never an Effect ancestor
            Assert.Equal(0.22, glow.Opacity, 3);
            Assert.Equal(10, glow.BoxShadow[0].Blur);
            var loop = Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            Assert.True(loop.OnFrameBeat);
            resources["FxGlowColor"] = Colors.Cyan;
            CoreMods.RaiseModChanged(null, new ModPackage(BuiltInMods.CCPDefault, null, isBuiltIn: true));
            Assert.Equal(Colors.Cyan, glow.BoxShadow[0].Color);
            Assert.Same(loop, Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow)));
            time.Advance(TimeSpan.FromSeconds(2.8)); BeatLoop.StepAll();
            Assert.Equal(0.95, glow.Opacity, 3);
            if (Environment.GetEnvironmentVariable("CCP_TEST_EVIDENCE_DIR") is { Length: > 0 } evidence)
            {
                Directory.CreateDirectory(evidence);
                using var frame = host.CaptureRenderedFrame();
                frame?.Save(System.IO.Path.Combine(evidence, "Awareness-live-status.png"));
            }
            time.Advance(TimeSpan.FromSeconds(2.8)); BeatLoop.StepAll();
            Assert.Equal(0.22, glow.Opacity, 3);
            Assert.Equal(1, dot.Opacity);

            parent.IsVisible = false;
            Assert.False(loop.IsRunning);
            Assert.Equal(0, glow.Opacity);
            parent.IsVisible = true;
            Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            host.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            host.WindowState = WindowState.Normal; host.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));

            typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null); Dispatcher.UIThread.RunJobs();
            Assert.False(host.IsActive);
            Assert.Equal(0, glow.Opacity);
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null); Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsActive);
            s.MotionLevel = MotionLevel.Reduced; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            s.MotionLevel = MotionLevel.Full; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            s.PerformanceMode = true; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Equal(0, glow.Opacity);
            s.PerformanceMode = false; AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Single(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            s.KeywordTriggersEnabled = false; Dispatcher.UIThread.RunJobs();
            Assert.Equal("Off", view.FindControl<TextBlock>("TxtAwarenessStatus")!.Text);
            Assert.Equal(0, glow.Opacity);
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            s.KeywordTriggersEnabled = true; Dispatcher.UIThread.RunJobs();
            parent.Child = null;
            Assert.Equal(0, glow.Opacity);
            Assert.DoesNotContain(BeatLoop.Running, x => ReferenceEquals(x.Owner, glow));
            s.KeywordTriggersEnabled = false;
            CoreMods.RaiseModChanged(null, new ModPackage(BuiltInMods.CCPDefault, null, isBuiltIn: true));
            AmbientFxCanvas.Env.RaiseMotionGateChanged(); // detached static callbacks must not restart it
            Assert.Equal(0, glow.Opacity);
        }
        finally
        {
            host?.Close(); Dispatcher.UIThread.RunJobs();
            CoreEntitlement.HasPremiumProvider = oldPremium;
            if (hadLocalTint) resources["FxGlowColor"] = oldTint;
            else resources.Remove("FxGlowColor");
            (s.KeywordTriggersEnabled, s.MotionLevel, s.PerformanceMode) = old;
            BeatLoop.Time = oldTime;
        }
    });
}
