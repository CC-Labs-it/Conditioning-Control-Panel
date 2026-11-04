using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperEditorTitleBarTests
{
    [Fact]
    public void DarkTitleBarOnlyUsesAWindowsHandle()
    {
        var calls = 0;
        var handle = new IntPtr(42);
        void Apply(IntPtr actual) { Assert.Equal(handle, actual); calls++; }
        DeeperEditorWindow.ApplyDarkTitleBar(false, handle, Apply);
        DeeperEditorWindow.ApplyDarkTitleBar(true, IntPtr.Zero, Apply);
        Assert.Equal(0, calls);
        DeeperEditorWindow.ApplyDarkTitleBar(true, handle, Apply);
        Assert.Equal(1, calls);
    }

    [Fact]
    public Task EditorOpensOnHeadlessPlatform() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var editor = new DeeperEditorWindow();
        try { editor.Show(); Assert.True(editor.IsVisible); }
        finally { editor.Close(); }
        return Task.CompletedTask;
    });
}
