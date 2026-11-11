using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PlayWebAppCardTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GamesCardHasWpfOrderAndBundledArt(bool checkOrder)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory();
            var png = Path.Combine(dir.FullName, "override.png");
            using (var bitmap = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96)))
                bitmap.Save(png);
            var previous = CoreModArt.OverridePathProvider;
            CoreModArt.OverridePathProvider = path => path == "billboard/webapp.png" ? png : null;
            Window? window = null;
            try
            {
                var play = new PlayTabView();
                window = new Window { Content = play, Width = 1100, Height = 780 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                if (checkOrder)
                {
                    var slot = play.FindControl<Grid>("SlotWebApp")!;
                    var games = Assert.IsType<UniformGrid>(slot.Parent);
                    Assert.Equal(new[] { "SlotBreakoutDemo", "SlotBreakout", "SlotGoon", "SlotPieceByPiece", "SlotDtrh", "SlotWebApp" },
                        games.Children.Select(child => child.Name));
                    return Task.CompletedTask;
                }
                var plate = play.FindControl<Border>("PlayWebAppHeroPlate")!;
                var brush = Assert.IsType<ImageBrush>(plate.Background);
                Assert.Equal(new PixelSize(688, 384), Assert.IsType<Bitmap>(brush.Source).PixelSize);
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                Dispatcher.UIThread.RunJobs();
                brush = Assert.IsType<ImageBrush>(plate.Background);
                Assert.Equal(new PixelSize(688, 384), Assert.IsType<Bitmap>(brush.Source).PixelSize);
            }
            finally
            {
                window?.Close();
                CoreModArt.OverridePathProvider = previous;
                dir.Delete(true);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CardLocalizesAndOpensWebAppFromKeyboard()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var loc = LocalizationManager.Instance;
            var language = loc.CurrentLanguage;
            var seen = CoreSettings.Current.SeenFeatureIntros;
            bool hadKey = seen.Remove(MainShellWindow.WebBannerSeenKey);
            var previous = MainShellWindow.OpenPlayWebAppLink;
            string? opened = null;
            MainShellWindow.OpenPlayWebAppLink = (_, url) => { opened = url; return Task.FromResult(true); };
            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow();
                w.Show();
                w.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                Assert.Null(w.Named<Button>("DoorWebApp"));
                var play = w.Named<PlayTabView>("PlayTab")!;
                var title = play.FindControl<TextBlock>("TxtNavWebAppLabel")!;
                var blurb = play.FindControl<TextBlock>("TxtPlayWebAppBlurb")!;
                loc.SetLanguage("it");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(loc.Get("nav_door_webapp"), title.Text);
                Assert.Equal(loc.Get("play_card_webapp_blurb"), blurb.Text);
                var button = play.FindControl<Button>("BtnPlayWebApp")!;
                Assert.True(button.Focus());
                w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("https://app.cclabs.app", opened);
                Assert.Contains(MainShellWindow.WebBannerSeenKey, seen);
            }
            finally
            {
                w?.Close();
                MainShellWindow.OpenPlayWebAppLink = previous;
                if (!hadKey) seen.Remove(MainShellWindow.WebBannerSeenKey);
                else if (!seen.Contains(MainShellWindow.WebBannerSeenKey)) seen.Add(MainShellWindow.WebBannerSeenKey);
                loc.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }
}
