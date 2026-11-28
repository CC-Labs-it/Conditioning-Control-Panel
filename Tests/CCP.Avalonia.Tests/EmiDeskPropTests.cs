using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class EmiDeskPropTests
{
    [Fact]
    public Task PlateTracksBodyAndChainAndHideClearIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var image = desk.FindControl<Image>("PropImage")!;
            foreach (var prop in EmiProps.All)
            {
                desk.ShowProp(prop.Key);
                Assert.True(desk.PropUp);
                Assert.True(image.IsVisible);
                Assert.NotNull(image.Source);
                foreach (double width in new[] { 220.0, 360.0 })
                {
                    desk.ApplyBodyWidth(width);
                    double bh = width * EmiDeskWindow.BodyAspect;
                    Assert.Equal(width * (1 - EmiProps.RightFrac), image.Margin.Right, 6);
                    Assert.Equal(bh * (1 - EmiProps.BottomFrac), image.Margin.Bottom, 6);
                    Assert.Equal(bh * EmiProps.TopFrac(prop), bh - image.Margin.Bottom - image.Height, 5);
                    var transforms = Assert.IsType<TransformGroup>(image.RenderTransform);
                    Assert.Equal(prop.TiltDeg, Assert.IsType<RotateTransform>(transforms.Children[0]).Angle);
                }
            }
            desk.Say("hello");
            Assert.False(desk.PropUp);
            desk.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.False(image.IsVisible);
            Assert.Null(image.Source);
            desk.Show(); desk.ShowProp("phone");
            desk.ShutDown();
            Assert.False(image.IsVisible);
            Assert.Null(image.Source);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RiseSlidesAndHideStopsItImmediately() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldMotion = CoreSettings.Current.MotionLevel;
        var oldGentle = CoreSettings.Current.GentleVisuals;
        var oldProbe = UiMotionPreference.OsProbe;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.GentleVisuals = false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var image = desk.FindControl<Image>("PropImage")!;
            desk.ShowProp("phone");
            var rise = Assert.IsType<TranslateTransform>(Assert.IsType<TransformGroup>(image.RenderTransform).Children[1]);
            Assert.Equal(image.Height * EmiProps.RiseFrac, rise.Y, 5);
            for (int i = 0; i < 50 && rise.Y > 0; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(0, rise.Y, 5);
            desk.HideProp();
            Assert.True(image.IsVisible);
            desk.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.False(image.IsVisible);
            Assert.Null(image.Source);
            Assert.Equal(0, rise.Y);
        }
        finally
        {
            desk.ShutDown();
            CoreSettings.Current.MotionLevel = oldMotion;
            CoreSettings.Current.GentleVisuals = oldGentle;
            UiMotionPreference.OsProbe = oldProbe;
            UiMotionPreference.CacheStamp = long.MinValue;
        }
    });

    [Fact]
    public Task HoldEndsAndMissingArtDeclines() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var desk = new EmiDeskWindow();
        string path = EmiProps.Path("phone")!;
        string backup = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var image = desk.FindControl<Image>("PropImage")!;
            File.Move(path, backup);
            desk.ShowProp("phone");
            Assert.False(desk.PropUp);
            Assert.False(image.IsVisible);
            File.Move(backup, path);
            desk.ShowProp("phone", 40);
            Assert.True(desk.PropUp);
            for (int i = 0; i < 100 && image.IsVisible; i++)
            {
                await Task.Delay(20);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(desk.PropUp);
            Assert.False(image.IsVisible);
            Assert.Null(image.Source);
        }
        finally
        {
            if (File.Exists(backup)) File.Move(backup, path);
            desk.ShutDown();
        }
    });
}
