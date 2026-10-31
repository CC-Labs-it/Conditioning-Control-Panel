using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task Art_loads_both_plates_repaints_on_mod_switch_and_unhooks()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var before = ModChangedCount();
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
                Assert.Equal(before + 1, ModChangedCount());
                var first = Assert.IsType<ImageBrush>(hero.Background);
                Assert.Equal(480, ((Bitmap)first.Source!).PixelSize.Width);
                Assert.Equal(AlignmentX.Right, first.AlignmentX);
                Assert.Equal(0.9, first.Opacity);
                Assert.Equal(Stretch.UniformToFill, first.Stretch);
                Assert.Equal(800, ((Bitmap)Assert.IsType<ImageBrush>(side.Background).Source!).PixelSize.Width);
                var previousResolver = CoreModArt.OverridePathProvider;
                using var dir = new TempArt();
                var requested = new System.Collections.Generic.List<string>();
                try
                {
                    CoreModArt.OverridePathProvider = path => { requested.Add(path); return dir.Path; };
                    CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(new[] { "features/mandatory_videos.png", "features/mandatory_videos.png" }, requested);
                    Assert.NotSame(first, hero.Background);
                    Assert.Equal(240, ((Bitmap)((ImageBrush)hero.Background!).Source!).PixelSize.Height);
                    Assert.Equal(400, ((Bitmap)((ImageBrush)side.Background!).Source!).PixelSize.Height);
                }
                finally { CoreModArt.OverridePathProvider = previousResolver; }
                // A mod event already posted when the card leaves must not repaint it.
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                host.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(before, ModChangedCount());
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

    private static int ModChangedCount() =>
        (typeof(CoreMods).GetField(nameof(CoreMods.ModChanged), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;

    private sealed class TempArt : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory().FullName;
        public string Path => System.IO.Path.Combine(_directory, "override.png");
        public TempArt()
        {
            using var bmp = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96));
            bmp.Save(Path);
        }
        public void Dispose() => Directory.Delete(_directory, true);
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
                Assert.False(dialog.IsVisible);
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
