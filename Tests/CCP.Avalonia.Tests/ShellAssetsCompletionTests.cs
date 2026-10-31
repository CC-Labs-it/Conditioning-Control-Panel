using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ShellAssetsCompletionTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void ThumbnailUsesPortableDecoderOnlyOffWindows()
    {
        string? requested = null;
        var missing = Path.Combine(Path.GetTempPath(), "ccp-missing-" + Guid.NewGuid().ToString("N"), "clip.mp4");
        Assert.Null(global::ConditioningControlPanel.Avalonia.Platform.ShellVideoThumbnail.Load(missing, path =>
        {
            requested = path;
            return null;
        }));
        Assert.Equal(OperatingSystem.IsWindows() ? null : missing, requested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NestedWheelMovesOuterEvenWhenInnerCanScroll(bool keywords) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        Control subject = keywords
            ? new global::ConditioningControlPanel.Avalonia.Views.Controls.Companion.KeywordTriggersPanel()
            : new global::ConditioningControlPanel.Avalonia.Views.Controls.AppSettings.UpdatesSettingsSection();
        var inner = subject.FindControl<ScrollViewer>("ParentWheelScroll")!;
        inner.Height = 100; inner.Content = new Border { Height = 600 };
        // Keep the real control and its attached handler, with deterministic scroll bounds.
        var outer = new ScrollViewer { Content = new StackPanel { Children = { subject, new Border { Height = 1000 } } } };
        var window = new Window { Width = 400, Height = 300, Content = outer };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var wheel = new PointerWheelEventArgs(inner, new Pointer(1, PointerType.Mouse, true), window, default, 0, default,
                KeyModifiers.None, new Vector(0, -1));
            inner.RaiseEvent(wheel);
            Assert.True(wheel.Handled);
            Assert.Equal(120, outer.Offset.Y);
            Assert.Equal(0, inner.Offset.Y);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
