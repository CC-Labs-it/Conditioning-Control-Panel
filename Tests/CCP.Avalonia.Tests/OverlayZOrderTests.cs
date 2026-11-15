using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;
public class OverlayZOrderTests
{
    [Fact]
    public Task EachEffectPinsBelowItsOwnMonitorAndTimerStops() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var v1 = new Window(); var v2 = new Window();
        var tint = new Window(); var spiral = new Window(); var corner = new Window(); var blur = new Window();
        var monitors = new Dictionary<Window, int> { [v1] = 0, [v2] = 1, [tint] = 0, [spiral] = 1, [corner] = 1, [blur] = 0 };
        var moves = new List<(Window, Window)>();
        OverlayZOrder.Pin(new[] { tint, spiral, corner, blur }, new[] { v1, v2 }, v1,
            w => monitors[w], (w, a) => moves.Add((w, a)));
        Assert.Equal(new[] { (tint, v1), (spiral, v2), (corner, v2), (blur, v1) }, moves);
        moves.Clear();
        OverlayZOrder.Pin(new[] { tint }, System.Array.Empty<Window>(), null, w => monitors[w], (w, a) => moves.Add((w, a)));
        Assert.Empty(moves);
        bool hadAnchor = false;
        OverlayZOrder.Reconcile(new[] { tint, spiral }, System.Array.Empty<Window>(), new[] { v1, v2 }, true, false,
            w => monitors[w], w => w == v1, (w, a) => moves.Add((w, a)), _ => { }, () => { }, ref hadAnchor);
        Assert.Equal(new[] { (tint, v1), (spiral, v2) }, moves);
        moves.Clear();
        OverlayZOrder.Reconcile(new[] { tint }, new[] { v1 }, new[] { v2 }, true, true,
            w => monitors[w], w => w == v1, (w, a) => moves.Add((w, a)), _ => { }, () => { }, ref hadAnchor);
        Assert.Empty(moves);
        OverlayZOrder.Reconcile(new[] { tint }, System.Array.Empty<Window>(), new[] { v1 }, false, false,
            w => monitors[w], w => w == v1, (w, a) => moves.Add((w, a)), _ => { }, () => { }, ref hadAnchor);
        Assert.Empty(moves);
        OverlayZOrder.Start(); Assert.True(OverlayZOrder.Running);
        OverlayZOrder.Stop(); Assert.False(OverlayZOrder.Running);
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosedAnchorRaisesEffectsOnceAfterQuiescence(bool bubble) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var anchor = new Window(); var tint = new Window(); var blur = new Window();
        var effects = new[] { tint, blur }; var none = System.Array.Empty<Window>();
        var events = new List<string>(); var raises = new List<Window>(); bool hadAnchor = false;
        void Run(Window[] videos, Window[] bubbles, bool hold, bool quiesced) =>
            OverlayZOrder.Reconcile(effects, videos, bubbles, hold, quiesced, _ => 0, _ => true,
                (w, a) => events.Add("pin"), w => { events.Add("raise"); raises.Add(w); },
                () => events.Add("sink"), ref hadAnchor);
        Run(bubble ? none : new[] { anchor }, bubble ? new[] { anchor } : none, bubble, false);
        Assert.Equal(new[] { "pin", "pin", "sink" }, events); events.Clear();
        Run(none, none, false, true); Assert.Empty(events); Assert.True(hadAnchor);
        Run(none, none, false, false);
        Assert.Equal(new[] { tint, blur }, raises); Assert.Equal(new[] { "raise", "raise", "sink" }, events);
        Assert.False(hadAnchor); events.Clear();
        Run(none, none, false, false); Assert.Equal(new[] { "sink" }, events);
        return Task.CompletedTask;
    });
}
