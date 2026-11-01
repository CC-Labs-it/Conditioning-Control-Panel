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
    public Task ChainMovesTheBodyAndPresentationStopsOffsets(string move) => AvaloniaTestDispatcher.RunAsync(async () =>
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
            desk.BeginPresentation(() => { });
            Assert.Equal(0, shift.X);
            Assert.Equal(0, shift.Y);
            Assert.Equal(1, crt.ScaleX);
            Assert.Equal(1, crt.ScaleY);
            await Task.Delay(80);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, shift.X);
            Assert.Equal(0, shift.Y);
            Assert.Equal(1, crt.ScaleX);
            Assert.Equal(1, crt.ScaleY);
            desk.EndPresentation();
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
