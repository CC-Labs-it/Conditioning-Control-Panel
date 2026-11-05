using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Commands;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Fix wave 2 #3: the post-panic quiet (Core PanicAftermath, WPF ccp-bugs #1464) on this head. Every
/// panic route arms it, the AI command gate reads it (seeded at start-up, WPF App.AiEffects.cs:19-20), and
/// only the user's own Start or Resume ends it early.</summary>
public sealed class PanicAftermathHeadTests
{
    private static void WithShell(Action<MainShellWindow, List<string>> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.PanicKeyEnabled, s.PanicKey);
        var oldAll = PanicSurfaces.All;
        var stops = new List<string>();
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stops.Add("stop")) };
        var shell = new MainShellWindow();
        shell.Show();
        PanicAftermath.EndQuiet();
        try { body(shell, stops); }
        finally
        {
            PanicAftermath.EndQuiet();
            PanicSurfaces.All = oldAll;
            if (LockCardWindow.IsAnyOpen()) LockCardWindow.ForceCloseAll();
            shell.Close();
            (s.PanicKeyEnabled, s.PanicKey) = old;
        }
    });

    [Fact]
    public void APressSpentOnALockCard_StillArmsTheQuiet() => WithShell((shell, stops) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
        LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: true);
        Assert.True(LockCardWindow.IsAnyOpen());
        var before = PanicAftermath.PanicCount;

        shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));

        Assert.Empty(stops);                                // the lock-card rung returns before the stop pass
        Assert.True(PanicAftermath.TriggersSilenced());     // WPF arms it right after the rung is decided
        Assert.True(PanicAftermath.PanicCount > before);
    });

    [Fact]
    public void ThePanicKeyThatCannotRun_ArmsNothing() => WithShell((shell, _) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (false, "F8");
        shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
        Assert.False(PanicAftermath.TriggersSilenced());
    });

    [Fact]
    public void TheStopPass_ArmsTheQuiet_ForTrayVoiceAndTheLeashGate()
    {
        // Tray Stop everything, the spoken safe word and the leash gate's Panic all run PanicSurfaces.StopAll.
        var quiet = PanicSurfaces.All.Single(x => x.Id == "panic-quiet");
        PanicAftermath.EndQuiet();
        try
        {
            quiet.Stop(null);
            Assert.True(PanicAftermath.TriggersSilenced());
            var ids = PanicSurfaces.All.Select(x => x.Id).ToList();
            Assert.True(ids.IndexOf("panic-quiet") < ids.IndexOf("engine"));
        }
        finally { PanicAftermath.EndQuiet(); }
    }

    [Fact]
    public void TheAiGate_ReadsTheQuietAndTheCount_OnceSeeded()
    {
        var (oldQuiet, oldCount) = (AiCommandService.PanicQuietProvider, AiCommandService.PanicCountProvider);
        try
        {
            AiCommandService.PanicQuietProvider = null;
            AiCommandService.PanicCountProvider = null;
            MainShellWindow.SeedPanicAftermath();
            PanicAftermath.EndQuiet();
            Assert.False(AiCommandService.PanicQuietProvider!());
            PanicAftermath.ArmQuiet();
            Assert.True(AiCommandService.PanicQuietProvider!());
            Assert.Equal(PanicAftermath.PanicCount, AiCommandService.PanicCountProvider!());
        }
        finally
        {
            PanicAftermath.EndQuiet();
            (AiCommandService.PanicQuietProvider, AiCommandService.PanicCountProvider) = (oldQuiet, oldCount);
        }
    }

    [Fact]
    public void TheUsersOwnStart_EndsTheQuiet() => WithShell((shell, _) =>
    {
        PanicAftermath.ArmQuiet();
        try
        {
            shell.RandomizeAndStart(new Random(1));   // "Jump right in", the user's own Start
            Assert.False(PanicAftermath.TriggersSilenced());
        }
        finally { MainShellWindow.StopEngine(); }
    });

    /// <summary>Routes that need a live desktop or a controller: a source scan, like PanicCancelsAiScanTests.</summary>
    [Theory]
    [InlineData("CCP.Avalonia/Views/Windows/MainShellWindow.PanicKey.cs", "internal bool StartPanicKey()", "SeedPanicAftermath();")]
    [InlineData("CCP.Avalonia/Platform/PanicWatchdog.cs", "private static void RunEmergencyTeardown()", "PanicAftermath.ArmQuiet()")]
    [InlineData("CCP.Core/Services/RemoteControl/RemoteCommands.cs", "public static void StopEffects(bool force)", "if (force) Safety.PanicAftermath.ArmQuiet();")]
    [InlineData("CCP.Avalonia/Views/Windows/MainShellWindow.SessionRun.cs", "private async void BtnPauseSession_Click(", "PanicAftermath.EndQuiet();")]
    public void EveryRouteArmsOrEndsTheQuiet(string file, string signature, string call)
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), file)).ReplaceLineEndings("\n");
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found in {file}");
        var end = src.IndexOf("\n        }\n", start, StringComparison.Ordinal);
        if (end < 0) end = src.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.Contains(call, src.Substring(start, end - start));
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
