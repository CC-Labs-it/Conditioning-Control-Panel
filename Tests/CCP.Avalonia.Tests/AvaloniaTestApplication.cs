using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using Xunit;

[assembly: AssemblyFixture(typeof(CCP.Avalonia.Tests.AvaloniaTestApplication))]

namespace CCP.Avalonia.Tests;

// All tests share Application.Current. Whichever class ran first used to choose the font
// bootstrap for the entire shard, so isolated runs could miss the bundled Inter collection.
public sealed class AvaloniaTestApplication
{
    public AvaloniaTestApplication() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    });
}
