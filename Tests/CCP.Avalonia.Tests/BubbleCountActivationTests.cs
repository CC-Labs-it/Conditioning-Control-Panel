using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleCountActivationTests
{
    [Theory]
    [InlineData(0x21, true, 3)] // WM_MOUSEACTIVATE -> MA_NOACTIVATE (deliver the click)
    [InlineData(0x100, false, 0)] // WM_KEYDOWN: retain Escape/panic handling
    [InlineData(0x201, false, 0)] // WM_LBUTTONDOWN: do not eat mouse input
    public void Click_hook_denies_only_mouse_activation(int message, bool expectedHandled, int expectedResult)
    {
        var hook = typeof(Win32Overlay).GetMethod("PreventClickRaise", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(hook);
        object[] args = { IntPtr.Zero, (uint)message, IntPtr.Zero, IntPtr.Zero, false };
        Assert.Equal(new IntPtr(expectedResult), hook.Invoke(null, args));
        Assert.Equal(expectedHandled, args[4]);
    }

    [Fact]
    public Task Live_bubble_is_owned_by_game_and_panic_closes_both() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var owner = new BubbleCountWindow(); // render constructor: no decoder or game timers
        CountBubble? bubble = null;
        try
        {
            owner.Show();
            bubble = new CountBubble(null, 120, 10, 10, new Random(1), null, owner);
            var window = (Window)typeof(CountBubble).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bubble)!;
            Assert.Same(owner, window.Owner); // owned native windows stay above their owner after clicks
            Assert.False(window.ShowActivated);
            Assert.False(window.IsHitTestVisible);
            BubbleCountWindow.ForceCloseAll();
            Assert.False(owner.IsVisible);
            Assert.False(window.IsVisible);
        }
        finally
        {
            bubble?.Dispose();
            BubbleCountWindow.ForceCloseAll();
        }
        return Task.CompletedTask;
    });
}
