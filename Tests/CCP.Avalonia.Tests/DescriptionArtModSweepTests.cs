using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public class DescriptionArtModSweepTests
{
    [Fact]
    public void AwarenessPlatesFollowModAndRestoreShippedArt() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = CoreModArt.OverridePathProvider;
        Window? host = null;
        var scratch = Directory.CreateTempSubdirectory("ccp-awareness-art-");
        var path = Path.Combine(scratch.FullName, "art.png");
        using (var bitmap = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96))) bitmap.Save(path);
        try
        {
            CoreModArt.OverridePathProvider = name => name == "features/awareness.png" ? path : null;
            var view = new AwarenessTabView();
            host = new Window { Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var carrier = view.FindControl<Image>("ImgAwarenessFeature")!;
            var hero = view.FindControl<Border>("AwarenessHeroArt")!;
            var side = view.FindControl<Border>("AwarenessSideArt")!;
            Assert.Same(carrier.Source, ((ImageBrush)hero.Background!).Source);
            Assert.Same(carrier.Source, ((ImageBrush)side.Background!).Source);
            var previous = Assert.IsType<Bitmap>(carrier.Source);
            Assert.Equal(2, previous.Size.AspectRatio);
            CoreModArt.OverridePathProvider = _ => null;
            CoreMods.RaiseModChanged(null, null!);
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(previous, carrier.Source);
            Assert.NotEqual(2, ((Bitmap)carrier.Source!).Size.AspectRatio);
            Assert.Same(carrier.Source, ((ImageBrush)hero.Background!).Source);
            Assert.Same(carrier.Source, ((ImageBrush)side.Background!).Source);
        }
        finally { host?.Close(); CoreModArt.OverridePathProvider = old; scratch.Delete(true); }
    });
}
