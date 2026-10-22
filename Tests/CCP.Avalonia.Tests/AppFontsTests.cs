using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AppFontsTests
{
    [Fact]
    public Task SharedMonoResourceMatchesWpfApplication() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApplication();
        var app = Application.Current!;
        Assert.True(app.TryFindResource("Font.Mono", out var value));
        Assert.Equal(new[] { "Cascadia Mono", "Consolas", "Courier New" }, Assert.IsType<FontFamily>(value).FamilyNames);
        // A real consumer: the exception detail of the update-failed dialog (WPF UpdateFailedDialog.xaml).
        var window = new UpdateFailedDialog("t", "m", "System.IO.IOException: detail");
        var label = window.FindControl<TextBlock>("TxtDetail")!;
        // Second consumer: the Companion context-wire chip (WPF CompanionTheme.xaml:1280).
        var companion = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://CCP.Avalonia/Views/Controls/Companion/Themes/CompanionTheme.axaml"));
        var wire = new TextBlock { Theme = (ControlTheme)companion["CmpWireTextStyle"]! };
        var wireHost = new Window { Content = wire };
        try
        {
            // Attach to a resource host without measuring text through another suite's font setup.
            window.ApplyStyling();
            label.ApplyStyling();
            wireHost.ApplyStyling();
            wire.ApplyStyling();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(value, label.FontFamily);
            Assert.Equal(value, wire.FontFamily);
            var replacement = new FontFamily(AppFonts.Mono);
            app.Resources["Font.Mono"] = replacement;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(replacement, label.FontFamily);
            Assert.Equal(replacement, wire.FontFamily);
        }
        finally
        {
            window.Close();
            wireHost.Close();
            app.Resources["Font.Mono"] = value;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EveryWpfMonoFamilyResolvesToBundledFixedWidthFaceOffWindows() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApplication();
        if (OperatingSystem.IsWindows()) return Task.CompletedTask;
        // The names a consumer actually writes: the shared composite chain and each link. Avalonia
        // walks a composite's FamilyNames in order and applies FontFamilyMappings per name, so the
        // chain's FIRST link must be mapped or an installed face could win before the bundled one.
        var mappings = AppFonts.Options(systemEmoji: false).FontFamilyMappings!;
        // Other suites in the same testhost build the app without AppFonts.Options; only resolve the
        // raw names through the live FontManager when it carries our options (Inter default).
        var live = FontManager.Current.DefaultFontFamily.Name.Contains("Inter");
        foreach (var name in new[] { "Cascadia Mono, Consolas, Courier New", "Cascadia Mono", "Consolas", "Courier New", "monospace" })
        {
            var first = new FontFamily(name).FamilyNames[0];
            Assert.True(mappings.TryGetValue(first, out var mapped), name);
            var typeface = new Typeface(live ? new FontFamily(name) : mapped!);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs), name);
            Assert.True(glyphs!.FamilyName == "Noto Sans Mono", $"{name} -> {glyphs.FamilyName}");
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
