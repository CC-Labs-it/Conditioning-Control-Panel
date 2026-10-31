using System.Linq;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RemoteFlashLifetimeTests
{
    [Fact]
    public Task StopKeepsPoolButPanicInvalidatesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var field = typeof(RemoteFlashSource).GetField("Pool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var pool = (RemoteFlashPool)field.GetValue(null)!;
        try
        {
            RemoteFlashSource.CleanupCache();
            Assert.True(pool.Add(pool.Generation, "pending"));
            FlashOverlay.CloseAll(final: false);
            Assert.Equal(1, RemoteFlashSource.ReadyCount);
            PanicSurfaces.All.Single(s => s.Id == "flash").Stop(null);
            Assert.Equal(0, RemoteFlashSource.ReadyCount);
            Assert.True(pool.Add(pool.Generation, "pending"));
            FlashOverlay.CloseAll();
            Assert.Equal(0, RemoteFlashSource.ReadyCount);
        }
        finally { RemoteFlashSource.CleanupCache(); }
        return Task.CompletedTask;
    });
}
