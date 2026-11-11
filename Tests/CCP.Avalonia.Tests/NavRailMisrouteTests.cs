using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NavRailMisrouteTests
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
            MainShellWindow.PossessionReroute = null;
            w.Close(); CoreSettings.Current.NavLastTabBySection = old;
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static void Press(MainShellWindow w, string name)
    {
        var b = w.Named<Button>(name)!;
        var at = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void HookCanRerouteOnePress() => Run(w =>
    {
        MainShellWindow.PossessionReroute = from => { MainShellWindow.PossessionReroute = null; return "social"; };
        Press(w, "DoorStudio");
        Assert.Equal("availablesubjects", w.CurrentTab);
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnavailableDestinationLeavesPressAlone(bool hidden) => Run(w =>
    {
        var destination = w.Named<Button>("DoorSocial")!;
        if (hidden) destination.IsVisible = false; else destination.IsEnabled = false;
        MainShellWindow.PossessionReroute = _ => "social";
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
    });

    [Fact]
    public void ThrowingHookLeavesPressAlone() => Run(w =>
    {
        MainShellWindow.PossessionReroute = _ => throw new InvalidOperationException();
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
    });

    [Fact]
    public void WebAppIsNeverADestination() => Run(w =>
    {
        // Even if a launcher were offered as a rail row, it cannot receive the reroute.
        w.Named<Button>("DoorSocial")!.Tag = "webapp";
        MainShellWindow.PossessionReroute = _ => "webapp";
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
    });
}
