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
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;
public class Sync7bTests
{
    private static void Setup()
    {
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Fact]
    public Task DrawerHandleAndNavigationResetItsPushWidth() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var drawer = new PageFavoritesDrawer();
        drawer.OnNavigated(ConditioningControlPanel.Services.UI.NavSections.Studio);
        Assert.True(drawer.IsVisible);
        drawer.Handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(drawer.IsOpen); Assert.Equal(92, drawer.Body.Width);
        drawer.OnNavigated(ConditioningControlPanel.Services.UI.NavSections.Studio);
        Assert.False(drawer.IsOpen); Assert.Equal(0, drawer.Body.Width);
        drawer.OnNavigated(ConditioningControlPanel.Services.UI.NavSections.Home);
        Assert.False(drawer.IsVisible);
        return Task.CompletedTask;
    });
    [Fact]
    public Task DrawerHandleFillsEdgeAndKeepsReferenceTypographyAndInk() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var drawer = new PageFavoritesDrawer();
        var labels = drawer.GetLogicalDescendants().OfType<TextBlock>().ToArray();
        foreach (var label in labels) label.FontFamily = new FontFamily("DejaVu Sans");
        drawer.Resources["SecondaryBrush"] = Brushes.Red; drawer.Resources["TextMutedBrush"] = Brushes.Blue;
        var host = new Window { Content = drawer, Width = 22, Height = 400 };
        try
        {
            host.Show(); drawer.Measure(new Size(22,400)); drawer.Arrange(new Rect(0,0,22,400));
            Assert.Equal(new Size(18,390), drawer.Handle.Bounds.Size);
            var presenter = Assert.IsType<global::Avalonia.Controls.Presenters.ContentPresenter>(Assert.Single(drawer.Handle.GetVisualChildren()));
            Assert.Equal(drawer.Handle.Bounds.Size, Assert.IsType<Border>(presenter.Content).Bounds.Size);
            Assert.Equal(8, drawer.FavoritesEmpty.FontSize); Assert.Equal(0.85, drawer.FavoritesEmpty.Opacity); Assert.Same(Brushes.Blue, drawer.FavoritesEmpty.Foreground);
            Assert.Equal(new Thickness(0,0,0,6), drawer.FavoritesEmpty.Margin);
            var caption = labels.Single(t => t.Text == ConditioningControlPanel.Localization.Loc.Get("rail_recent_caption"));
            Assert.Equal(9, caption.FontSize); Assert.Equal(FontWeight.Bold, caption.FontWeight); Assert.Same(Brushes.Red, caption.Foreground);
            Assert.Equal(new Thickness(0,8,0,6), caption.Margin);
            var hue = Color.FromRgb(100,80,160); drawer.Paint(hue);
            Assert.Equal(ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.Lighten(hue,0.18), Assert.IsType<SolidColorBrush>(drawer.Chevron.Foreground).Color);
            host.MouseMove(new Point(10,20));
            Assert.True(drawer.Handle.IsPointerOver);
            Assert.Equal(ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.Lighten(hue,0.55), Assert.IsType<SolidColorBrush>(drawer.Chevron.Foreground).Color);
            host.MouseMove(new Point(110,20));
            Assert.True(drawer.Handle.Focus()); Assert.Null(drawer.Handle.FocusAdorner);
            Assert.Equal(ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.Lighten(hue,0.55), Assert.IsType<SolidColorBrush>(drawer.Chevron.Foreground).Color);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task DrawerSharesPinsPushesPageAndClosesOnNavigation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.RailFavorites.Clear();
        var w = new MainShellWindow();
        try
        {
            w.ShowTab("haptics");
            var d = w.PageDrawer!;
            Assert.True(d.IsVisible);
            w.TogglePinned("tab.haptics");
            Assert.True(d.IsOpen);
            Assert.Equal("tab.haptics", Assert.Single(d.FavoritesList.Children).Tag);
            Assert.Empty(d.RecentList.Children.Where(c => Equals(c.Tag, "tab.haptics")));
            Assert.Equal(92, d.Body.Width);
            Assert.Equal(1, Grid.GetColumn(d));
            Assert.Equal(GridLength.Auto, ((Grid)d.Parent!).ColumnDefinitions[1].Width);
            w.ShowTab("haptics");
            Assert.False(d.IsOpen);
            Assert.Equal(0, d.Body.Width);
            d.Handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(d.IsOpen);
            w.ShowTab("settings");
            Assert.False(d.IsVisible);
            Assert.False(d.IsOpen);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task VoiceSliderSeedsRestoresAndControlsSpokenGain() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.CompanionVoiceLevel = 40;
        var cell = new WorkshopBehaviorCell();
        var slider = cell.FindControl<Slider>("SliderVoiceLevelCompanion")!;
        Assert.Equal(40, slider.Value);
        slider.Value = 100;
        Assert.Equal(100, CoreSettings.Current.CompanionVoiceLevel);
        Assert.Equal("100", cell.FindControl<TextBlock>("TxtVoiceLevelCompanion")!.Text);
        CoreSettings.Current.CompanionVoiceLevel = 20; cell.SyncFromSettings();
        Assert.Equal(20, slider.Value);
        var path = Path.GetTempFileName();
        var old = CoreAudio.PlayStoppableProvider;
        var w = new AvatarTubeWindow();
        float gain = -1;
        CoreAudio.PlayStoppableProvider = (_, v, _, _, _) => { gain = v; return () => { }; };
        try
        {
            CoreSettings.Current.MasterVolume = 50;
            CoreSettings.Current.CompanionVoiceLinesMuted = false;
            var play = typeof(AvatarTubeWindow).GetMethod("PlaySpeechAudio", BindingFlags.Instance | BindingFlags.NonPublic)!;
            play.Invoke(w, new object?[] { false, path, true, null });
            Assert.Equal(ConditioningControlPanel.Services.CompanionVoiceGain.Compute(50,20,0.85f), gain);
            slider.Value = 0; gain = -1;
            play.Invoke(w, new object?[] { false, path, true, null });
            Assert.Equal(-1, gain);
        }
        finally { w.Close(); CoreAudio.PlayStoppableProvider = old; File.Delete(path); }
        return Task.CompletedTask;
    });
    private sealed class Drone : LayeredAudio.ILayerPlayer
    {
        public int Last; public bool Disposed;
        public int Volume { set => Last = value; }
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public Task MantraMasterChangesDroneAndToneAndUnhooks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var opener = MantraWindow.DroneOpener;
        var play = CoreAudio.PlayOneShotProvider;
        var drone = new Drone();
        MantraWindow.DroneOpener = _ => drone;
        var w = new MantraWindow();
        void Call(string name) => typeof(MantraWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w,null);
        try
        {
            CoreSettings.Current.MasterVolume = 100; Call("StartDrone");
            Assert.True(drone.Last > 0);
            CoreSettings.Current.MasterVolume = 0; Assert.Equal(0,drone.Last);
            CoreSettings.Current.MasterVolume = 25;
            Assert.Equal(ToneWav.VlcVolume(0.05 * 0.25 * ToneWav.DronePeak), drone.Last);
            float gain = -1;
            CoreAudio.PlayOneShotProvider = (_, v, _, _, _) => gain = v;
            typeof(MantraWindow).GetMethod("PlayTone", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,new object[] { 400.0, 150 });
            Assert.Equal(0.15f * 0.25f,gain);
            Call("StopDrone"); Assert.True(drone.Disposed);
            var last = drone.Last; CoreSettings.Current.MasterVolume = 100; Assert.Equal(last,drone.Last);
        }
        finally { Call("Cleanup"); w.Close(); MantraWindow.DroneOpener = opener; CoreAudio.PlayOneShotProvider = play; }
        return Task.CompletedTask;
    });
}
