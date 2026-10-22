using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using IFrameSource = ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource;

namespace CCP.Avalonia.Tests;

/// <summary>chaos-dvd-overlay: logos fly on one click-through host window, stepped by the host's frame
/// source (no timer of their own); the panic registry closes it; no compositor = no window.</summary>
public sealed class ChaosDvdOverlayTests
{
    private sealed class SteppedFrames : IFrameSource
    {
        public readonly List<Action<TimeSpan>> Subs = new();
        public void Subscribe(Action<TimeSpan> f) { if (!Subs.Contains(f)) Subs.Add(f); }
        public void Unsubscribe(Action<TimeSpan> f) => Subs.Remove(f);
        public void Frame(double sec) { foreach (var s in Subs.ToArray()) s(TimeSpan.FromSeconds(sec)); }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task LogosFlyOnTheHostFramesAndPanicClosesTheWindow()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var (can, prep, frames, sfx) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx);
            var src = new SteppedFrames();
            var host = new Window();
            host.Show();
            try
            {
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.FrameSourceFor = _ => src;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                ChaosDvdOverlay.Launch(host, 10, 1, 1, count: 2);

                var w = Assert.IsType<Window>(ChaosDvdOverlay.Window);
                Assert.False(w.IsHitTestVisible);
                var canvas = Assert.IsType<Canvas>(w.Content);
                Assert.Equal(2, canvas.Children.Count);
                Assert.Single(src.Subs);
                var label = canvas.Children[0];
                var before = (Canvas.GetLeft(label), Canvas.GetTop(label));
                src.Frame(1.000);
                src.Frame(1.050);
                Assert.NotEqual(before, (Canvas.GetLeft(label), Canvas.GetTop(label)));
                Assert.InRange(label.Opacity, 0.01, 0.85);

                PanicSurfaces.All.Single(s => s.Id == "chaos-dvd").Stop(null);
                Assert.Null(ChaosDvdOverlay.Window);
                Assert.False(w.IsVisible);
                Assert.False(ChaosDvdOverlay.AnyActive);
                Assert.Empty(src.Subs);
            }
            finally
            {
                ChaosDvdOverlay.CloseActive();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx) = (can, prep, frames, sfx);
                host.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LastLogoRetiringDropsTheFrameSubscriptionAndARefusedWindowStaysNull()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var (can, prep, frames, sfx) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx);
            var src = new SteppedFrames();
            var host = new Window();
            host.Show();
            try
            {
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.FrameSourceFor = _ => src;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };

                ChaosDvdOverlay.PrepareWindow = (_, _) => false;   // the platform refuses click-through/override-redirect
                ChaosDvdOverlay.Launch(host, 1, 1, 1);
                Assert.Null(ChaosDvdOverlay.Window);
                Assert.False(ChaosDvdOverlay.AnyActive);
                Assert.Empty(src.Subs);

                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.Launch(host, 1, 1, 1);   // 1 s flight + 240 ms fade
                Assert.Single(src.Subs);
                for (var t = 0.0; t <= 2.0 && src.Subs.Count > 0; t += 0.05) src.Frame(t);
                Assert.False(ChaosDvdOverlay.AnyActive);
                Assert.Empty(src.Subs);
                Assert.Empty(Assert.IsType<Canvas>(ChaosDvdOverlay.Window!.Content).Children);
            }
            finally
            {
                ChaosDvdOverlay.CloseActive();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx) = (can, prep, frames, sfx);
                host.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task NoCompositorMeansNoWindowAndNoLogo()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var (can, prep) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow);
            var host = new Window();
            host.Show();
            try
            {
                ChaosDvdOverlay.CanOverlay = () => false;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.Launch(host, 10, 1, 1);
                Assert.Null(ChaosDvdOverlay.Window);
                Assert.False(ChaosDvdOverlay.AnyActive);
            }
            finally
            {
                ChaosDvdOverlay.CloseActive();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow) = (can, prep);
                host.Close();
            }
            return Task.CompletedTask;
        });
    }
}
