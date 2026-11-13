using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit; namespace CCP.Avalonia.Tests;
// WPF MainWindow.BankFx.cs:272,394,488,540,594,635,657,679,734,750.
public sealed class BankShellTests {
    private static object? Field(MainShellWindow sh, string name) => typeof(MainShellWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sh);
    private static void Land(MainShellWindow sh, int id, int index, bool last) => typeof(MainShellWindow)
        .GetMethod("BankLanded", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(sh, [id, index, last]);
    private static Task Run(Action<MainShellWindow, SteppedTime> test) => AvaloniaTestDispatcher.RunAsync(() => {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider; var oldTime = BeatLoop.Time; var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings; var s = CoreSettings.Current; s.OfflineMode = true; s.OfflineUsername = "bank-test";
        s.MotionLevel = MotionLevel.Full; s.GentleVisuals = false; s.PerformanceMode = false; s.AutoPerformanceMode = false; s.PlayerLevel = 10; s.PlayerXP = 5;
        var clock = new SteppedTime(); BeatLoop.Time = clock; var shell = new MainShellWindow();
        try { shell.Show(); shell.Activate(); Dispatcher.UIThread.RunJobs(); Assert.True(shell.IsActive); Assert.True(AmbientFxCanvas.Env.AllowParticles); test(shell, clock); }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); BeatLoop.Time = oldTime; CoreSettings.ServiceProvider = oldSettings; }
        return Task.CompletedTask;
    });
    private static string Text(MainShellWindow sh) => sh.FindControl<TextBlock>("TxtXP")!.Text!;
    private static void Award(double xp = 20, string source = "Quest")
    { ProgressionBank.Add(xp, source); Dispatcher.UIThread.RunJobs(); }
    private static void Launch(MainShellWindow sh, SteppedTime clock)
    { clock.Advance(TimeSpan.FromMilliseconds(1501)); sh.PollBank(); Assert.True(sh.BankFlying); }
    [Fact]
    public Task CompletionHoldsLedgerAndCanvasLandingsReleaseWithCatch() => Run((sh, clock) => {
        var overlay = sh.FindControl<Border>("XPBarFlashOverlay")!; const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic;
        var clockBase = typeof(global::Avalonia.Animation.Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", true)!;
        var animationClock = Activator.CreateInstance(clockBase, nonPublic: true)!;
        var clockProperty = (AvaloniaProperty)typeof(global::Avalonia.Animation.Animatable)
            .GetField("ClockProperty", any | BindingFlags.Static)!.GetValue(null)!;
        overlay.SetValue(clockProperty, animationClock);
        void Pulse(double ms) => clockBase.GetMethod("Pulse", any | BindingFlags.Instance)!
            .Invoke(animationClock, [TimeSpan.FromMilliseconds(ms)]);
        Award(); Assert.Equal(25, CoreSettings.Current.PlayerXP); Assert.True(sh.BankHolding); Assert.True(sh.BankPolling); Assert.StartsWith("5 /", Text(sh));
        Assert.StartsWith("25 /", sh.FindControl<TextBlock>("ProfileMenuXp")!.Text); Launch(sh, clock);
        var layer = Assert.IsType<AmbientFxCanvas>(Field(sh, "_bankLayer")); Assert.InRange(layer.Width, 200, 2600); Assert.InRange(layer.Height, 200, 2600);
        int id = (int)Field(sh, "_bankId")!; Land(sh, id, 0, false); clock.Advance(TimeSpan.FromMilliseconds(100)); BeatLoop.StepAll();
        Assert.NotEqual("5", Text(sh).Split(' ')[0]); Assert.NotEqual("25", Text(sh).Split(' ')[0]);
        // The actual canvas owns the callback stream, including its force-land contract.
        layer.Stop(); Assert.False(sh.BankHolding); Assert.False(sh.BankFlying); Assert.False(sh.BankPolling); Assert.StartsWith("25 /", Text(sh)); Pulse(0); Pulse(125);
        Assert.InRange(overlay.Opacity, .7, .8); clock.Advance(TimeSpan.FromMilliseconds(90)); BeatLoop.StepAll();
        Assert.Equal(1.28, ((ScaleTransform)sh.FindControl<TextBlock>("TxtXP")!.RenderTransform!).ScaleX, 3); clock.Advance(TimeSpan.FromMilliseconds(300)); BeatLoop.StepAll();
        Assert.Equal(1, ((ScaleTransform)sh.FindControl<TextBlock>("TxtXP")!.RenderTransform!).ScaleX, 3);
        Assert.StartsWith("25 /", Text(sh)); // no abandoned step rewinds the truthful release
    });
    [Fact]
    public Task WeatherBypassesButWeatherAndSuccessorDuringFlightKeepTheirOwnPot() => Run((sh, clock) => {
        Award(2, "Mantra"); Assert.False(sh.BankHolding); Assert.False(sh.BankPolling); clock.Advance(TimeSpan.FromSeconds(1)); BeatLoop.StepAll(); Award(); Launch(sh, clock);
        Award(3, "Mantra"); Award(10); int id = (int)Field(sh, "_bankId")!; int count = (int)Field(sh, "_bankCount")!; Land(sh, id, count - 1, true);
        Assert.True(sh.BankHolding); Assert.False(sh.BankFlying); Assert.Equal(27d, Field(sh, "_lastXpShown")); // weather and next pot excluded
        clock.Advance(TimeSpan.FromMilliseconds(3001)); sh.PollBank(); Assert.True(sh.BankFlying); ((AmbientFxCanvas)Field(sh, "_bankLayer")!).Stop(); Assert.False(sh.BankHolding);
        clock.Advance(TimeSpan.FromSeconds(1)); BeatLoop.StepAll(); Assert.StartsWith("40 /", Text(sh));
    });
    [Theory]
    [InlineData("watchdog")]
    [InlineData("hide")]
    [InlineData("minimize")]
    [InlineData("motion")]
    [InlineData("performance")]
    [InlineData("panic")]
    [InlineData("level")]
    [InlineData("close")]
    public Task EveryExitInvalidatesLateLandingsAndStopsPolling(string exit) => Run((sh, clock) => {
        Award(); Launch(sh, clock); int id = (int)Field(sh, "_bankId")!;
        switch (exit) {
            case "watchdog": clock.Advance(TimeSpan.FromMilliseconds(6001)); sh.PollBank(); break; case "hide": sh.Hide(); break;
            case "minimize": sh.WindowState = WindowState.Minimized; break;
            case "motion": CoreSettings.Current.MotionLevel = MotionLevel.Reduced; AmbientFxCanvas.Env.RaiseMotionGateChanged(); break;
            case "performance": CoreSettings.Current.PerformanceMode = true; sh.PollBank(); break; case "panic": PanicSurfaces.All.Single(s => s.Id == "bank-fx").Stop(sh); break;
            case "level": Award(XpCurve.GetXPForLevel(10, XpCurve.EpochOf(CoreSettings.Current))); break; case "close": sh.Close(); break;
        }
        Assert.False(sh.BankHolding); Assert.False(sh.BankFlying); Assert.False(sh.BankPolling); string before = Text(sh); Land(sh, id, 0, false); Assert.Equal(before, Text(sh));
        clock.Advance(TimeSpan.FromSeconds(1)); BeatLoop.StepAll();
        if (exit != "close") Assert.StartsWith($"{(int)CoreSettings.Current.PlayerXP} /", Text(sh));
        if (exit == "close") { Award(); Assert.False(sh.BankHolding); }
    });
    [Theory]
    [InlineData(MotionLevel.Off)]
    [InlineData(MotionLevel.Reduced)]
    public Task DisabledMotionNeverOpensPot(MotionLevel motion) => Run((sh, clock) => {
        CoreSettings.Current.MotionLevel = motion; Award(); Assert.False(sh.BankHolding); Assert.False(sh.BankPolling);
    });
    [Fact]
    public Task InvalidGeometryAndOrphanHoldSelfHeal() => Run((sh, clock) => {
        Award(); sh.FindControl<TextBlock>("TxtXP")!.IsVisible = false; clock.Advance(TimeSpan.FromSeconds(2)); sh.PollBank();
        Assert.False(sh.BankHolding); Assert.False(sh.BankPolling); sh.FindControl<TextBlock>("TxtXP")!.IsVisible = true;
        Award(); ((BankAccumulator)Field(sh, "_bank")!).Reset(); sh.PollBank(); Assert.False(sh.BankHolding); Assert.False(sh.BankPolling);
    });
    [Fact]
    public Task ThudUsesOverrideThenFallbackAndMasterVolume() => Run((sh, clock) => {
        var oldArt = CoreModArt.AudioOverridePathProvider; var oldAudio = CoreAudio.PlayOneShotProvider; string path = Path.GetTempFileName();
        string? heard = null; float volume = 0; string? tag = null;
        try {
            CoreAudio.PlayOneShotProvider = (p, v, t, _, _) => { heard = p; volume = v; tag = t; };
            CoreModArt.AudioOverridePathProvider = key => key == "ui/bank_thud.mp3" ? path : null; CoreSettings.Current.MasterVolume = 50;
            Award(); Launch(sh, clock); ((AmbientFxCanvas)Field(sh, "_bankLayer")!).Stop();
            Assert.Equal(path, heard); Assert.Equal(.11f, volume, 4); Assert.Equal("ui-bank", tag); heard = null;
            CoreModArt.AudioOverridePathProvider = key => key == "bubbles/Pop2.mp3" ? path : null;
            typeof(MainShellWindow).GetMethod("PlayBankThud", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            Assert.Equal(path, heard); heard = null; CoreSettings.Current.MasterVolume = 0;
            typeof(MainShellWindow).GetMethod("PlayBankThud", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null); Assert.Null(heard);
        }
        finally { CoreAudio.PlayOneShotProvider = oldAudio; CoreModArt.AudioOverridePathProvider = oldArt; File.Delete(path); }
    });
    [Theory]
    [InlineData("Quest", "quests")]
    [InlineData("Session", "presets")]
    [InlineData("BubbleCount", "studio")]
    [InlineData("LockCard", null)]
    public Task FlightStartsAtItsSourceOrProfile(string source, string? tab) => Run((sh, clock) => {
        var host = sh.FindControl<Grid>("EventFxHost")!; Control anchor = tab == null ? sh.FindControl<Button>("BtnProfileBubble")! : sh.NavAnchorForTab(tab)!;
        var start = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), host)!.Value;
        var text = sh.FindControl<TextBlock>("TxtXP")!; var end = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), host)!.Value;
        Award(20, source); Launch(sh, clock); var layer = (AmbientFxCanvas)Field(sh, "_bankLayer")!; Assert.Equal(Math.Min(start.X, end.X) - 100, layer.Margin.Left, 3);
        Assert.Equal(Math.Abs(start.X - end.X) + 200, layer.Width, 3); Assert.Equal(Math.Min(start.Y, end.Y) - 100, layer.Margin.Top, 3);
        Assert.Equal(Math.Abs(start.Y - end.Y) + 200, layer.Height, 3);
    });
    [Fact]
    public Task FocusLossDropsThePot() => Run((sh, clock) => {
        Award();
        // Headless does not transfer activation between windows. Use the platform callback.
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sh, null);
        Assert.False(sh.IsActive); Assert.False(sh.BankHolding); Assert.False(sh.BankPolling);
    });
    [Fact]
    public Task BadBoundsRefuseAndStaleFlightCannotStepItsSuccessor() => Run((sh, clock) => {
        var anchor = sh.NavAnchorForTab("quests")!; anchor.RenderTransform = new TranslateTransform(10000, 0);
        Award(); clock.Advance(TimeSpan.FromMilliseconds(1501)); sh.PollBank(); Assert.False(sh.BankFlying); Assert.False(sh.BankHolding); anchor.RenderTransform = null;
        clock.Advance(TimeSpan.FromSeconds(3)); Award(); Launch(sh, clock); int old = (int)Field(sh, "_bankId")!;
        sh.StopBankFx(); clock.Advance(TimeSpan.FromSeconds(3)); Award(); Launch(sh, clock); string before = Text(sh); Land(sh, old, 0, true);
        Assert.True(sh.BankFlying); Assert.True(sh.BankHolding); Assert.Equal(before, Text(sh)); sh.Close();
        var subscribers = (MulticastDelegate?)typeof(ProgressionBank)
            .GetField("XPAwarded", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        Assert.DoesNotContain(subscribers?.GetInvocationList() ?? [], d => ReferenceEquals(d.Target, sh));
    });
    [Fact]
    public Task TokensUseGlowInsteadOfParticleColor() => Run((sh, clock) => {
        var resources = Application.Current!.Resources; var oldGlow = resources["FxGlowColor"]; var oldParticle = resources["FxParticleColor"];
        try {
            resources["FxGlowColor"] = Colors.Lime; resources["FxParticleColor"] = Colors.Red; Assert.NotEqual(AmbientFxCanvas.Env.GlowColor, AmbientFxCanvas.Env.ParticleColor);
            Award(); Launch(sh, clock);
            var layer = (AmbientFxCanvas)Field(sh, "_bankLayer")!; var brush = (IRadialGradientBrush)typeof(AmbientFxCanvas)
                .GetField("_tokDot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layer)!;
            Assert.Equal(Colors.Lime, brush.GradientStops[0].Color);
        }
        finally { resources["FxGlowColor"] = oldGlow; resources["FxParticleColor"] = oldParticle; }
    });
    [Fact]
    public Task HeaderFallbackUsesTransformedTrackBounds() => Run((sh, clock) => {
        sh.FindControl<Viewbox>("ShellViewbox")!.RenderTransform = new ScaleTransform(.65, .8); sh.NavAnchorForTab("quests")!.IsVisible = false;
        sh.FindControl<Button>("BtnProfileBubble")!.IsVisible = false; sh.UpdateLayout();
        var host = sh.FindControl<Grid>("EventFxHost")!; var track = sh.FindControl<Border>("XPBarTrack")!;
        var transform = track.TransformToVisual(host)!.Value; Assert.NotEqual(1, transform.M11);
        var bounds = new Rect(track.Bounds.Size).TransformToAABB(transform); var text = sh.FindControl<TextBlock>("TxtXP")!;
        var target = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), host)!.Value; Award(); Launch(sh, clock);
        var layer = (AmbientFxCanvas)Field(sh, "_bankLayer")!; var origin = new Point(bounds.Left - 180, bounds.Center.Y);
        Assert.Equal(Math.Min(origin.X, target.X) - 100, layer.Margin.Left, 3); Assert.Equal(Math.Abs(origin.X - target.X) + 200, layer.Width, 3);
        Assert.Equal(Math.Min(origin.Y, target.Y) - 100, layer.Margin.Top, 3); Assert.Equal(Math.Abs(origin.Y - target.Y) + 200, layer.Height, 3);
    });
}
