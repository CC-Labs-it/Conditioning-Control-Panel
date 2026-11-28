using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane L5 of the Linux Super port: Scrawl on the bouncing-text windows, its state in Core
/// <see cref="ScrawlField"/> (WPF BouncingTextService.cs:390-780, ScrawlScene.cs).</summary>
public sealed class ScrawlOverlayTests
{
    internal static global::Avalonia.Platform.Screen Screen(int w = 1920, int h = 1080)
    {
        var s = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(1), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(s, 1.0);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(s, new PixelRect(0, 0, w, h));
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(s, new PixelRect(0, 0, w, h));
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(s, true);
        return s;
    }

    /// <summary>Bouncing text on one 1920x1080 screen with the shipped Scrawl switch, for the given account.</summary>
    internal static Task WithWords(bool premium, Action<AppSettings> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        var saved = (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests, BouncingTextOverlay.ScrawlOn,
            BouncingTextOverlay.ClickOverOwnWindow, BouncingTextOverlay.RaiseAll, BouncingTextOverlay.CornerHit,
            CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider, ScreenList.QueryOverride);
        var all = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        var host = new Window();
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
            SuperStageHost.Stop();
            var s = service.Current;
            s.BouncingTextEnabled = true;
            s.SuperModes = SuperModeRule.With(s.SuperModes, SuperEffect.Scrawl, SuperMode.Both);
            (s.MotionLevel, s.GentleVisuals, s.LockdownPhotosafe, s.MovingEffectsPop) = (MotionLevel.Full, false, false, false);
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => premium;
            SuperAccess.TryingProvider = () => null;
            ScreenList.QueryOverride = _ => new[] { Screen() };
            BouncingTextOverlay.SkipPlatformChecksForTests = true;
            BouncingTextOverlay.ManualFramesForTests = true;
            BouncingTextOverlay.ScrawlOn = () => SuperAccess.IsOn(SuperEffect.Scrawl);   // the shipped switch
            BouncingTextOverlay.ClickOverOwnWindow = (_, _) => false;
            BouncingTextOverlay.RaiseAll = () => { };
            BouncingTextOverlay.CornerHit = () => { };
            host.Show();
            BouncingTextOverlay.Start(host);
            Assert.True(BouncingTextOverlay.IsRunning);
            body(s);
        }
        finally
        {
            BouncingTextOverlay.Stop();
            SuperStageHost.Stop();
            host.Close();
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", all);
            (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests, BouncingTextOverlay.ScrawlOn,
                BouncingTextOverlay.ClickOverOwnWindow, BouncingTextOverlay.RaiseAll, BouncingTextOverlay.CornerHit,
                CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider, ScreenList.QueryOverride) = saved;
            CoreSettings.ServiceProvider = provider;
        }
        return Task.CompletedTask;
    });

    private static void HitLeft(BouncingTextEngine e)
    {
        var l = e.Logos[0];
        (l.PosX, l.PosY, l.VelX, l.VelY) = (e.MinX - 1, (e.MinY + e.MaxY) / 2, -100, 0);
        BouncingTextOverlay.Advance(0.016);
    }

    [Fact]
    public Task A_free_account_never_gets_a_stamp_without_the_debug_override() => WithWords(premium: false, _ =>
    {
        HitLeft(BouncingTextOverlay.Engine);
        Assert.Null(BouncingTextOverlay.Scrawl);
        Assert.Contains(SuperEffect.Scrawl, SuperBox.HostedOnThisHead);
        CoreEntitlement.HasPremiumProvider = () => true;
        HitLeft(BouncingTextOverlay.Engine);   // the frame re-reads the switch
        Assert.NotNull(BouncingTextOverlay.Scrawl);
    });

    [Fact]
    public Task The_walls_hold_fewer_stamps_when_they_grow_and_start_over_when_the_size_moves() => WithWords(premium: true, s =>
    {
        var e = BouncingTextOverlay.Engine;
        (s.ScrawlStampCap, s.ScrawlStampSize) = (44, 150);
        HitLeft(e);   // the Stamp size moved since start: the walls start over at the new size
        var field = BouncingTextOverlay.Scrawl!;
        Assert.Equal(e.FontSize * 1.5, field.FontSize, 3);
        for (int i = 0; i < 60; i++) HitLeft(e);
        int cap = ScrawlOptions.EffectiveCap(44, field.FontSize / BouncingTextEngine.BaseFontSize * 100, e.Logos.Count);
        Assert.True(cap < 44);
        Assert.Equal(cap, field.Cap);
        Assert.Equal(cap, field.Stamps.Count);
        s.ScrawlStampSize = 100;
        HitLeft(e);
        Assert.NotSame(field, BouncingTextOverlay.Scrawl);
        Assert.Empty(BouncingTextOverlay.Scrawl!.Stamps);
        HitLeft(e);
        Assert.Single(BouncingTextOverlay.Scrawl!.Stamps);
        // The stamps go on the OCR skip list with the words.
        Assert.True(BouncingTextOverlay.ActiveTextRects().Count > e.Logos.Count);
    });

    [Fact]
    public Task The_corner_word_follows_corner_flash_and_the_gold_corner_blasts_the_stage() => WithWords(premium: true, s =>
    {
        var e = BouncingTextOverlay.Engine;
        var blasts = new List<Blast>();
        void Seen(Blast b) => blasts.Add(b);
        SuperStageHost.Stage.Blasted += Seen;
        try
        {
            void Corner()
            {
                var l = e.Logos[0];
                (l.PosX, l.PosY, l.VelX, l.VelY) = (e.MinX - 1, e.MinY - 1, -100, -100);
                BouncingTextOverlay.Advance(0.016);
            }
            s.BouncingTextFxCornerBurst = false;
            BouncingTextOverlay.Advance(0.016);   // the option box is read once a frame, as on WPF
            Corner();
            var field = BouncingTextOverlay.Scrawl!;
            Assert.Contains(field.Stamps, st => st.Gold);
            Assert.Null(field.LabelText);
            s.BouncingTextFxCornerBurst = true;
            BouncingTextOverlay.Advance(0.016);
            Corner();
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("super_scrawl_corner"), field.LabelText);
            Assert.Contains(blasts, b => b.Kind == BlastKind.GoldCorner && b.ReachPx == 0);
        }
        finally { SuperStageHost.Stage.Blasted -= Seen; }
    });

    [Fact]
    public Task The_word_sweeps_the_stage_only_while_moving_effects_pop() => WithWords(premium: true, s =>
    {
        var e = BouncingTextOverlay.Engine;
        var swept = new List<(double L, double T, double R, double B)>();
        void Seen(double l, double t, double r, double b) => swept.Add((l, t, r, b));
        SuperStageHost.Stage.Swept += Seen;
        try
        {
            var w = e.Logos[0];
            (w.PosX, w.PosY, w.VelX, w.VelY) = (600, 400, 10, 10);
            BouncingTextOverlay.Advance(0.016);
            Assert.Empty(swept);
            s.MovingEffectsPop = true;
            (w.PosX, w.PosY) = (600, 400);
            BouncingTextOverlay.Advance(0.016);
            var box = Assert.Single(swept);
            Assert.Equal((600.0, 400.0, 600 + w.TextWidth, 400 + w.TextHeight), box);
        }
        finally { SuperStageHost.Stage.Swept -= Seen; }
    });

    [Fact]
    public Task Panic_takes_the_stamps_down_at_once() => WithWords(premium: true, _ =>
    {
        HitLeft(BouncingTextOverlay.Engine);
        var field = BouncingTextOverlay.Scrawl!;
        Assert.NotEmpty(field.Stamps);
        PanicSurfaces.All.Single(p => p.Id == "bouncing-text").Stop(null);
        Assert.False(BouncingTextOverlay.IsRunning);
        Assert.Null(BouncingTextOverlay.Scrawl);
        Assert.Empty(field.Stamps);
        Assert.Equal(0, field.ShakeX);
    });

    [Fact]
    public Task Photosafe_never_shakes() => WithWords(premium: true, s =>
    {
        var e = BouncingTextOverlay.Engine;
        s.LockdownPhotosafe = true;
        var l = e.Logos[0];
        (l.PosX, l.PosY, l.VelX, l.VelY) = (e.MinX - 1, e.MinY - 1, -100, -100);
        BouncingTextOverlay.Advance(0.016);
        var field = BouncingTextOverlay.Scrawl!;
        Assert.Equal((0.0, 0.0), (field.ShakeX, field.ShakeY));
    });

    [Fact]
    public void The_render_sample_draws_stamps() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var layer = (Control)Activator.CreateInstance(typeof(ScrawlLayer), nonPublic: true)!;
        layer.Measure(new Size(1280, 720));
        layer.Arrange(new Rect(0, 0, 1280, 720));
        using var bmp = new RenderTargetBitmap(new PixelSize(1280, 720));
        bmp.Render(layer);
        var px = new byte[1280 * 720 * 4];
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(px.Length);
        try
        {
            bmp.CopyPixels(new PixelRect(0, 0, 1280, 720), buffer, px.Length, 1280 * 4);
            System.Runtime.InteropServices.Marshal.Copy(buffer, px, 0, px.Length);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
        int lit = 0;
        for (int i = 3; i < px.Length; i += 4) if (px[i] > 0) lit++;
        Assert.True(lit > 2000, $"only {lit} pixels drawn");
    });
}
