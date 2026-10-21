using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Adapted from parity/trunk 90cdc1120: the subliminal card reads the "steals focus" switch (WPF SubliminalService.cs:730,
/// :764-765). Swaps the process-wide activate seam, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalStealsFocusTests
{
    [Fact]
    public void Win32FocusOptIn_PreservesMousePassThroughAcrossStyleRebuilds()
    {
        const uint rebuilt = 0x00200000u; // Avalonia NOREDIRECTIONBITMAP survives our callback
        var passive = Win32Overlay.Style(rebuilt, clickThrough: true);
        var focused = Win32Overlay.Style(passive, clickThrough: true, allowFocus: true);
        Assert.Equal(0u, focused & Win32Overlay.WsExNoActivate);
        Assert.NotEqual(0u, focused & Win32Overlay.WsExTransparent);
        Assert.Equal(rebuilt | Win32Overlay.OverlayBits | Win32Overlay.WsExTransparent, focused);
        Assert.Equal(focused, Win32Overlay.Style(rebuilt, clickThrough: true, allowFocus: true));
        // Other passive surfaces retain their no-focus contract even if focus were requested.
        Assert.NotEqual(0u, Win32Overlay.Style(focused, true, passive: true, allowFocus: true) & Win32Overlay.WsExNoActivate);
    }

    [Fact]
    public Task The_card_takes_the_foreground_only_with_the_switch_on() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        // Other classes can initialize a bare headless app first. This fixture uses the same
        // bundled face as the shipped app, independent of that process's default font manager.
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(AppFonts.Inter)), out _))
        {
            var builder = AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register);
            builder.AfterSetupCallback(builder);
        }
        var settings = global::ConditioningControlPanel.CoreSettings.Current;
        var font = settings.SubliminalFont;
        var steals = settings.SubliminalStealsFocus;
        settings.SubliminalFont = AppFonts.Inter;
        var before = SubliminalOverlay.ActivateCard;
        var activated = 0;
        SubliminalOverlay.ActivateCard = _ => activated++;
        var off = new SubliminalOverlayWindow("OBEY");
        var on = new SubliminalOverlayWindow("OBEY");
        try
        {
            Assert.False(X11Overlay.ActivateClickThrough(off)); // headless never invokes native APIs
            settings.SubliminalStealsFocus = false;
            SubliminalOverlay.Present(off);
            Assert.True(off.IsVisible);
            Assert.Equal(0, activated);

            settings.SubliminalStealsFocus = true; // the Advanced ChkStealsFocus setting
            SubliminalOverlay.Present(on);
            Assert.True(on.IsVisible);
            Assert.Equal(1, activated);
            Assert.False(on.IsHitTestVisible);
            Assert.False(on.Focusable);
            Assert.False(on.ShowActivated);   // shown passive first, as WPF's SWP_NOACTIVATE show, then activated

            // A platform that refuses the focus never breaks the card.
            SubliminalOverlay.ActivateCard = _ => throw new InvalidOperationException("refused");
            var refused = new SubliminalOverlayWindow("OBEY");
            SubliminalOverlay.Present(refused);
            Assert.True(refused.IsVisible);
            SubliminalOverlay.CloseAll(); // existing engine Stop / Panic desktop-overlays path
            Assert.False(off.IsVisible);
            Assert.False(on.IsVisible);
            Assert.False(refused.IsVisible);
        }
        finally
        {
            settings.SubliminalFont = font;
            settings.SubliminalStealsFocus = steals;
            SubliminalOverlay.ActivateCard = before;
            off.Close(); on.Close();
        }
        return Task.CompletedTask;
    });
}
