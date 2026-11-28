using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PromoFontsTests
{
    [Fact]
    public Task PromoFacesResolveAtTheirRealWeights() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        foreach (var (name, family, weight) in new[] {
            ("Fredoka", AppFonts.PromoRound, FontWeight.Medium),
            ("Fredoka", AppFonts.PromoRound, FontWeight.SemiBold),
            ("Courier Prime", AppFonts.PromoMono, FontWeight.Bold) })
        {
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(family), FontStyle.Normal, weight), out var face));
            Assert.StartsWith(name, face!.FamilyName);
            Assert.Equal(weight, face.Weight);
            Assert.Equal(FontSimulations.None, face.FontSimulations);
        }
        return Task.CompletedTask;
    });
}
