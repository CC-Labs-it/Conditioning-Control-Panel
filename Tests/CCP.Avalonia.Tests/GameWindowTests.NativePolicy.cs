using System;
using System.Linq;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void GameNativePolicy_IsAttachedAndPanicClosesWindow() => Run((_, _) =>
    {
        var window = new GameWindow(new GameWindow.Game("arcademy", "title", "arcademy/index.html"));
        try
        {
            Assert.Equal((Action<IPlatformHandle?>)SpiralWebView2Policy.Attach, window.Web.ConfigureNative);
            window.Show();
            PanicSurfaces.All.Single(p => p.Id == "games").Stop(null!);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });
}
