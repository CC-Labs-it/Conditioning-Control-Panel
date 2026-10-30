using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Billboard;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
namespace CCP.Avalonia.Tests;
public sealed class PosterArtViewTests
{
    [Fact]
    public Task MotionGatesHoldAndReleaseThePoster() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        var deck = new BillboardDeck(() => new[] { new PosterProvider() },
            () => new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now));
        var host = new BillboardCardHost(deck);
        var window = new Window { Content = host, Width = 800, Height = 450 };
        try
        {
            window.Show();
            host.Begin();
            window.UpdateLayout();
            var view = Assert.Single(host.GetVisualDescendants().OfType<PosterArtView>());
            view.Run();
            Assert.False(view.IsAnimating);
            var still = view.CurrentMatrix;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            CoreSettings.Current.PerformanceMode = false;
            view.Run();
            Assert.True(view.IsAnimating);
            view.Step(.1, true, new Point(1, 0));
            Assert.NotEqual(still, view.CurrentMatrix);
            view.Hold();
            Assert.False(view.IsAnimating);
            view.Run();
            Assert.True(view.IsAnimating);
            host.ReleaseArt();
            view.Run();
            Assert.False(view.IsAnimating);
        }
        finally { host.ReleaseArt(); window.Close(); }
        return Task.CompletedTask;
    });
    private sealed class PosterProvider : IBillboardProvider
    {
        public string Id => "poster-test";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => new[]
        {
            new BillboardCardSpec("poster-test", BillboardCardKind.House, 0, "", "Poster", "", "#ff4fa8",
                "poster", "", BillboardAction.None),
        };
        public void Invoke(string target) { }
        public event EventHandler? Changed { add { } remove { } }
    }
}
