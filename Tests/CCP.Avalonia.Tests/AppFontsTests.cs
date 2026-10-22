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
        Assert.True(Application.Current!.TryFindResource("Font.Mono", out var value));
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
            Application.Current.Resources["Font.Mono"] = replacement;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(replacement, label.FontFamily);
            Assert.Equal(replacement, wire.FontFamily);
        }
        finally
        {
            window.Close();
            wireHost.Close();
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
