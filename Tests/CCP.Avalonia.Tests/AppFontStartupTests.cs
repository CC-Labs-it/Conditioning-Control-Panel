using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AppFontStartupTests
{
    // Deliberately no per-test AppBuilder: every shard must start with the app's fonts,
    // even when its first test does not know about the production builder.
    [Fact]
    public async Task Shared_startup_registers_bundled_normal_and_bold_fonts_before_any_view()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Assert.NotNull(Application.Current);
            foreach (var weight in new[] { FontWeight.Normal, FontWeight.Bold })
            {
                var face = new Typeface(AppFonts.Inter, FontStyle.Normal, weight).GlyphTypeface;
                Assert.NotNull(face);
                Assert.NotEqual((ushort)0, face.CharacterToGlyphMap['A']);
                var text = new TextBlock { Text = "FALL IN", FontFamily = new FontFamily(AppFonts.Inter), FontWeight = weight };
                text.Measure(Size.Infinity);
                Assert.True(text.DesiredSize.Width > 0);
                Assert.True(text.DesiredSize.Height > 0);
            }
            return Task.CompletedTask;
        });
    }
}
