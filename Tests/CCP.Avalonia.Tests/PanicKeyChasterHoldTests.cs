using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Fix wave 2 #4: WPF HandlePanicKeyPress opens Circe's Tab safety hold on every panic-key press, before
/// the lock-card, palette and grace-pause rungs return (only a game's Escape pause, which is no panic, comes
/// first). Here the hold lived only in the stop pass, so a press spent on a lock card left prices booking.</summary>
public sealed class PanicKeyChasterHoldTests
{
    [Fact]
    public void APressSpentOnALockCard_StillOpensTheHold() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.PanicKeyEnabled, s.PanicKey);
        var (oldAll, oldService) = (PanicSurfaces.All, PanicChasterHold.Service);
        var stops = new List<string>();
        var holds = 0;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stops.Add("stop")) };
        PanicChasterHold.Service = () => { holds++; return null; };
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
            LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: true);
            Assert.True(LockCardWindow.IsAnyOpen());

            shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));

            Assert.False(LockCardWindow.IsAnyOpen());
            Assert.Empty(stops);          // the lock-card rung returned before the stop pass
            Assert.Equal(1, holds);       // ...and the hold opened anyway
        }
        finally
        {
            if (LockCardWindow.IsAnyOpen()) LockCardWindow.ForceCloseAll();
            (PanicSurfaces.All, PanicChasterHold.Service) = (oldAll, oldService);
            shell.Close();
            (s.PanicKeyEnabled, s.PanicKey) = old;
        }
    });

    [Fact]
    public void TheHoldOpensAfterTheGamePauseAndBeforeEveryOtherRung()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Windows", "MainShellWindow.PanicKey.cs"))
            .ReplaceLineEndings("\n");
        var at = src.IndexOf("internal void HandlePanicKeyPress(DateTime now)", StringComparison.Ordinal);
        var body = src.Substring(at, src.IndexOf("\n        }\n", at, StringComparison.Ordinal) - at);
        int pause = body.IndexOf("if (TryGamePauseOnEscape(now)) return;", StringComparison.Ordinal);
        int hold = body.IndexOf("PanicChasterHold.Open(\"panic key\");", StringComparison.Ordinal);
        int lockCard = body.IndexOf("LockCardWindow.IsAnyOpen()", StringComparison.Ordinal);
        int palette = body.IndexOf("SettingsPaletteWindow.TryConsumeEscape()", StringComparison.Ordinal);
        int grace = body.IndexOf("TryGracePause(", StringComparison.Ordinal);
        Assert.True(pause > 0 && hold > pause, "a game's Escape pause is no panic: it returns before the hold");
        Assert.True(hold < lockCard && hold < palette && hold < grace, "the hold opens before every rung that returns");
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
