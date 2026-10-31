using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.LogicalTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BillboardCtaPressTests
{
    [Fact]
    public Task Press_sinks_face_release_restores_and_motion_off_stays_still() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.MotionLevel = MotionLevel.Full;
        service.Current.PerformanceMode = false;
        var deck = new BillboardDeck(() => new IBillboardProvider[] { new HouseProvider() },
            () => new BillboardContext(BillboardTier.Free, System.DateTime.UtcNow, System.DateTime.Now),
            new Dictionary<string, System.DateTime>(), () => { });
        var host = new BillboardCardHost(deck) { Width = 640, Height = 300 };
        var window = new Window { Content = host, Width = 640, Height = 300 };
        try
        {
            host.Begin();
            // Press travel is independent of the Windows font families on the card.
            foreach (var text in host.GetLogicalDescendants().OfType<TextBlock>()) text.FontFamily = new FontFamily("DejaVu Sans");
            window.Show(); Dispatcher.UIThread.RunJobs();
            var face = (Border)host.Cta.Content!;
            var point = host.Cta.TranslatePoint(new Point(20, 10), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            Assert.True(host.Cta.IsPressed);
            Assert.Equal(2, Assert.IsType<TranslateTransform>(face.RenderTransform).Y);
            service.Current.MotionLevel = MotionLevel.Off; host.RefreshMotion();
            Assert.Equal(0, Assert.IsType<TranslateTransform>(face.RenderTransform).Y);
            window.MouseUp(point, MouseButton.Left);
            service.Current.MotionLevel = MotionLevel.Full; host.RefreshMotion();
            Assert.False(host.Cta.IsPressed);
            Assert.Equal(0, Assert.IsType<TranslateTransform>(face.RenderTransform).Y);
        }
        finally { window.Close(); host.ReleaseArt(); }
        return Task.CompletedTask;
    });
}
