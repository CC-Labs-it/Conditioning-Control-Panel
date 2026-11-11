using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Relapse on the shared stop pass: tray and leash-gate panics track only a running stop
/// (WPF MainWindow.xaml.cs:1925-1937), the spoken safe word always (WPF MainWindow.RemoteControl.cs:1493).</summary>
public sealed class RelapseShellTests
{
    private static void WithShell(Action<MainShellWindow, AchievementEngine> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-relapse-shell-").FullName;
        var field = typeof(AvApp).GetField("<Achievements>k__BackingField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldEngine = field.GetValue(null);
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        field.SetValue(null, engine);
        var oldAll = PanicSurfaces.All;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("engine", _ => CoreEngine.Stop()) };
        var s = CoreSettings.Current;
        var oldPanicKey = s.PanicKeyEnabled;
        s.PanicKeyEnabled = true;   // the safe word reaches as far as the key (Core VoicePanicReach)
        var shell = new MainShellWindow();
        shell.Show();
        try { body(shell, engine); }
        finally
        {
            CoreEngine.Stop();
            PanicAftermath.EndQuiet();
            PanicSurfaces.All = oldAll;
            s.PanicKeyEnabled = oldPanicKey;
            shell.Close();
            field.SetValue(null, oldEngine);
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void TrayPanicTracksOnlyARunningStop() => WithShell((shell, engine) =>
    {
        PanicSurfaces.StopAll("tray", shell);
        Assert.Null(engine.Progress.LastPanicPressTime);
        CoreEngine.Start(systemInitiated: true);
        PanicSurfaces.StopAll("tray", shell);
        Assert.False(CoreEngine.IsRunning);
        Assert.NotNull(engine.Progress.LastPanicPressTime);
    });

    [Fact]
    public void VoicePanicTracksEvenWhenIdle() => WithShell((shell, engine) =>
    {
        Assert.False(CoreEngine.IsRunning);
        Assert.True(shell.RunVoicePanic());
        Assert.NotNull(engine.Progress.LastPanicPressTime);
    });
}
