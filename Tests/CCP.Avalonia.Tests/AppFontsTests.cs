using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AppFontsTests
{
    // Deliberately bypass Program.BuildAvaloniaApp, as the headless hosts and designer do.
    [Fact]
    public Task HeadlessSystemFallbackBuildsTextGeometry() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

        foreach (var family in new[] { "$Default", "Segoe UI Black, Segoe UI", "fonts:Inter#Inter, $Default", "CCP Missing Font, $Default" })
        {
            var typeface = new Typeface(new FontFamily(family), FontStyle.Normal, FontWeight.Black);
            var text = new FormattedText("PREMIUM", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 18, Brushes.Black);
            Assert.True(text.Width > 0, family);
            Assert.True(text.BuildGeometry(default)!.Bounds.Width > 0, family);
        }
        return Task.CompletedTask;
    });
}
