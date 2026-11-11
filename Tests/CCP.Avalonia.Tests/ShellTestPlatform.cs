using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia.Platform;

namespace CCP.Avalonia.Tests;

/// <summary>The lane and vault tests use the same bundled fonts regardless of which class starts Avalonia.</summary>
internal static class ShellTestPlatform
{
    internal static void Ensure()
    {
        if (Application.Current == null)
        {
            var fonts = AppFonts.Options(systemEmoji: false);
            ((System.Collections.Generic.Dictionary<string, global::Avalonia.Media.FontFamily>)fonts.FontFamilyMappings!)["Segoe UI Black"] = new global::Avalonia.Media.FontFamily(AppFonts.Inter);
            ((System.Collections.Generic.Dictionary<string, global::Avalonia.Media.FontFamily>)fonts.FontFamilyMappings!)["Segoe UI"] = new global::Avalonia.Media.FontFamily(AppFonts.Inter);
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(fonts).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
    }

}
