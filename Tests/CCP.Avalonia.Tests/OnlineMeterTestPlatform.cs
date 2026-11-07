using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;

namespace CCP.Avalonia.Tests;

internal static class OnlineMeterTestPlatform
{
    internal static void EnsureApplication()
    {
        if (Application.Current != null) return;
        // FontManager is fixed by the first test to create the app. Use the head's bundled
        // fonts even when the ring test runs first; the shell also builds PremiumSpark's label.
        var fonts = AppFonts.Options(systemEmoji: false);
        var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
        mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(AppFonts.Inter);
        fonts.FontFamilyMappings = mappings;
        global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
