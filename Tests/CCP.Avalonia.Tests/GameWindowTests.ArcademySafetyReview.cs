using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Fyp;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed partial class GameWindowTests
{
    [Fact]
    public void Arcademy_BootPanicCannotBeLost() => Run((_, _) =>
    {
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        try { w.Show(); Assert.False(w.IsReady); Assert.True(GameWindow.HandleArcademyPanic(DateTime.Now)); Assert.Null(GameWindow.OpenForTests("arcademy")); }
        finally { w.Close(); }
    });
    [Fact]
    public void Arcademy_OtherWindowCloseKeepsBothSyncStores() => Run((_, _) =>
    {
        var w = new GameWindow(GameWindow.Games["arcademy"]); var other = new GameWindow(GameWindow.Games["breakoutdemo"]);
        var cards = typeof(ArcademySyncService).GetField("_store", BindingFlags.NonPublic | BindingFlags.Static)!;
        var wallet = typeof(ArcademyWalletSyncService).GetField("_store", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            w.Show(); other.Show(); var expected = cards.GetValue(null);
            Assert.NotNull(expected); Assert.Same(expected, wallet.GetValue(null));
            other.Close(); Assert.Same(expected, cards.GetValue(null)); Assert.Same(expected, wallet.GetValue(null));
        }
        finally { other.Close(); w.Close(); }
    });
    [Fact]
    public void Arcademy_CounterClosesBeforePanicHandoff() => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current; var old = (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll);
        var w = new GameWindow(GameWindow.Games["arcademy"]); string? reason = null;
        try
        {
            (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = ("F8", true, false);
            launcher.Hide(); w.Show(); w.HandleMessage("{\"type\":\"ready\"}");
            var counter = GameWindow.LaunchPrizeCounter(onClosed: r => reason = r);
            Assert.NotNull(counter); shell.HandlePanicKeyPress(DateTime.Now);
            Assert.Null(GameWindow.OpenForTests(GameWindow.PrizeCounterId));
            Assert.Equal("panic", reason); Assert.Same(w, GameWindow.OpenForTests("arcademy"));
        }
        finally { w.Close(); (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = old; }
    });
}
