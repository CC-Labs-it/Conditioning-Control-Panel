using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>render-all-segv (CI exit 139): bold/italic emoji must never reach Avalonia's synthetic-typeface path
/// (SkiaTypeface.TryGetStream reads a stream it does not keep alive; a GC mid-copy SIGSEGVs).</summary>
public sealed class SystemEmojiFontCollectionTests
{
    [Fact]
    public void EmojiFallbackIsNotTheBareSystemFamily() =>
        Assert.DoesNotContain(AppFonts.Options().FontFallbacks!, f => f.FontFamily.Name == "emoji"
            && f.FontFamily.Key is null);

    /// <summary>The real app builder (Program.BuildAvaloniaApp -> ConfigureFonts) registers the collection the emoji
    /// fallback names: without AppFonts.Register the first fallback resolves to nothing.</summary>
    [Fact]
    public Task AppBuilderRegistersTheSystemEmojiCollection() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var name = SystemEmojiFontCollection.SystemFamilyName();
        Assert.SkipWhen(name is null, "no system colour emoji font on this machine");
        FontManager.Current.RemoveFontCollection(SystemEmojiFontCollection.CollectionKey);
        try
        {
            var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
            builder.AfterSetupCallback(builder);   // runs the ConfigureFonts actions against FontManager.Current
            var fallback = AppFonts.Options().FontFallbacks![0].FontFamily;
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(fallback), out var glyphs));
            Assert.Equal(name, glyphs!.FamilyName);
            Assert.True(glyphs.CharacterToGlyphMap.ContainsGlyph(0x1F600));
        }
        finally { FontManager.Current.RemoveFontCollection(SystemEmojiFontCollection.CollectionKey); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BoldItalicEmojiResolveToTheRegularFaceWithoutSynthesis() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        // The bundled Noto Emoji stands in for the system font: same collection, deterministic on every machine.
        var fonts = new SystemEmojiFontCollection(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/fonts/NotoEmoji-wght.ttf")));
        FontManager.Current.AddFontCollection(fonts);
        try
        {
            var family = new FontFamily(SystemEmojiFontCollection.CollectionKey + "#Noto Emoji");
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family, FontStyle.Italic, FontWeight.Bold), out var glyphs));
            Assert.Equal("Noto Emoji", glyphs!.FamilyName);
            Assert.Equal(FontSimulations.None, glyphs.FontSimulations);
            Assert.True(fonts.RefusedSyntheses > 0);
        }
        finally { FontManager.Current.RemoveFontCollection(SystemEmojiFontCollection.CollectionKey); }
        return Task.CompletedTask;
    });
}
