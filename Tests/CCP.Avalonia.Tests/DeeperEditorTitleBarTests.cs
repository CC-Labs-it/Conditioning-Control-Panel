using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using ConditioningControlPanel;
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
        void Apply(IntPtr actual, int attribute, int value) { Assert.Equal(handle, actual); calls++; }
        DeeperEditorWindow.ApplyDarkTitleBar(false, handle, Apply, null);
        DeeperEditorWindow.ApplyDarkTitleBar(true, IntPtr.Zero, Apply, null);
        Assert.Equal(0, calls);
        DeeperEditorWindow.ApplyDarkTitleBar(true, handle, Apply, null);
        Assert.Equal(4, calls);
    }

    [Theory]
    [InlineData("#FF69B4", 0xB469FF, 0x260816)]
    [InlineData("#FFFFFF", 0xFFFFFF, 0x260816)]
    [InlineData("invalid", 0xB469FF, 0x260816)]
    public void DarkTitleBarUsesAccentAndContrast(string accent, int caption, int text)
    {
        var calls = new List<(IntPtr, int, int)>();
        var handle = new IntPtr(42);
        void Apply(IntPtr h, int a, int v) => calls.Add((h, a, v));
        DeeperEditorWindow.ApplyDarkTitleBar(false, handle, Apply, accent);
        DeeperEditorWindow.ApplyDarkTitleBar(true, IntPtr.Zero, Apply, accent);
        Assert.Empty(calls);
        DeeperEditorWindow.ApplyDarkTitleBar(true, handle, Apply, accent);
        Assert.Equal(new[] { (handle, 20, 1), (handle, 35, caption), (handle, 34, caption), (handle, 36, text) }, calls);
    }

    [Fact]
    public Task OpeningEditorRequestsAttributesForItsHandle() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreMods.AccentColorHexProvider;
        CoreMods.AccentColorHexProvider = () => "#123456";
        var calls = new List<(IntPtr, int, int)>();
        var handle = new IntPtr(42);
        DeeperEditorWindow? requestedWindow = null;
        var editor = new DeeperEditorWindow { TitleBarWindowsOverride = true,
            TitleBarHandleOverride = window => { requestedWindow = window; return handle; },
            TitleBarAttributeOverride = (h, a, v) => calls.Add((h, a, v)) };
        try
        {
            editor.Show();
            Assert.Same(editor, requestedWindow);
            Assert.Equal(new[] { (handle, 20, 1), (handle, 35, 0x563412),
                (handle, 34, 0x563412), (handle, 36, 0xFFFFFF) }, calls);
        }
        finally { editor.Close(); CoreMods.AccentColorHexProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditorOpensOnHeadlessPlatform() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var editor = new DeeperEditorWindow();
        try { editor.Show(); Assert.True(editor.IsVisible); }
        finally { editor.Close(); }
        return Task.CompletedTask;
    });
}
