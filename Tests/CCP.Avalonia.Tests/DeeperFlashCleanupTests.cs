using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class DeeperFlashCleanupTests
{
    private static Bitmap Picture() => new(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task StopRetiresOnlyPlayerFlashes(bool duringLoad) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var load = FlashOverlay.LoadPicturesAsync;
        var available = FlashOverlay.OverlaysAvailable;
        var present = FlashOverlay.Present;
        var lead = FlashOverlay.VoiceLead;
        var after = FlashOverlay.After;
        var host = new Window();
        var pending = new TaskCompletionSource<List<(Bitmap Bitmap, PixelRect Rect, string Path, Screen Screen)>>();
        var callbacks = new List<Action>();
        try
        {
            FlashOverlay.CloseAll(final: false);
            host.Show();
            var screen = host.Screens.All[0];
            var rect = new PixelRect(0, 0, 64, 64);
            FlashOverlay.OverlaysAvailable = () => true;
            FlashOverlay.Present = (_, _, _) => true;
            FlashOverlay.VoiceLead = () => Task.CompletedTask;
            FlashOverlay.After = (action, _) => callbacks.Add(action);
            FlashOverlay.LoadPicturesAsync = (_, _, _, _, _, _) => pending.Task;
            Assert.True(FlashOverlay.Show(Picture(), rect, "other.png", screen, 1, TimeSpan.Zero,
                TimeSpan.FromMinutes(1), false, false, 0));
            var other = FlashOverlay.Active[0].Window;
            var dispatcher = new BrowserEnhancementDispatcher();
            var generation = FlashOverlay.Generation;
            FlashOverlay.TriggerOwned(host, 1, null, null, false, dispatcher.FlashOwner);
            Assert.True(FlashOverlay.IsBusy);
            if (duringLoad) dispatcher.StopOneShotFlashes();
            pending.SetResult(new() { (Picture(), rect, "player.png", screen) });
            for (int i = 0; i < 100 && (duringLoad ? FlashOverlay.IsBusy : callbacks.Count == 0); i++)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            if (!duringLoad)
            {
                Assert.Single(callbacks);
                callbacks[0]();
                Assert.Equal(2, FlashOverlay.Active.Count);
                dispatcher.StopOneShotFlashes();
            }
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            foreach (var callback in callbacks) { if (duringLoad) callback(); }
            Assert.Equal(generation, FlashOverlay.Generation);
            Assert.Single(FlashOverlay.Active);
            Assert.Same(other, FlashOverlay.Active[0].Window);
            Assert.True(other.IsVisible);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            host.Close();
            (FlashOverlay.LoadPicturesAsync, FlashOverlay.OverlaysAvailable, FlashOverlay.Present, FlashOverlay.VoiceLead, FlashOverlay.After) =
                (load, available, present, lead, after);
        }
    });
}
