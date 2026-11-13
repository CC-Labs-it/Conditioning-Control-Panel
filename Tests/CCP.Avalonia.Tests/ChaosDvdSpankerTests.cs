using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;
using IFrameSource = ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource;

namespace CCP.Avalonia.Tests;

/// <summary>The Spanker capstone (WPF ChaosDvdOverlay.cs:204-206,305-319, restack b138 from #3527): a logo
/// launched while it is worn gets its own click-taking window, and a left click turns it.</summary>
public sealed class ChaosDvdSpankerTests
{
    private sealed class SteppedFrames : IFrameSource
    {
        public readonly List<Action<TimeSpan>> Subs = new();
        public void Subscribe(Action<TimeSpan> f) { if (!Subs.Contains(f)) Subs.Add(f); }
        public void Unsubscribe(Action<TimeSpan> f) => Subs.Remove(f);
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task SpankerLogosTakeClicksAndOthersStayClickThrough(bool shared, bool spanker) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PrepareClickableWindow,
            ChaosDvdOverlay.SpankerRedirect, ChaosDvdOverlay.PlaySfx, ChaosDvdOverlay.FrameSourceFor);
        var previousShared = CoreSettings.Current.ChaosDvdSharedHost;
        var host = new Window();
        host.Show();
        var throughWindows = new List<Window>();
        var clickWindows = new List<Window>();
        int cues = 0;
        try
        {
            CoreSettings.Current.ChaosDvdSharedHost = shared;
            ChaosDvdOverlay.CanOverlay = () => true;
            ChaosDvdOverlay.FrameSourceFor = _ => new SteppedFrames();
            ChaosDvdOverlay.PrepareWindow = (w, _) => { throughWindows.Add(w); return true; };
            ChaosDvdOverlay.PrepareClickableWindow = (w, _) => { clickWindows.Add(w); return true; };
            ChaosDvdOverlay.SpankerRedirect = () => spanker;
            ChaosDvdOverlay.PlaySfx = (key, volume) => { if (key == "dvd_bounce" && volume == 0.4f) cues++; };

            ChaosDvdOverlay.Launch(host, 10, 1, 1);
            var logo = Assert.Single(ChaosDvdOverlay.Flock.Active);
            Assert.Equal(spanker ? 1 : 0, clickWindows.Count);
            Assert.Equal(shared || !spanker ? 1 : 0, throughWindows.Count);   // the shared host, or the logo's own window
            if (spanker)
            {
                var w = clickWindows[0];
                Assert.True(w.IsHitTestVisible);
                Assert.False(w.ShowActivated);   // input never takes focus
                var panel = Assert.IsType<Border>(w.Content);
                var hue = logo.HueIndex;
                ChaosDvdOverlay.SpankerRedirect = () => false;   // sampled at launch, not per click
                panel.RaiseEvent(new PointerPressedEventArgs(panel, new Pointer(1, PointerType.Mouse, true), panel,
                    new Point(1, 1), 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.None));
                Assert.Equal((hue + 1) % 6, logo.HueIndex);
                Assert.Equal(1, cues);
            }
            Assert.Equal(spanker, ChaosDvdOverlay.Smack(logo));
            logo.RemainingSec = 0;
            ChaosDvdOverlay.Advance(0.05);
            Assert.Equal(spanker, ChaosDvdOverlay.Smack(logo));   // a fading logo still answers, as WPF
            ChaosDvdOverlay.Advance(0.3);
            Assert.False(ChaosDvdOverlay.AnyActive);
            Assert.False(ChaosDvdOverlay.Smack(logo));            // a retired logo does not
            Assert.All(clickWindows, w => Assert.False(w.IsVisible));
        }
        finally
        {
            ChaosDvdOverlay.CloseActive();
            (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PrepareClickableWindow,
                ChaosDvdOverlay.SpankerRedirect, ChaosDvdOverlay.PlaySfx, ChaosDvdOverlay.FrameSourceFor) = saved;
            CoreSettings.Current.ChaosDvdSharedHost = previousShared;
            host.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RefusedClickableWindowLeavesNothingBehind() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareClickableWindow, ChaosDvdOverlay.SpankerRedirect,
            ChaosDvdOverlay.FrameSourceFor);
        var previousShared = CoreSettings.Current.ChaosDvdSharedHost;
        var host = new Window();
        host.Show();
        try
        {
            CoreSettings.Current.ChaosDvdSharedHost = false;
            ChaosDvdOverlay.CanOverlay = () => true;
            ChaosDvdOverlay.FrameSourceFor = _ => new SteppedFrames();
            ChaosDvdOverlay.PrepareClickableWindow = (_, _) => false;
            ChaosDvdOverlay.SpankerRedirect = () => true;
            ChaosDvdOverlay.Launch(host, 5, 1, 1);
            Assert.False(ChaosDvdOverlay.AnyActive);   // no stuck toy glow, no input-taking leftover
            Assert.All(ChaosDvdOverlay.Windows, w => Assert.False(w.IsVisible));
        }
        finally
        {
            ChaosDvdOverlay.CloseActive();
            (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareClickableWindow, ChaosDvdOverlay.SpankerRedirect,
                ChaosDvdOverlay.FrameSourceFor) = saved;
            CoreSettings.Current.ChaosDvdSharedHost = previousShared;
            host.Close();
        }
        return Task.CompletedTask;
    });
}
