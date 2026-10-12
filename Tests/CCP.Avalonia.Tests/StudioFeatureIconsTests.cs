using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;
using IconVariant = FluentIcons.Common.IconVariant;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Fluent icons lane L2 (decision 2026-10-10): the Studio rack and the mini player draw
/// Fluent icons, chosen by Kind, where they used to draw emoji text.</summary>
public sealed class StudioFeatureIconsTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task RackRowsAndDetailHeaderDrawTheModuleIcon() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("studio");
            Dispatcher.UIThread.RunJobs();
            var rack = shell.StudioRack!;
            IconGlyph RowIcon(string key) => rack.GetVisualDescendants().OfType<RadioButton>()
                .First(r => r.Tag as string == key).GetVisualDescendants().OfType<IconGlyph>().First();

            Assert.Equal(IconKind.Heart, RowIcon("pinkfilter").Kind);
            Assert.Equal(IconVariant.Filled, RowIcon("pinkfilter").Variant);
            Assert.Equal(IconKind.Keyboard, RowIcon("lockcard").Kind);   // typing, not the premium padlock
            Assert.Equal(IconBrand.Spiral, RowIcon("spiral").Brand);
            Assert.Equal(IconKind.PhoneVibrate, RowIcon("haptics").Kind);
            // Every icon the rack and its hosted feature pages draw resolved to a Fluent icon. The one emoji
            // fallback is Flash's Hydra octopus, content by decision (oracle table D4: "🐙 Hydra: keep").
            Assert.Equal(new[] { "🐙" }, rack.GetVisualDescendants().OfType<IconGlyph>().Where(g => g.IsFallback).Select(g => g.Emoji));

            rack.FocusRackEntry("mindwipe");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.BrainCircuit, rack.FindControl<IconGlyph>("TxtDetailIcon")!.Kind);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MiniPlayerTransportIsAPlayIconAndCloseIsNamed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var w = new MiniPlayerWindow();
        Assert.Equal(IconKind.Play, w.FindControl<IconGlyph>("IcoPlayPause")!.Kind);
        Assert.Same(w.FindControl<IconGlyph>("IcoPlayPause"), w.FindControl<Button>("BtnPlayPause")!.Content);
        var close = w.FindControl<Button>("BtnClose")!;
        Assert.Equal(IconKind.Dismiss, Assert.IsType<IconGlyph>(close.Content).Kind);
        Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(close)));
        return Task.CompletedTask;
    });
}
