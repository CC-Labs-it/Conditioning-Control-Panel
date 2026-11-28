using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>ccp-bugs#1628 guard: a rail coin press that drifts a few px before release still opens its
/// section (WPF ButtonBase clicks on any release inside the button), and never starts a window move/resize.</summary>
public sealed class NavRailClickMoveTests
{
    private static void Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
        {
            var fonts = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        }
        var old = CoreSettings.Current.NavLastTabBySection;
        CoreSettings.Current.NavLastTabBySection = null;
        var w = new MainShellWindow();
        try { w.Show(); Dispatcher.UIThread.RunJobs(); body(w); }
        finally
        {
            w.Close(); CoreSettings.Current.NavLastTabBySection = old;
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void PressMoveReleaseOnACoinOpensItsSectionAndNeverDragsTheWindow() => Run(w =>
    {
        // BeginMoveDrag is reached only through the title bar's PointerPressed (WindowChrome.cs:146).
        bool titleBarPressed = false;
        w.Named<Border>("TitleBarBorder")!.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => titleBarPressed = true, RoutingStrategies.Bubble, handledEventsToo: true);
        var fails = new List<string>();
        var doors = new[] { ("DoorCompanion", "companion"), ("DoorPlay", "play") };
        int n = 0;
        // Centre and the coin's right edge (the clip's spot); hover first so the coin is leaning.
        foreach (var (fx, mx, my) in new[] { (0.5, 1, 0), (0.5, 0, 3), (0.5, -6, -6), (0.75, 6, 0), (0.75, 3, 3), (0.75, 0, -6) })
        {
            var (name, section) = doors[n++ % 2];
            var b = w.Named<Button>(name)!;
            var at = b.TranslatePoint(new Point(b.Bounds.Width * fx, 28), w)!.Value;
            w.MouseMove(at - new Point(4, 4)); w.MouseMove(at); Dispatcher.UIThread.RunJobs();
            w.MouseDown(at, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            for (int i = 1; i <= 3; i++) { w.MouseMove(at + new Point(mx * i / 3.0, my * i / 3.0)); Dispatcher.UIThread.RunJobs(); }
            w.MouseUp(at + new Point(mx, my), MouseButton.Left); Dispatcher.UIThread.RunJobs();
            if (NavSections.SectionForTab(w.CurrentTab) != section) fails.Add($"{name} fx={fx} move=({mx},{my}) tab={w.CurrentTab}");
        }
        Assert.Empty(fails);
        Assert.False(titleBarPressed);
        Assert.Null(w.LastResizeEdge);
    });
}
