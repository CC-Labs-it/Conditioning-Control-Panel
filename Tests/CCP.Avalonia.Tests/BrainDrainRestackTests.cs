using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class BrainDrainRestackTests
{
    [Fact] public void KwinProbeReadsTheRootProperty() { var expected = System.Environment.GetEnvironmentVariable("CCP_TEST_KWIN_BLUR"); if (expected != null || string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DISPLAY"))) Assert.Equal(expected == "1", X11Overlay.HasKwinBlur()); }
    [Fact]
    public Task UnsupportedBackendDoesNotClaimToHaveSunkTheBlur() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        // Collections run in parallel and the first bootstrap owns process fonts; when it is this one, bundled Inter must stay.
        if (Application.Current is null)
        {
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(AppFonts.Inter), FontStyle.Normal, FontWeight.Bold), out _));
        }
        var drain = new Window(); var picture = new Window();
        try
        {
            drain.Show(); picture.Show();
            Assert.False(X11Overlay.RestackBelow(drain, picture));
        }
        finally { picture.Close(); drain.Close(); }
        return Task.CompletedTask;
    });
}
