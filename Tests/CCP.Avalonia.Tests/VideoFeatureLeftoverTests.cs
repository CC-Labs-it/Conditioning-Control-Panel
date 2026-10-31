using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Reflection;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Re-cut wire-feat-video-b: the Video card's mod-aware art plates, the Test button's error
/// dialog (WPF Features/VideoFeatureControl.xaml.cs:533,563-586).</summary>
public sealed class VideoFeatureLeftoverTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task The_hero_and_side_plates_carry_the_feature_art()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var card = new VideoFeatureControl();
            var owner = new Window { Content = card };
            owner.Show();
            try
            {
                var hero = Assert.IsType<ImageBrush>(card.FindControl<Border>("HeroArt")!.Background);
                Assert.NotNull(hero.Source);
                Assert.Equal(0.9, hero.Opacity, 3);
                Assert.Equal(AlignmentX.Right, hero.AlignmentX);
                var side = Assert.IsType<ImageBrush>(card.FindControl<Border>("SideArt")!.Background);
                Assert.NotNull(side.Source);
            }
            finally { owner.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_Test_click_that_throws_tells_the_user()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            ConditioningControlPanel.Localization.LocalizationManager.Instance.SetLanguage("en");
            var old = CoreEngine.Video;
            var scheduler = new MandatoryVideoScheduler(new Host(), library: () => throw new IOException("disk gone"));
            CoreEngine.Video = scheduler;
            var card = new VideoFeatureControl();
            var owner = new Window { Content = card };
            owner.Show();
            try
            {
                card.FindControl<Button>("BtnTestVideo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal("Error", dialog.Title);
                Assert.Equal("Error triggering video: disk gone", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await card.TestVideoRun;
            }
            finally { owner.Close(); CoreEngine.Video = old; }
        });
    }

    [Fact]
    public async Task VideoPlatesRepaintOnModSwitchAndUnsubscribe()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            LocalizationManager.Instance.SetLanguage("en");

            int before = ModChangedCount();
            var card = new VideoFeatureControl();
            var window = new Window { Content = card, Width = 1100, Height = 780 };
            window.Show();
            Assert.Equal(before + 1, ModChangedCount());

            var hero = Assert.IsType<ImageBrush>(card.FindControl<Border>("HeroArt")!.Background);
            var side = Assert.IsType<ImageBrush>(card.FindControl<Border>("SideArt")!.Background);
            Assert.Equal(0.9, hero.Opacity);
            Assert.Equal(AlignmentX.Right, hero.AlignmentX);
            Assert.Equal(Stretch.UniformToFill, side.Stretch);
            Assert.Equal(480, Width(hero)); // WPF hero decode cap
            Assert.NotEqual(240, Height(hero));

            var dir = Directory.CreateTempSubdirectory();
            var png = Path.Combine(dir.FullName, "mod.png");
            using (var bmp = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96)))
                bmp.Save(png);
            var previous = CoreModArt.OverridePathProvider;
            CoreModArt.OverridePathProvider = p => p == "features/mandatory_videos.png" ? png : null;
            try
            {
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(240, Height((ImageBrush)card.FindControl<Border>("HeroArt")!.Background!));
                Assert.Equal(400, Height((ImageBrush)card.FindControl<Border>("SideArt")!.Background!));

                window.Content = null; // off screen: unsubscribed
                Assert.Equal(before, ModChangedCount());
            }
            finally
            {
                CoreModArt.OverridePathProvider = previous;
                window.Close();
                dir.Delete(true);
            }
            return Task.CompletedTask;
        });
    }

    private static int Width(ImageBrush brush) => ((Bitmap)brush.Source!).PixelSize.Width;
    private static int Height(ImageBrush brush) => ((Bitmap)brush.Source!).PixelSize.Height;

    private static int ModChangedCount()
        => (typeof(CoreMods).GetField(nameof(CoreMods.ModChanged), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;

    private sealed class Host : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }
}
