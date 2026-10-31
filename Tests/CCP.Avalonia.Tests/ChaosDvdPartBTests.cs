using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class ChaosDvdPartBTests {
    private static void Setup() { if (Application.Current == null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting(); }
    [Fact]
    public async Task NoCompositorCaseRestoresSharedHost() { var saved = ChaosDvdOverlay.SharedHost; Func<bool> sentinel = () => saved != null; ChaosDvdOverlay.SharedHost = sentinel;
        try { await new ChaosDvdOverlayTests().NoCompositorMeansNoWindowAndNoLogo(); Assert.Same(sentinel, ChaosDvdOverlay.SharedHost); } finally { ChaosDvdOverlay.SharedHost = saved; } }
    // WPF ChaosModeService.cs:2819-2832,2996-3005; ChaosDvdOverlay.cs:103-107,204-239,305-331.
    [Fact]
    public async Task EquippedThoughtsUsePoolCadencePauseCapAndRunReset() { await AvaloniaTestDispatcher.RunAsync(() => { Setup();
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosDvdOverlay.SharedHost, ChaosEffectBannerOverlay.SkipPlatformChecksForTests);
            var pool = CoreSettings.Current.BouncingTextPool; var language = LocalizationManager.Instance.CurrentLanguage; var meta = ChaosMeta.State; var json = JsonConvert.SerializeObject(meta); try
            { ChaosDvdOverlay.SharedHost = () => true; ChaosDvdOverlay.CanOverlay = () => true; ChaosDvdOverlay.PrepareWindow = (_, _) => true; ChaosDvdOverlay.PlaySfx = (_, _) => { };
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true; CoreSettings.Current.BouncingTextPool = new() { ["enabled"] = true, ["disabled"] = false };
                meta.ActiveLifetimeBoons.Clear(); meta.ActiveLifetimeBoons.Add("intrusive_thoughts"); meta.LifetimeBoonLevels["intrusive_thoughts"] = 4; void Start() {
                    ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 600, WaveCount = 3 }); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); } Start();
                Assert.True(ChaosRunHost.State!.IntrusiveThoughtsSec > 0); var state = ChaosRunHost.State; Assert.False(ChaosDvdOverlay.SpankerRedirect()); state.SpankerActive = true;
                Assert.False(ChaosDvdOverlay.SpankerRedirect()); state.MaxedBoons.Add("the_spanker"); Assert.True(ChaosDvdOverlay.SpankerRedirect()); state.SpankerActive = false;
                Assert.False(ChaosDvdOverlay.SpankerRedirect()); state.MaxedBoons.Remove("the_spanker"); for (int i = 0; i < 19; i++) ChaosRunHost.Tick(); Assert.False(ChaosDvdOverlay.AnyActive);
                ChaosRunHost.ToggleManualPause(); for (int i = 0; i < 20; i++) ChaosRunHost.Tick(); Assert.False(ChaosDvdOverlay.AnyActive); ChaosRunHost.ToggleManualPause(); ChaosRunHost.Tick();
                var logo = Assert.Single(ChaosDvdOverlay.Flock.Active); Assert.Equal("enabled", logo.Text); Assert.Equal(ChaosRunHost.State.IntrusiveThoughtsSec, logo.RemainingSec);
                Assert.Equal(0.8, logo.FontScale); Assert.Equal(230 * 1.3, Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy), 6); Assert.True(logo.SplitOnRabbit);
                Assert.False(ChaosDvdOverlay.AnyToyActive); CoreSettings.Current.BouncingTextPool.Clear(); for (int i = 0; i < 20; i++) ChaosRunHost.Tick();
                Assert.Equal("GIVE IN", ChaosDvdOverlay.Flock.Active[1].Text); foreach (var (lang, expected) in new[] { ("en", "GIVE IN"), ("de", "GIB NACH"), ("es", "CEDE"), ("fr", "CÈDE"), ("it", "CEDI"),
                    ("ja", "身をゆだねて"), ("ko", "맡겨 봐"), ("pt-BR", "CEDA"), ("ru", "ПОДДАЙСЯ"), ("zh-CN", "放下抵抗") }) {
                    LocalizationManager.Instance.SetLanguage(lang); Assert.Equal(expected, Loc.Get("chaos_thought_fallback")); for (int i = 0; i < 20; i++) ChaosRunHost.Tick();
                    Assert.Equal(Loc.Get("chaos_thought_fallback"), ChaosDvdOverlay.Flock.Active[^1].Text); Assert.NotEqual("chaos_thought_fallback", Loc.Get("chaos_thought_fallback"));
                    ChaosDvdOverlay.CloseActive(); } for (int i = 0; i < 200; i++) ChaosRunHost.Tick(); Assert.Equal(8, ChaosDvdOverlay.Flock.Active.Count);
                for (int i = 0; i < 19; i++) ChaosRunHost.Tick(); ChaosRunHost.ForceShutdown(); Assert.False(ChaosDvdOverlay.AnyActive); Start(); ChaosRunHost.Tick();
                Assert.False(ChaosDvdOverlay.AnyActive); // prior partial interval cannot leak
            } finally { ChaosRunHost.ForceShutdown(); LocalizationManager.Instance.SetLanguage(language); CoreSettings.Current.BouncingTextPool = pool;
                JsonConvert.PopulateObject(json, meta, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosDvdOverlay.SharedHost, ChaosEffectBannerOverlay.SkipPlatformChecksForTests) = saved; }
            return Task.CompletedTask; }); }
    // WPF ChaosDvdOverlay.cs:164,473-484; ChaosWindowZ.cs:35,50. Exercise real preparation flags.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeLogoPreparationPreservesPassiveInputAndPin(bool pin, bool clickable) { await AvaloniaTestDispatcher.RunAsync(() => { Setup();
            var saved = (ChaosDvdOverlay.PrepareNativeWindow, CoreSettings.Current.ChaosPinOnTop); var window = new Window { Topmost = pin }; bool called = false; try {
                CoreSettings.Current.ChaosPinOnTop = pin; ChaosDvdOverlay.PrepareNativeWindow = (w, _, through, passive, topmost) => {
                    called = true; Assert.Equal(!clickable, through); Assert.True(passive); Assert.Equal(pin, topmost); uint style = Win32Overlay.Style(0, through, passive);
                    style = Win32Overlay.Style(style, through, passive); // Avalonia style rebuild
                    Assert.NotEqual(0u, style & Win32Overlay.WsExNoActivate); Assert.Equal(clickable ? 0u : Win32Overlay.WsExTransparent, style & Win32Overlay.WsExTransparent);
                    Assert.Equal(new IntPtr(pin ? -1 : -2), Win32Overlay.StackAfter(topmost)); return true; };
                Assert.True(ChaosDvdOverlay.PrepareLogoWindow(window, new PixelRect(0, 0, 100, 50), clickable)); Assert.True(called); }
            finally { window.Close(); (ChaosDvdOverlay.PrepareNativeWindow, CoreSettings.Current.ChaosPinOnTop) = saved; } return Task.CompletedTask; }); }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HostsAndSpankerInputFollowLatchedGates(bool shared, bool spanker) { await AvaloniaTestDispatcher.RunAsync(() => { Setup();
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PrepareLogoWindow,
                ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.SpankerRedirect, ChaosDvdOverlay.PlaySfx); var host = new Window(); host.Show(); var windows = new List<Window>(); int cues = 0;
            var oldPin = CoreSettings.Current.ChaosPinOnTop; CoreSettings.Current.ChaosPinOnTop = false; try { ChaosDvdOverlay.SharedHost = () => true; ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true; ChaosDvdOverlay.PrepareLogoWindow = (w, _, input) =>
                { Assert.False(w.Topmost); Assert.Equal(spanker, input); windows.Add(w); return true; }; ChaosDvdOverlay.SharedHost = () => shared; ChaosDvdOverlay.SpankerRedirect = () => spanker;
                ChaosDvdOverlay.PlaySfx = (key, volume) => { Assert.Equal("dvd_bounce", key); Assert.Equal(0.4f, volume); cues++; }; ChaosDvdOverlay.Launch(host, 10, 1, 1);
                var logo = Assert.Single(ChaosDvdOverlay.Flock.Active); Assert.Equal(shared && !spanker ? 0 : 1, windows.Count); if (windows.Count > 0) { var w = windows[0];
                    Assert.Equal(spanker, w.IsHitTestVisible); Assert.False(w.ShowActivated); Assert.Equal(logo.W, w.Width);
                    logo.X = logo.Y = 100; // keep the motion check away from random wall bounces
                    ChaosDvdOverlay.Advance(0); var before = w.Position; ChaosDvdOverlay.Advance(0.05); Assert.NotEqual(before, w.Position); if (spanker) {
                        var panel = Assert.IsType<Border>(w.Content); var hue = logo.HueIndex; ChaosDvdOverlay.SpankerRedirect = () => false; // gate sampled at launch
                        panel.RaiseEvent(new PointerPressedEventArgs(panel, new Pointer(1, PointerType.Mouse, true), panel,
                            new Point(1, 1), 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
                        Assert.Equal((hue + 1) % 6, logo.HueIndex); Assert.Equal(230 * 1.18, Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy), 6); Assert.Equal(1, cues); } }
                Assert.Equal(spanker, ChaosDvdOverlay.Smack(logo)); logo.RemainingSec = 0; ChaosDvdOverlay.Advance(0.05);
                Assert.Equal(spanker, ChaosDvdOverlay.Smack(logo)); // fading WPF logos still answer input
                ChaosDvdOverlay.Advance(0.3); Assert.Empty(ChaosDvdOverlay.LogoWindows); Assert.All(windows, w => Assert.False(w.IsVisible)); ChaosDvdOverlay.CloseActive();
                ChaosDvdOverlay.SharedHost = () => false; ChaosDvdOverlay.PrepareLogoWindow = (_, _, _) => false; ChaosDvdOverlay.Launch(host, 5, 1, 1);
                Assert.False(ChaosDvdOverlay.AnyActive); // refusal cannot leave a stuck toy glow
                Assert.Empty(ChaosDvdOverlay.LogoWindows); } finally { CoreSettings.Current.ChaosPinOnTop = oldPin; ChaosDvdOverlay.CloseActive(); host.Close();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PrepareLogoWindow,
                    ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.SpankerRedirect, ChaosDvdOverlay.PlaySfx) = saved; } return Task.CompletedTask; }); }
    // WPF VideoService.cs:5822 -> ChaosModeService.cs:745-757.
    [Fact]
    public async Task VideoSurfaceRaisesDvdOnlyDuringPinnedRun() { await AvaloniaTestDispatcher.RunAsync(() => { Setup(); var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow,
                ChaosDvdOverlay.RaiseWindow, ChaosEffectBannerOverlay.SkipPlatformChecksForTests, ChaosDvdOverlay.SharedHost); var pin = CoreSettings.Current.ChaosPinOnTop; Window? video = null; try {
                ChaosDvdOverlay.SharedHost = () => true; ChaosDvdOverlay.CanOverlay = () => true; ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true; CoreSettings.Current.ChaosPinOnTop = true; var raised = new List<Window>(); ChaosDvdOverlay.RaiseWindow = raised.Add;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 600, WaveCount = 3 }); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
                ChaosDvdOverlay.Launch(ChaosRunHost.Overlay, 5, 1, 1); video = MandatoryVideoOverlay.Instance.Build(null, false).Window; Assert.Contains(ChaosDvdOverlay.Window!, raised);
                raised.Clear(); CoreSettings.Current.ChaosPinOnTop = false; ChaosDvdOverlay.RaiseActive(); Assert.Empty(raised); CoreSettings.Current.ChaosPinOnTop = true;
                ChaosRunHost.ForceShutdown(); ChaosDvdOverlay.RaiseActive(); Assert.Empty(raised); } finally { video?.Close(); ChaosRunHost.ForceShutdown(); CoreSettings.Current.ChaosPinOnTop = pin;
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.RaiseWindow, ChaosEffectBannerOverlay.SkipPlatformChecksForTests, ChaosDvdOverlay.SharedHost) = saved; }
            return Task.CompletedTask; }); }
    // WPF ChaosDvdOverlay.cs:322-331,392-398; BubbleService.cs:1916-1924.
    [Fact]
    public async Task RabbitContactSplitsOnceAndCloneCanSplitAgain() { await AvaloniaTestDispatcher.RunAsync(() => { Setup();
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosDvdOverlay.SharedHost); var host = new Window(); host.Show(); try {
                ChaosDvdOverlay.SharedHost = () => true; ChaosDvdOverlay.CanOverlay = () => true; ChaosDvdOverlay.PrepareWindow = (_, _) => true; ChaosDvdOverlay.PlaySfx = (_, _) => { };
                BubbleOverlay.BeginChaos(host, _ => { }); ChaosDvdOverlay.Launch(host, 5, 1, 0.8, text: "thought", splitOnRabbit: true); var logo = Assert.Single(ChaosDvdOverlay.Flock.Active);
                logo.X = 100; logo.Y = 100; logo.Vx = 10; logo.Vy = 0; var rabbit = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsDarter = true });
                rabbit.X = 100; rabbit.Y = 100; rabbit.Size = 20; ChaosDvdOverlay.Advance(0.05); Assert.Equal(2, ChaosDvdOverlay.Flock.Active.Count); Assert.True(logo.SplitSpent);
                var clone = ChaosDvdOverlay.Flock.Active[1]; Assert.False(clone.SplitSpent); Assert.Equal(6.95, logo.RemainingSec, 6); Assert.Equal(logo.RemainingSec, clone.RemainingSec);
                Assert.Equal(logo.Text, clone.Text); Assert.Equal(logo.X, clone.X); Assert.Equal(10, Math.Sqrt(clone.Vx * clone.Vx + clone.Vy * clone.Vy), 6); Assert.False(rabbit.Popping);
                ChaosDvdOverlay.Advance(0.05); Assert.Equal(3, ChaosDvdOverlay.Flock.Active.Count); Assert.Equal(6.9, logo.RemainingSec, 6); // parent cannot split again
            } finally { BubbleOverlay.EndChaos(); ChaosDvdOverlay.CloseActive(); host.Close();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosDvdOverlay.SharedHost) = saved; } return Task.CompletedTask; }); } }
