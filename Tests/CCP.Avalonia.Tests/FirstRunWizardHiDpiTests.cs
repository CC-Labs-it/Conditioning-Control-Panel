using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// distro-check F5: at 200% on 1920x1080 (960x540 DIP) the fixed 900x680 wizard put Enter off-screen,
/// so the 18+ gate could not be accepted. Fitted to that working area, Enter and the age checkbox
/// (after scrolling the body) lie inside the window; a normal screen keeps WPF's 900x680.
/// </summary>
public sealed class FirstRunWizardHiDpiTests
{
    [Fact]
    public Task SmallWorkingAreaKeepsEnterAndTheAgeBoxReachable() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var wizard = new FirstRunWizard();
        try
        {
            wizard.FitTo(960, 540);
            wizard.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(900, wizard.Width);
            Assert.Equal(540, wizard.Height);

            var next = wizard.FindControl<Button>("BtnNext")!;
            AssertInside(wizard, next);

            var box = wizard.FindControl<CheckBox>("ChkAgeConfirm")!;
            box.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            AssertInside(wizard, box);
            // Above the footer, not hidden under it: the scrolled body really shows it.
            Assert.True(box.TranslatePoint(new Point(0, box.Bounds.Height), wizard)!.Value.Y
                        <= next.TranslatePoint(default, wizard)!.Value.Y);
        }
        finally { wizard.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task NormalScreenKeepsTheWpfSize() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var wizard = new FirstRunWizard();
        wizard.FitTo(1920, 1032);
        Assert.Equal(900, wizard.Width);
        Assert.Equal(680, wizard.Height);
        wizard.FitToWorkingArea(null);
        Assert.Equal(680, wizard.Height);
        return Task.CompletedTask;
    });

    // The working area comes in physical pixels: 1920x1032 at 200% is 960x516 DIPs, not 1920x1032.
    [Fact]
    public Task WorkingAreaPixelsAreDividedByScaling() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var wizard = new FirstRunWizard();
        wizard.FitToWorkingArea(new PixelRect(0, 0, 1920, 1032), 2);
        Assert.Equal(900, wizard.Width);
        Assert.Equal(516, wizard.Height);
        wizard.FitToWorkingArea(new PixelRect(0, 0, 1920, 1032), 0);
        Assert.Equal(516, wizard.Height);
        return Task.CompletedTask;
    });

    private static void AssertInside(Window window, Control control)
    {
        var topLeft = control.TranslatePoint(default, window)!.Value;
        var bottom = topLeft.Y + control.Bounds.Height;
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(topLeft.Y >= 0 && bottom <= window.Bounds.Height,
            $"{control.Name} spans {topLeft.Y:0}..{bottom:0}, window is {window.Bounds.Height:0} tall");
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
