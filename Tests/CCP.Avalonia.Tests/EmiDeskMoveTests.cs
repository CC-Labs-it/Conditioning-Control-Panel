using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskMoveTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Theory]
    [InlineData("bounce")]
    [InlineData("thud")]
    [InlineData("nod")]
    [InlineData("droop")]
    [InlineData("shiver")]
    public Task ChainMovesTheBodyAndPresentationKeepsOffsets(string move) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            var crt = (ScaleTransform)typeof(EmiDeskWindow).GetField("_crtScale", Private)!.GetValue(desk)!;
            var shift = (TranslateTransform)typeof(EmiDeskWindow).GetField("_moveShift", Private)!.GetValue(desk)!;
            var chain = new EmiChain("move-test", "MOVE", new[] { new EmiFrame("0_0", 5000) }, Move: move);
            desk.PlayChain(chain);
            bool moved = false;
            for (int i = 0; i < 30 && !moved; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
                moved = move switch
                {
                    "bounce" => crt.ScaleX > 1.06 && crt.ScaleY < .955,
                    "thud" => crt.ScaleX > 1.08 && crt.ScaleY < .90,
                    "nod" => shift.Y > 2 && shift.X == 0,
                    "droop" => shift.Y > 4 && shift.X == 0,
                    "shiver" => Math.Abs(shift.X) > 1 && shift.Y == 0,
                    _ => false
                };
            }
            Assert.True(moved, move + " did not move the body");
            var xBefore = shift.X;
            var yBefore = shift.Y;
            desk.BeginPresentation(() => { });
            Assert.Equal(xBefore, shift.X);
            Assert.Equal(yBefore, shift.Y);
            Assert.Equal(1, crt.ScaleX);
            Assert.Equal(1, crt.ScaleY);
            await Task.Delay(80);
            Dispatcher.UIThread.RunJobs();
            if (move == "shiver") Assert.NotNull(typeof(EmiDeskWindow).GetField("_moveXAnim", Private)!.GetValue(desk));
            if (move is "nod" or "droop") Assert.NotNull(typeof(EmiDeskWindow).GetField("_moveYAnim", Private)!.GetValue(desk));
            Assert.Equal(1, crt.ScaleX);
            Assert.Equal(1, crt.ScaleY);
            desk.EndPresentation();
        }
        finally { desk.ShutDown(); }
    });

    [Theory]
    [InlineData("bounce", "thud")]
    [InlineData("thud", "bounce")]
    public Task ReplacingPulseStartsAtTheCurrentScale(string first, string second) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            var crt = (ScaleTransform)typeof(EmiDeskWindow).GetField("_crtScale", Private)!.GetValue(desk)!;
            EmiChain Chain(string move) => new("move-test", "MOVE", new[] { new EmiFrame("0_0", 5000) }, Move: move);
            desk.PlayChain(Chain(first));
            for (int i = 0; i < 30 && crt.ScaleX <= 1.04; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(crt.ScaleX > 1.04 && crt.ScaleY < .97);
            double x = crt.ScaleX, y = crt.ScaleY;
            desk.PlayChain(Chain(second));
            Assert.Equal(x, crt.ScaleX);
            Assert.Equal(y, crt.ScaleY);
            bool reachedPeak = false;
            for (int i = 0; i < 25 && !reachedPeak; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
                reachedPeak = second == "bounce" ? crt.ScaleX > 1.06 && crt.ScaleY < .955
                    : crt.ScaleX > 1.08 && crt.ScaleY < .90;
            }
            Assert.True(reachedPeak);
            await Task.Delay(420);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, crt.ScaleX);
            Assert.Equal(1, crt.ScaleY);
        }
        finally { desk.ShutDown(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PresentationBaseReturnsAfterTheMove(bool endEarly) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            var shift = (TranslateTransform)typeof(EmiDeskWindow).GetField("_moveShift", Private)!.GetValue(desk)!;
            desk.PlayChain(new EmiChain("move-test", "MOVE", new[] { new EmiFrame("0_0", 5000) }, Move: "nod"));
            for (int i = 0; i < 40 && shift.Y <= 2; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(shift.Y > 2);
            double animated = shift.Y;
            desk.BeginPresentation(() => { });
            Assert.Equal(animated, shift.Y);
            desk.PresentAt(new Point(400, 400), 23, 0, "idle", "0_0");
            Assert.Equal(animated, shift.Y);
            desk.PresentAt(new Point(400, 400), 37, 0, "idle", "0_0");
            Assert.Equal(animated, shift.Y);
            if (endEarly)
            {
                desk.EndPresentation();
                Assert.Equal(animated, shift.Y);
            }
            await Task.Delay(80);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(37, shift.Y);
            for (int i = 0; i < 150 && typeof(EmiDeskWindow).GetField("_moveYAnim", Private)!.GetValue(desk) != null; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Null(typeof(EmiDeskWindow).GetField("_moveYAnim", Private)!.GetValue(desk));
            Assert.Equal(endEarly ? 0 : 37, shift.Y);
            if (!endEarly)
            {
                desk.PresentAt(new Point(400, 400), 12, 0, "idle", "0_0");
                Assert.Equal(12, shift.Y);
                desk.EndPresentation();
                Assert.Equal(0, shift.Y);
            }
        }
        finally { desk.ShutDown(); }
    });

    [Fact]
    public Task ClickPatStartsTheBounce() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        var state = EmiState.Current;
        int pets = state.PetsTotal;
        bool gist = state.PetGistGot;
        try
        {
            desk.Show();
            Dispatcher.UIThread.RunJobs();
            var body = (Control)typeof(EmiDeskWindow).GetField("_bodyRoot", Private)!.GetValue(desk)!;
            var crt = (ScaleTransform)typeof(EmiDeskWindow).GetField("_crtScale", Private)!.GetValue(desk)!;
            var point = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), desk)!.Value;
            desk.MouseDown(point, MouseButton.Left);
            desk.MouseUp(point, MouseButton.Left);
            bool bounced = false;
            for (int i = 0; i < 20 && !bounced; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
                bounced = crt.ScaleX > 1.02 && crt.ScaleY < .98;
            }
            Assert.True(bounced);
        }
        finally
        {
            desk.ShutDown();
            state.PetsTotal = pets;
            state.PetGistGot = gist;
        }
    });
}
