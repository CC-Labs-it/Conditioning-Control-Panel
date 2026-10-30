using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrainDrainRestackTests
{
    [Fact]
    public Task UnsupportedBackendDoesNotClaimToHaveSunkTheBlur() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var drain = new Window();
        var picture = new Window();
        try
        {
            drain.Show();
            picture.Show();
            Assert.False(X11Overlay.RestackBelow(drain, picture));
        }
        finally { picture.Close(); drain.Close(); }
        return Task.CompletedTask;
    });
}
