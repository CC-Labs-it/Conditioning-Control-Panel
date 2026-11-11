using System;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskBodyMoveTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(EmiDeskWindow desk, string name) =>
        (T)typeof(EmiDeskWindow).GetField(name, Private)!.GetValue(desk)!;

    private static EmiDeskWindow Desk()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        return new EmiDeskWindow();
    }

    private static void Play(EmiDeskWindow desk, string move) =>
        desk.PlayChain(new EmiChain("test", "test", new[] { new EmiFrame("^_^", 5000) }, Move: move));

    [Theory]
    [InlineData("bounce", 340, 0.4, 1.08, 0.94, 0, 0)]
    [InlineData("thud", 280, 0.4, 1.10, 0.88, 0, 0)]
    [InlineData("nod", 1800, 0.25, 1, 1, 0, 4)]
    [InlineData("droop", 600, 0.5, 1, 1, 0, 6)]
    [InlineData("shiver", 2560, 0.125, 1, 1, 2, 0)]
    public Task ChainMovesUseWpfTimingAndTransforms(string move, double ms, double peak,
        double sx, double sy, double x, double y) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = Desk();
        try
        {
            double duration = 0;
            Action<double>? step = null;
            desk.BodyMoveTween = (d, _, a) => { duration = d; step = a; a(0); };
            Play(desk, move);
            Assert.NotNull(step);
            Assert.Equal(ms, duration);
            var scale = Field<ScaleTransform>(desk, "_bodyMoveScale");
            var shift = Field<TranslateTransform>(desk, "_bodyMoveShift");
            step!(peak);
            Assert.Equal(sx, scale.ScaleX, 6); Assert.Equal(sy, scale.ScaleY, 6);
            Assert.Equal(x, shift.X, 6); Assert.Equal(y, shift.Y, 6);
            if (move is "bounce" or "thud")
            {
                step(0.2); // halfway to the keyframe, cubic ease-out has covered 7/8
                Assert.Equal(1 + (sx - 1) * 0.875, scale.ScaleX, 6);
                Assert.Equal(1 + (sy - 1) * 0.875, scale.ScaleY, 6);
                step(0.7); // halfway back, the same ease applies
                Assert.Equal(sx + (1 - sx) * 0.875, scale.ScaleX, 6);
            }
            else if (move != "droop")
            {
                step(move == "nod" ? 0.75 : 0.875); // last repeated peak
                Assert.Equal(x, shift.X, 6); Assert.Equal(y, shift.Y, 6);
            }
            step(1);
            Assert.Equal(1, scale.ScaleX); Assert.Equal(1, scale.ScaleY);
            Assert.Equal(0, shift.X); Assert.Equal(0, shift.Y);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplacementChainWithoutMoveLeavesNodRunning() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = Desk();
        try
        {
            Action<double>? step = null;
            CancellationToken token = default;
            desk.BodyMoveTween = (_, t, a) => { token = t; step = a; a(0); };
            var shift = Field<TranslateTransform>(desk, "_bodyMoveShift");
            Play(desk, "nod");
            step!(0.25); Assert.Equal(4, shift.Y);
            desk.PlayChain(new EmiChain("plain", "plain", new[] { new EmiFrame("^_^", 5000) }));
            Assert.False(token.IsCancellationRequested);
            Assert.Equal(4, shift.Y);
            step(0.5); Assert.Equal(0, shift.Y);
            step(0.75); Assert.Equal(4, shift.Y);
            step(1); Assert.Equal(0, shift.Y);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DifferentMovePropertiesRunTogetherAndOnlyMatchingMovesReplace() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = Desk();
        try
        {
            var moves = new List<(CancellationToken Token, Action<double> Step)>();
            desk.BodyMoveTween = (_, t, a) => { moves.Add((t, a)); a(0); };
            var shift = Field<TranslateTransform>(desk, "_bodyMoveShift");
            var scale = Field<ScaleTransform>(desk, "_bodyMoveScale");
            Play(desk, "nod"); moves[0].Step(0.25);
            Play(desk, "shiver"); moves[1].Step(0.125);
            Play(desk, "bounce"); moves[2].Step(0.4);
            Assert.False(moves[0].Token.IsCancellationRequested);
            Assert.False(moves[1].Token.IsCancellationRequested);
            Assert.Equal(4, shift.Y); Assert.Equal(2, shift.X); Assert.Equal(1.08, scale.ScaleX, 6);
            moves[0].Step(1); // ending Y must not reset X or scale
            Assert.Equal(0, shift.Y); Assert.Equal(2, shift.X); Assert.Equal(1.08, scale.ScaleX, 6);
            Play(desk, "shiver"); Assert.True(moves[1].Token.IsCancellationRequested);
            Assert.False(moves[2].Token.IsCancellationRequested);
            moves[1].Step(0.125); Assert.Equal(-2, shift.X); // stale tick cannot overwrite the new X
            Play(desk, "thud"); Assert.True(moves[2].Token.IsCancellationRequested);
            Assert.False(moves[3].Token.IsCancellationRequested);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplacementCancelDismissAndCloseResetAndInvalidateTheTween() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = Desk();
        try
        {
            Action<double>? step = null;
            CancellationToken token = default;
            desk.BodyMoveTween = (_, t, a) => { token = t; step = a; a(0); };
            var shift = Field<TranslateTransform>(desk, "_bodyMoveShift");
            Play(desk, "droop"); step!(0.5); Assert.Equal(6, shift.Y);
            var stale = step; var old = token;
            Play(desk, "nod"); Assert.True(old.IsCancellationRequested);
            stale(0.5); Assert.Equal(0, shift.Y);
            step!(0.25); Assert.Equal(4, shift.Y);
            old = token; stale = step;
            desk.CancelChain(); Assert.True(old.IsCancellationRequested); Assert.Equal(0, shift.Y);
            stale(0.25); Assert.Equal(0, shift.Y);
            desk.Show();
            Play(desk, "droop"); step!(0.5); old = token;
            desk.RunDismiss(); Assert.True(old.IsCancellationRequested); Assert.Equal(0, shift.Y);
            desk.StopChainForPanic();
            Play(desk, "droop"); step!(0.5); old = token;
            desk.Close(); Assert.True(old.IsCancellationRequested); Assert.Equal(0, shift.Y);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });
}
