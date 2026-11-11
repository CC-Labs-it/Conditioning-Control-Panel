using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class BrainDrainRestackTests
{
    [Fact]
    public Task SharedFixtureRestoresBundledInter() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var fonts = FontManager.Current;
        var face = new Typeface(new FontFamily(AppFonts.Inter), FontStyle.Normal, FontWeight.Bold);
        fonts.RemoveFontCollection(new Uri("fonts:Inter"));
        try
        {
            Assert.False(fonts.TryGetGlyphTypeface(face, out _));
            IsolateProcessStateAttribute.EnsureInter();
            Assert.True(fonts.TryGetGlyphTypeface(face, out _));
        }
        finally { if (!fonts.TryGetGlyphTypeface(face, out _)) fonts.AddFontCollection(new global::Avalonia.Fonts.Inter.InterFontCollection()); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task UnsupportedBackendDoesNotClaimToHaveSunkTheBlur() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        // Even this text-free fixture must leave bundled fonts available to the next test.
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(AppFonts.Inter), FontStyle.Normal, FontWeight.Bold), out _));
        var drain = new Window();
        var picture = new Window();
        try
        {
            drain.Show();
            picture.Show();
            Assert.False(X11Overlay.RestackBelow(drain, picture));
        }
        finally { picture.Close(); drain.Close(); }
        return Task.CompletedTask;
    });
}
