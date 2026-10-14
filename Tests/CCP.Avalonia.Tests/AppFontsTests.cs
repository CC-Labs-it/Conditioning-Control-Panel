using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AppFontsTests
{
    [Fact]
    public Task SharedMonoResourceMatchesWpfApplication() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApplication();
        Assert.True(Application.Current!.TryFindResource("Font.Mono", out var value));
        Assert.Equal(new[] { "Cascadia Mono", "Consolas", "Courier New" }, Assert.IsType<FontFamily>(value).FamilyNames);
        var label = new TextBlock { [!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("Font.Mono") };
        var window = new Window { Content = label };
        try
        {
            // Attach to a resource host without measuring text through another suite's font setup.
            window.ApplyStyling();
            label.ApplyStyling();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(value, label.FontFamily);
            var replacement = new FontFamily(AppFonts.Mono);
            Application.Current.Resources["Font.Mono"] = replacement;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(replacement, label.FontFamily);
        }
        finally
        {
            window.Close();
            Application.Current.Resources["Font.Mono"] = value;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EveryWpfMonoFamilyResolvesToBundledFixedWidthFaceOffWindows() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApplication();
        if (OperatingSystem.IsWindows()) return Task.CompletedTask;
        var mappings = AppFonts.Options(systemEmoji: false).FontFamilyMappings!;
        foreach (var name in new[] { "Cascadia Mono", "Consolas", "Courier New", "monospace" })
        {
            Assert.True(mappings.TryGetValue(name, out var family), name);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family!), out var glyphs));
            Assert.Equal("Noto Sans Mono", glyphs!.FamilyName);
            Assert.True(glyphs.CharacterToGlyphMap.TryGetGlyph('i', out var narrow));
            Assert.True(glyphs.CharacterToGlyphMap.TryGetGlyph('W', out var wide));
            Assert.True(glyphs.TryGetHorizontalGlyphAdvance(narrow, out var narrowAdvance));
            Assert.True(glyphs.TryGetHorizontalGlyphAdvance(wide, out var wideAdvance));
            Assert.True(narrowAdvance > 0);
            Assert.Equal(narrowAdvance, wideAdvance);
        }
        return Task.CompletedTask;
    });

    private static void EnsureApplication()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
