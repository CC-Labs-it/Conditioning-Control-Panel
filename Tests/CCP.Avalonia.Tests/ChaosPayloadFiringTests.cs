using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class ChaosPayloadFiringTests
{
    [Fact]
    public void CascadeUsesWpfParametersAndGlobalDuration()
    {
        var old = EffectPayload.GlobalDurationMult;
        var oldFirer = EffectPayload.Firer;
        try
        {
            EffectPayload.GlobalDurationMult = 2.5;
            double[]? actual = null;
            EffectPayload.Firer = p => App.FireChaosPayload(p, (rate, duration, size, speed, opacity, scale) =>
                actual = new[] { rate, duration, size, speed, opacity, scale });
            new GifCascadePayload { DurationMult = 3 }.Fire();
            Assert.Equal(new[] { 1.67, 15.0, 400.0, 3.6, 0.9, 0.45 }, actual);
        }
        finally { EffectPayload.GlobalDurationMult = old; EffectPayload.Firer = oldFirer; }
    }

    [Fact]
    public void OtherPayloadDoesNotStartCascade()
    {
        bool called = false;
        App.FireChaosPayload(new FlashPayload(), (_, _, _, _, _, _) => called = true);
        Assert.False(called);
    }

    [Fact]
    public void EmptyPoolDoesNotCreateOverlay()
    {
        ChaosGifCascadeOverlay.CloseActive();
        ChaosGifCascadeOverlay.ShowWithFiles(new List<string>(), 1.67, 6, 400, 3.6, 0.9, 0.45);
        Assert.Null(ChaosGifCascadeOverlay.Active);
    }

    [Fact]
    public async Task DesktopStartupConnectsPayloadAndPanicCancelsPendingPick()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-cascade-fire-").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "images"));
        File.WriteAllBytes(Path.Combine(dir, "images", "one.gif"), Convert.FromBase64String(
            "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var app = new App { ApplicationLifetime = new ClassicDesktopStyleApplicationLifetime() };
                try
                {
                    EffectPayload.Firer = null;
                    await Assert.ThrowsAsync<OperationCanceledException>(() => app.StartDesktop((progress, _) =>
                        progress == 0.4 ? Task.FromException(new OperationCanceledException()) : Task.CompletedTask));
                    Assert.NotNull(EffectPayload.Firer);
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    CorePaths.EffectiveAssetsProvider = () => { entered.Set(); release.Wait(); return dir; };

                    new GifCascadePayload().Fire();
                    Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))));
                    var pending = Assert.IsAssignableFrom<Task>(ChaosGifCascadeOverlay.LastPick);
                    PanicSurfaces.All.Single(x => x.Id == "gif-cascade").Stop(null);
                    release.Set();
                    await pending;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Null(ChaosGifCascadeOverlay.Active);

                    // A later fire must still reach the real overlay through the startup registration.
                    new GifCascadePayload().Fire();
                    await Assert.IsAssignableFrom<Task>(ChaosGifCascadeOverlay.LastPick);
                    Dispatcher.UIThread.RunJobs();
                    Assert.NotNull(ChaosGifCascadeOverlay.Active);
                    Assert.True(ChaosGifCascadeOverlay.IsRaining);
                }
                finally
                {
                    release.Set();
                    ChaosGifCascadeOverlay.CloseActive();
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                    ChaosGifCascadeOverlay.ManualFramesForTests = false;
                }
            });
        }
        finally { Directory.Delete(dir, true); }
    }
}
