using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiGlassSpiralTests
{
    [Fact]
    public Task SpiralLandsMovesAndTapsThroughGlass() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var previous = CoreSettings.ServiceProvider;
        var bedtime = EmiState.Current.BedtimeUntil;
        var oldEffect = EmiDeskWindow.GlassSpiralEffect;
        var oldRoll = EmiDeskWindow.GlassRollWrong;
        int fired = 0;
        CoreSettings.ServiceProvider = () => newSettings;
        EmiDeskWindow.GlassSpiralEffect = _ => fired++;
        EmiDeskWindow.GlassRollWrong = () => false;
        var desk = new EmiDeskWindow { GlassChannelOverride = "spiral" };
        try
        {
            PanicAftermath.EndQuiet();
            EmiLineEngine.Instance.ResetForTests();
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip);
            Assert.Equal("spiral", desk.GlassChannel);
            desk.AdvanceGlass(flip.AddMilliseconds(220));
            var canvas = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            var path = Assert.Single(canvas.Children.OfType<Path>());
            var first = path.Data;
            desk.AdvanceGlass(flip.AddMilliseconds(420));
            Assert.NotSame(first, path.Data);
            var body = desk.FindControl<Control>("BodyRoot")!;
            var point = body.TranslatePoint(desk.GlassRect.Center, desk)!.Value;
            desk.MouseDown(point, MouseButton.Left);
            Assert.True(desk.GlassIsLive);
            desk.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, fired);
            Assert.False(desk.GlassIsLive);

            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            desk.AdvanceGlass(DateTime.UtcNow.AddSeconds(62));
            Assert.True(desk.GlassIsLive);
            PanicAftermath.ArmQuiet();
            desk.MouseDown(point, MouseButton.Left); desk.MouseUp(point, MouseButton.Left);
            Assert.Equal(1, fired);
        }
        finally
        {
            PanicAftermath.EndQuiet();
            desk.ShutDown(); EmiLineEngine.Instance.ResetForTests();
            EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = previous;
            EmiDeskWindow.GlassSpiralEffect = oldEffect; EmiDeskWindow.GlassRollWrong = oldRoll;
        }
        return Task.CompletedTask;
    });
    private static readonly SettingsService newSettings = new();
}
