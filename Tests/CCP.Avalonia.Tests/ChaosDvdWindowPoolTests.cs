using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosDvdWindowPoolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ReusesWindowsAndDrainsOnTeardown(bool panic) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareLogoWindow,
            ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.SpankerRedirect, ChaosDvdOverlay.PlaySfx);
        var host = new Window();
        host.Show();
        try
        {
            bool clickable = true;
            ChaosDvdOverlay.CanOverlay = () => true;
            ChaosDvdOverlay.SharedHost = () => false;
            ChaosDvdOverlay.SpankerRedirect = () => clickable;
            ChaosDvdOverlay.PrepareLogoWindow = (_, _, _) => true;
            ChaosDvdOverlay.PlaySfx = (_, _) => { };
            ChaosDvdOverlay.Launch(host, 10, 1, 1, text: "first");
            var first = Assert.Single(ChaosDvdOverlay.LogoWindows).Value;
            var oldPanel = first.Content;
            Assert.True(first.IsHitTestVisible);
            for (int i = 0; i < 3; i++)
            {
                Assert.Single(ChaosDvdOverlay.Flock.Active).RemainingSec = 0;
                ChaosDvdOverlay.Advance(0.3);
                ChaosDvdOverlay.Advance(0.3);
                Assert.Equal(1, ChaosDvdOverlay.PooledWindowCount);
                Assert.False(first.IsVisible);
                clickable = false;
                ChaosDvdOverlay.Launch(host, 10, 1, 0.8, text: "second");
                Assert.Same(first, Assert.Single(ChaosDvdOverlay.LogoWindows).Value);
                Assert.False(first.IsHitTestVisible);
                Assert.NotSame(oldPanel, first.Content);
                oldPanel = first.Content;
            }
            ChaosDvdOverlay.Launch(host, 10, 1, 1, text: "other");
            var active = ChaosDvdOverlay.LogoWindows.Values.ToArray();
            Assert.Single(ChaosDvdOverlay.Flock.Active.Where(l => ChaosDvdOverlay.LogoWindows[l] == first)).RemainingSec = 0;
            ChaosDvdOverlay.Advance(0.3);
            ChaosDvdOverlay.Advance(0.3);
            Assert.Equal(1, ChaosDvdOverlay.PooledWindowCount);
            if (panic) PanicSurfaces.All.Single(s => s.Id == "chaos-dvd").Stop(null);
            else ChaosDvdOverlay.CloseActive();
            Assert.Equal(0, ChaosDvdOverlay.PooledWindowCount);
            Assert.Empty(ChaosDvdOverlay.LogoWindows);
            Assert.All(active, w => Assert.False(w.IsVisible));
        }
        finally
        {
            ChaosDvdOverlay.CloseActive();
            (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareLogoWindow,
                ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.SpankerRedirect, ChaosDvdOverlay.PlaySfx) = saved;
            host.Close();
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task PoolIsCappedAtTwelve() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareLogoWindow,
            ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.SpankerRedirect, ChaosDvdOverlay.PlaySfx);
        var host = new Window();
        host.Show();
        try
        {
            ChaosDvdOverlay.CanOverlay = () => true;
            ChaosDvdOverlay.SharedHost = () => false;
            ChaosDvdOverlay.SpankerRedirect = () => false;
            ChaosDvdOverlay.PrepareLogoWindow = (_, _, _) => true;
            ChaosDvdOverlay.PlaySfx = (_, _) => { };
            for (int i = 0; i < 8; i++)
            {
                ChaosDvdOverlay.Launch(host, 10, 1, 1);
                ChaosDvdOverlay.Launch(host, 10, 1, 1, text: "thought");
            }
            Assert.Equal(16, ChaosDvdOverlay.LogoWindows.Count);
            foreach (var logo in ChaosDvdOverlay.Flock.Active) logo.RemainingSec = 0;
            ChaosDvdOverlay.Advance(0.3);
            ChaosDvdOverlay.Advance(0.3);
            Assert.Equal(12, ChaosDvdOverlay.PooledWindowCount);
        }
        finally
        {
            ChaosDvdOverlay.CloseActive();
            (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareLogoWindow,
                ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.SpankerRedirect, ChaosDvdOverlay.PlaySfx) = saved;
            host.Close();
        }
        return Task.CompletedTask;
    });

}
