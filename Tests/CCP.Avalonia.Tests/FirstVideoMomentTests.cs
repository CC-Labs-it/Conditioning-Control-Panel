using System.Linq;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FirstVideoMomentTests
{
    [Fact]
    public Task FirstVideoOffersMomentAndPanicStopsItsPresentation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var desk = EmiDeskService.Instance.BeginPresentation()!;
        var player = new Decoder();
        var overlay = new MandatoryVideoOverlay(() => player);
        double? minutes = 0;
        int calls = 0;
        overlay.VideoMinutes = () => minutes;
        overlay.FirstVideoMoment = () => { calls++; desk.Say("first video"); };
        try
        {
            desk.EndPresentation();
            overlay.Show("test.mp4", false);
            Assert.Equal(1, calls);
            Assert.True(desk.ChainLive);
            PanicSurfaces.All.Single(s => s.Id == "emi-desk-feedback").Stop(null);
            Assert.False(desk.ChainLive);
            overlay.CloseAll();
            minutes = 1;
            overlay.Show("test.mp4", false);
            Assert.Equal(1, calls);
            overlay.CloseAll();
            minutes = null;
            overlay.Show("test.mp4", false);
            Assert.Equal(1, calls);
        }
        finally { overlay.CloseAll(); desk.ShutDown(); }
        return Task.CompletedTask;
    });

    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 120000;
        public bool IsPlaying => true;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public void SetPause(bool paused) { }
        public void Stop() { }
        public void Dispose() { }
    }
}
