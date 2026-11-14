using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NavRailMisrouteTests
{
    private sealed class Host(params PossessionTarget[] targets) : IPossessionHost
    {
        public IReadOnlyList<PossessionTarget> Targets => targets;
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        private sealed class Handle : IDisposable { public void Dispose() { } }
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new Handle();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }
    private static PossessionTarget Target(Button button) => new()
    { Visual = button, Role = PossessionRole.TabHeader, Key = button.Name!, DisplayName = button.Name! };
    private static PossessionContext Context(params PossessionTarget[] targets) => new()
    {
        Host = new Host(targets), Attribution = new Attribution(), Rung = PossessionRung.Melt,
        Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1),
        ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { }
    };

    private static void Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
        {
            var fonts = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        }
        var old = CoreSettings.Current.NavLastTabBySection;
        CoreSettings.Current.NavLastTabBySection = null;
        var w = new MainShellWindow();
        try { w.Show(); Dispatcher.UIThread.RunJobs(); body(w); }
        finally
        {
            MainShellWindow.PossessionReroute = null;
            w.Close(); CoreSettings.Current.NavLastTabBySection = old;
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static void Press(MainShellWindow w, string name)
    {
        var b = w.Named<Button>(name)!;
        var at = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void HookCanRerouteOnePress() => Run(w =>
    {
        MainShellWindow.PossessionReroute = from => { MainShellWindow.PossessionReroute = null; return "social"; };
        Press(w, "DoorStudio");
        Assert.Equal("availablesubjects", w.CurrentTab);
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnavailableDestinationLeavesPressAlone(bool hidden) => Run(w =>
    {
        var destination = w.Named<Button>("DoorSocial")!;
        if (hidden) destination.IsVisible = false; else destination.IsEnabled = false;
        MainShellWindow.PossessionReroute = _ => "social";
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
    });

    [Fact]
    public void ThrowingHookLeavesPressAlone() => Run(w =>
    {
        MainShellWindow.PossessionReroute = _ => throw new InvalidOperationException();
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
    });

    [Fact]
    public void WebAppIsNeverADestination() => Run(w =>
    {
        // Even if a launcher were offered as a rail row, it cannot receive the reroute.
        w.Named<Button>("DoorSocial")!.Tag = "webapp";
        MainShellWindow.PossessionReroute = _ => "webapp";
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
    });
    [Fact]
    public void ShellDiscoversRailDoorsAndMisroutesARealPress() => Run(w =>
    {
        var host = (IPossessionHost)w;
        var doors = new[]
        {
            ("DoorHome", "the Home door"), ("DoorStudio", "the Studio door"),
            ("DoorCompanion", "the Companion door"), ("DoorPlay", "the Play door"),
            ("DoorSocial", "the Social door"), ("DoorYou", "the You door"),
            ("DoorLibrary", "the Library door"), ("DoorSettings", "the Settings door")
        };
        var targets = host.Targets;
        foreach (var (name, displayName) in doors)
        {
            var button = w.Named<Button>(name)!;
            var target = Assert.Single(targets, t => ReferenceEquals(t.Visual, button));
            Assert.Equal(PossessionRole.TabHeader, target.Role);
            Assert.Equal(displayName, target.DisplayName);
            if (name != "DoorStudio" && name != "DoorSocial") button.IsVisible = false;
        }
        Dispatcher.UIThread.RunJobs();
        w.InvalidatePossessionTargets();
        var studio = Assert.Single(host.Targets, t => ReferenceEquals(t.Visual, w.Named<Button>("DoorStudio")));
        var ctx = new PossessionContext
        {
            Host = host, Attribution = new Attribution(), Rung = PossessionRung.Melt,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1),
            ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { }
        };
        var effect = Assert.IsType<MisrouteEffect>(
            Assert.Single(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "misroute"));
        Assert.True(effect.CanApply(ctx, studio));
        try
        {
            effect.ApplyAsync(ctx, studio, CancellationToken.None).GetAwaiter().GetResult();
            Press(w, "DoorStudio");
            Assert.Equal("availablesubjects", w.CurrentTab);
            Assert.Null(MainShellWindow.PossessionReroute);
            Press(w, "DoorStudio");
            Assert.Equal("studio", w.CurrentTab);
        }
        finally { effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult(); }
    });

    [Fact]
    public void MisrouteEffectStealsOnePressAndReleasesItsHook() => Run(w =>
    {
        var studio = Target(w.Named<Button>("DoorStudio")!);
        var social = Target(w.Named<Button>("DoorSocial")!);
        var ctx = Context(studio, social);
        var effect = new MisrouteEffect();
        Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "misroute");
        Assert.True(effect.CanApply(ctx, studio));
        effect.ApplyAsync(ctx, studio, CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(MainShellWindow.PossessionReroute);
        Assert.Equal("social", MainShellWindow.PossessionReroute!("studio"));
        Assert.Null(MainShellWindow.PossessionReroute);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        effect.ApplyAsync(ctx, studio, CancellationToken.None).GetAwaiter().GetResult();
        Press(w, "DoorStudio");
        Assert.Equal("availablesubjects", w.CurrentTab);
        Assert.Null(MainShellWindow.PossessionReroute);
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();

        string? named = null;
        var socialButton = w.Named<Button>("DoorSocial")!;
        PossessionTree.SetName(socialButton, "the social door");
        var second = new PossessionContext
        {
            Host = new Host(studio, social), Attribution = new Attribution(), Rung = PossessionRung.Melt,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1),
            ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, target) => named = target
        };
        effect.ApplyAsync(second, studio, CancellationToken.None).GetAwaiter().GetResult();
        Press(w, "DoorSocial");
        Assert.Equal("studio", w.CurrentTab);
        Assert.Equal("the social door", named);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
    });

    [Fact]
    public void MisrouteRejectsLockdownAsSourceAndDestination() => Run(w =>
    {
        var studioButton = w.Named<Button>("DoorStudio")!;
        var socialButton = w.Named<Button>("DoorSocial")!;
        var studio = Target(studioButton);
        var social = Target(socialButton);
        var effect = new MisrouteEffect();
        socialButton.Tag = "lockdown";
        Assert.False(effect.CanApply(Context(studio, social), studio));
        Assert.False(effect.CanApply(Context(studio, social), social));
        socialButton.Tag = "social";
        Assert.True(effect.CanApply(Context(studio, social), studio));
        effect.ApplyAsync(Context(studio, social), studio, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Null(MainShellWindow.PossessionReroute!("lockdown"));
        Assert.NotNull(MainShellWindow.PossessionReroute);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.Null(MainShellWindow.PossessionReroute);
    });

    [Fact]
    public void RailNeverRoutesFromOrToLockdown() => Run(w =>
    {
        var studio = w.Named<Button>("DoorStudio")!;
        var social = w.Named<Button>("DoorSocial")!;
        var called = false;
        studio.Tag = "lockdown";
        MainShellWindow.PossessionReroute = _ => { called = true; return "social"; };
        Press(w, "DoorStudio");
        Assert.False(called);
        studio.Tag = "studio";
        social.Tag = "lockdown";
        MainShellWindow.PossessionReroute = _ => "lockdown";
        Press(w, "DoorStudio");
        Assert.Equal("studio", w.CurrentTab);
    });

}
