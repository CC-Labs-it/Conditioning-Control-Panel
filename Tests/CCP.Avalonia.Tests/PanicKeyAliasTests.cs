using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>A stored key name matches the pressed Avalonia <see cref="Key"/> by value, so an alias
/// ("Enter" for the key Avalonia prints as "Return") still fires the panic key on the in-window
/// surfaces. MandatoryVideoOverlay and FirstShowStage are covered in their own test classes.</summary>
public sealed class PanicKeyAliasTests
{
    [Theory]
    [InlineData("Enter", Key.Return)]
    [InlineData("Return", Key.Enter)]
    [InlineData("enter", Key.Return)]
    [InlineData(" Escape ", Key.Escape)]
    [InlineData("CapsLock", Key.Capital)]
    [InlineData("Next", Key.PageDown)]
    [InlineData("OemTilde", Key.Oem3)]
    [InlineData("F12", Key.F12)]
    public void AliasesMatchTheSameKey(string stored, Key pressed)
    {
        Assert.True(HotkeyNames.Matches(pressed, stored));
        Assert.Equal(pressed.ToString(), HotkeyNames.Canonical(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("6")]       // the numeric value of Return must not parse
    [InlineData("None")]
    [InlineData("NotAKey")]
    public void BlankNumericOrUnknownNeverMatch(string? stored)
    {
        Assert.False(HotkeyNames.Matches(Key.Return, stored));
        Assert.False(HotkeyNames.Matches(Key.None, stored));
        Assert.Equal(stored, HotkeyNames.Canonical(stored));
    }

    [Fact]
    public void DifferentKeysDoNotMatch() => Assert.False(HotkeyNames.Matches(Key.Space, "Enter"));

    /// <summary>The game window (no focusable child): panic bound to Enter, no global listener, Enter force-stops.</summary>
    [Fact]
    public Task BubbleCountGamePanicKeyBoundToEnter() => Run(() =>
    {
        var w = new BubbleCountWindow("none.mp4", BubbleCountScheduler.Difficulty.Medium, strictMode: false, _ => { });
        Assert.Contains(w, BubbleCountWindow.OpenWindows);
        // Routed through the window's own KeyDown subscription (handler stays private). Not KeyPress: Show()
        // without a playable video ends the game, so a headless real press would hit a closed window.
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return, Source = w };
        w.RaiseEvent(e);
        Assert.True(e.Handled, "Enter is the panic key");
        Assert.Empty(BubbleCountWindow.OpenWindows);
    });

    /// <summary>The answer window: Enter in the focused answer box would submit it; the panic key wins.</summary>
    [Fact]
    public Task BubbleCountAnswerPanicKeyBoundToEnterBeatsTheFocusedBox() => Run(() =>
    {
        double xp = 0;
        var prevXp = CoreProgression.AddXPProvider;
        CoreProgression.AddXPProvider = (a, _) => xp += a;
        try
        {
            bool? completed = null;
            var r = new BubbleCountResultWindow(7, strictMode: false, ok => completed = ok);
            r.Show();
            var box = r.FindControl<TextBox>("TxtAnswer")!;
            box.Text = "7";
            box.Focus();
            Assert.True(box.IsFocused);
            r.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
            Assert.Equal(0, xp);                       // the answer was not submitted
            Assert.Null(completed);
            Assert.Empty(BubbleCountResultWindow.OpenWindows);
        }
        finally
        {
            CoreProgression.AddXPProvider = prevXp;
            BubbleCountResultWindow.ForceCloseAll();
        }
    });

    private static Task Run(System.Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.PanicKeyEnabled, s.PanicKey);
        var listener = BubbleCountWindow.PanicListenerLive;
        var engine = CoreEngine.BubbleCount;
        (s.PanicKeyEnabled, s.PanicKey) = (true, "Enter");
        BubbleCountWindow.PanicListenerLive = () => false;
        CoreEngine.BubbleCount = null;
        try { body(); }
        finally
        {
            BubbleCountWindow.ForceCloseAll();
            (s.PanicKeyEnabled, s.PanicKey) = saved;
            BubbleCountWindow.PanicListenerLive = listener;
            CoreEngine.BubbleCount = engine;
        }
        return Task.CompletedTask;
    });
}
