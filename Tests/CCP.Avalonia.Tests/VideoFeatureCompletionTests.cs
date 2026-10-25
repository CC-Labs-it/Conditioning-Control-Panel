using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VideoFeatureCompletionTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task Art_loads_both_plates_repaints_on_mod_switch_and_unhooks()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var card = new VideoFeatureControl();
            var hero = card.FindControl<Border>("VideoHeroArt")!;
            var side = card.FindControl<Border>("VideoSideArt")!;
            Assert.NotNull(hero);
            Assert.NotNull(side);
            var heroWash = hero.Background;
            var sideWash = side.Background;
            Assert.IsNotType<ImageBrush>(heroWash); // painted on attach, not in the constructor
            Assert.NotNull(heroWash);
            var host = new Window { Content = card };
            host.Show();
            try
            {
                var first = Assert.IsType<ImageBrush>(hero.Background);
                Assert.NotNull(first.Source);
                Assert.Equal(AlignmentX.Right, first.AlignmentX);
                Assert.Equal(0.9, first.Opacity);
                Assert.Equal(Stretch.UniformToFill, first.Stretch);
                Assert.NotNull(Assert.IsType<ImageBrush>(side.Background).Source);
                var previousResolver = CoreModArt.OverridePathProvider;
                var requested = new System.Collections.Generic.List<string>();
                try
                {
                    CoreModArt.OverridePathProvider = path => { requested.Add(path); return null; };
                    CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(new[] { "features/mandatory_videos.png", "features/mandatory_videos.png" }, requested);
                    Assert.NotSame(first, hero.Background);
                }
                finally { CoreModArt.OverridePathProvider = previousResolver; }
                host.Close();
                Assert.Same(heroWash, hero.Background); // detach puts the authored wash back
                Assert.Same(sideWash, side.Background);
                var afterDetach = 0;
                try
                {
                    CoreModArt.OverridePathProvider = _ => { afterDetach++; return null; };
                    CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                    Dispatcher.UIThread.RunJobs();
                }
                finally { CoreModArt.OverridePathProvider = previousResolver; }
                Assert.Equal(0, afterDetach); // unhooked: a mod switch no longer resolves art
                Assert.Same(heroWash, hero.Background);
            }
            finally { host.Close(); }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Test_failure_shows_Wpf_error_copy_and_closes_on_OK_or_panic(bool panic)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var previous = CoreEngine.Video;
            var card = new VideoFeatureControl();
            var host = new Window { Content = card };
            host.Show();
            CoreEngine.Video = new MandatoryVideoScheduler(MandatoryVideoOverlay.Instance,
                library: () => throw new InvalidOperationException("test library failure"));
            try
            {
                card.FindControl<Button>("BtnTestVideo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.Single(host.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal("Error", dialog.Title);
                Assert.Equal("Error triggering video: test library failure", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                if (panic)
                    ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(s => s.Id == "video-test-error").Stop(null);
                else dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await card.TestVideoRun;
            }
            finally
            {
                foreach (var w in host.OwnedWindows.ToArray()) w.Close();
                host.Close();
                CoreEngine.Video = previous;
            }
        });
    }
}
