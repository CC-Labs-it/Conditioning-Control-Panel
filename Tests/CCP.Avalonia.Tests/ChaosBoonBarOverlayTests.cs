using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services;
using Xunit;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;

namespace CCP.Avalonia.Tests;

/// <summary>The run-pick ribbon from a real run: born empty at BeginRun (WPF ChaosModeService :417),
/// one tile per drafted pick in order with only newcomers popping in, closed at run end and on panic.</summary>
public sealed class ChaosBoonBarOverlayTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static ChaosRunConfig Cfg() => new() { DurationSec = 60, WaveCount = 3 };

    // WPF BubbleService.cs:2066, ModResourceResolver.cs:374-377: current mod, WAV twin, fallback.
    [Theory]
    [InlineData("mp3")]
    [InlineData("wav")]
    public async Task Chime_uses_active_mod_audio_then_content_when_override_is_absent(string extension) {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var dir = System.IO.Directory.CreateTempSubdirectory("ccp-chime-");
            var previous = CoreModArt.AudioOverridePathProvider; var audio = CoreAudio.PlayOneShotProvider; var paths = new System.Collections.Generic.List<string>();
            try {
                var sounds = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir.FullName, "resources", "sounds"));
                for (int i = 1; i <= 3; i++) System.IO.File.WriteAllBytes(System.IO.Path.Combine(sounds.FullName, $"chime{i}.{extension}"), new byte[1]);
                CoreModArt.AudioOverridePathProvider = name => CoreModArt.ModAudioFile(name, dir.FullName);
                CoreAudio.PlayOneShotProvider = (path, _, tag, _, _) => { if (tag == "bubble-chime") paths.Add(path); }; BubbleOverlay.PlayChime(0.25f);
                var overridden = Assert.Single(paths); Assert.StartsWith(sounds.FullName, overridden); Assert.EndsWith("." + extension, overridden);
                foreach (var file in sounds.GetFiles()) file.Delete(); BubbleOverlay.PlayChime(0.25f);
                Assert.Equal(2, paths.Count); Assert.DoesNotContain(dir.FullName, paths[1]); Assert.EndsWith(".mp3", paths[1]);
            }
            finally { CoreModArt.AudioOverridePathProvider = previous; CoreAudio.PlayOneShotProvider = audio; dir.Delete(true); }
            return Task.CompletedTask; });
    }
    [Fact]
    public async Task Hub_only_equips_discovered_mantras_and_can_unequip_them() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var hub = new ChaosHubWindow(); var host = new Window { Width = 500, Height = 150 };
            Assert.Equal(ChaosBoonPool.All.Count, hub.FindControl<StackPanel>("MantrasHost")!.Children.Count); var boon = ChaosBoonPool.All.First(b => !b.IsCurse);
            var curse = ChaosBoonPool.All.First(b => b.IsCurse); var meta = ChaosMeta.State; var equipped = meta.EquippedStartBoon;
            var discovered = meta.DiscoveredCodexIds.ToArray();
            void Click(ChaosBoon b) {
                host.Content = hub.MantraRow(b); host.Show(); host.UpdateLayout(); host.MouseDown(new Point(30, 30), global::Avalonia.Input.MouseButton.Left);
                host.MouseUp(new Point(30, 30), global::Avalonia.Input.MouseButton.Left);
            }
            try {
                meta.EquippedStartBoon = null; meta.DiscoveredCodexIds.Remove("boon:" + boon.Id); Click(boon); Assert.Null(meta.EquippedStartBoon);
                ChaosMeta.MarkDiscovered("boon:" + boon.Id); Click(boon);
                Assert.Equal(boon.Id, meta.EquippedStartBoon); Assert.Equal(boon.Id, ChaosMetaStore.Load().EquippedStartBoon); ChaosMeta.MarkDiscovered("boon:" + curse.Id);
                Click(curse); Assert.Equal(boon.Id, meta.EquippedStartBoon); Click(boon);
                Assert.Null(meta.EquippedStartBoon); Assert.Null(ChaosMetaStore.Load().EquippedStartBoon);
            }
            finally {
                host.Close(); hub.Close(); meta.EquippedStartBoon = equipped; meta.DiscoveredCodexIds.Clear(); meta.DiscoveredCodexIds.UnionWith(discovered);
            }
            return Task.CompletedTask; });
    }
    // WPF ChaosModeService.cs:422-435, ChaosBoonBarOverlay.cs:179-192, ChaosArt.cs:58-69.
    [Fact]
    public async Task Equipped_welcome_shower_lands_with_art_and_treats_unless_scripted_or_invalid() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var resolver = ChaosRunState.IconResolver; ChaosRunState.IconResolver = null; var boon = ChaosBoonPool.All.First(b => b.Id == "welcome_shower");
            var meta = ChaosMeta.State; var equipped = meta.EquippedStartBoon; var audio = CoreAudio.PlayOneShotProvider;
            var chimes = new System.Collections.Generic.List<float>();
            CoreAudio.PlayOneShotProvider = (_, volume, tag, _, _) => { if (tag == "bubble-chime") chimes.Add(volume); };
            var path = System.IO.Path.Combine(global::ConditioningControlPanel.CorePaths.UserData,
                "assets", "assets", "Chaos", "boons", boon.Id + ".png"); var prior = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null; var cache = (System.Collections.Generic.Dictionary<string, global::Avalonia.Media.Imaging.Bitmap?>)typeof(ChaosEffectBannerOverlay).GetField("ArtCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!; var hadCached = cache.TryGetValue(path, out var priorArt); cache.Remove(path);
            try {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                using (var bitmap = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(2, 2),
                    new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul))
                    bitmap.Save(path); meta.EquippedStartBoon = boon.Id; ChaosRunHost.StartRun(Cfg()); ChaosRunHost.Overlay!.FinishCountdown();
                var pick = Assert.Single(ChaosRunHost.State!.RunPickTiles);
                Assert.Equal(boon.Id, pick.Id); Assert.Contains(ChaosRunHost.State.ActiveBoons, b => b.Id == boon.Id);
                var tile = (Border)Assert.Single(ChaosRunHost.BoonBar!.Row.Children); var image = Assert.IsType<Image>(((Panel)tile.Child!).Children[0]);
                Assert.Same(pick.Icon, image.Source); Assert.NotNull(image.Source); Assert.Same(image.Source, ChaosEffectBannerOverlay.ResolveArt(boon.Id, "boons"));
                Assert.Contains("boon:" + boon.Id, meta.DiscoveredCodexIds);
                // WPF ChaosModeService.cs:438-440,1645-1659: six opening treats, chime and feed.
                Assert.Equal(6, BubbleOverlay.ChaosCount);
                Assert.All(BubbleOverlay.Field.Bubbles.Where(b => b.Spec != null), b => {
                    Assert.False(b.Spec!.IsLive); Assert.Equal(ChaosMotion.RainDown, b.Spec.Motion);
                    var y = b.Y; b.Step(); Assert.True(b.Y > y); Assert.Contains(b.Spec.Payload.Kind, new[] { EffectBubblePayloadKind.Flash, EffectBubblePayloadKind.Subliminal });
                }); var settings = CoreSettings.Current;
                Assert.Equal((float)System.Math.Pow(settings.MasterVolume / 100f * (settings.BubblesVolume / 100f), 1.5) * 0.25f, Assert.Single(chimes));
                var feed = Assert.Single(ChaosRunHost.State.RecentEvents, e => e.EndsWith(Loc.Get("chaos_welcome_shower_event"), System.StringComparison.Ordinal));
                Assert.Equal("🚿", global::ConditioningControlPanel.Avalonia.Controls.IconText.Split(feed).Icons.Single()); // WPF :1659 marker
                ChaosRunHost.ForceShutdown(); var scripted = Cfg(); scripted.ScriptedFirstRun = true; ChaosRunHost.StartRun(scripted); ChaosRunHost.Overlay!.FinishCountdown();
                Assert.Empty(ChaosRunHost.State!.RunPickTiles); Assert.Equal(0, BubbleOverlay.ChaosCount); Assert.Single(chimes); Assert.False(ChaosRunHost.BoonBar!.Pill.IsVisible);
                ChaosRunHost.ForceShutdown(); meta.EquippedStartBoon = "unknown-boon"; ChaosRunHost.StartRun(Cfg()); ChaosRunHost.Overlay!.FinishCountdown();
                Assert.Empty(ChaosRunHost.State!.RunPickTiles); Assert.Equal(0, BubbleOverlay.ChaosCount); Assert.Single(chimes); Assert.Null(ChaosEffectBannerOverlay.ResolveArt("unknown-boon", "boons"));
                ChaosRunHost.State.ApplyBoon(ChaosBoonPool.All.First(b => b.IsCurse)); var fallback = (Border)Assert.Single(ChaosRunHost.BoonBar!.Row.Children);
                Assert.IsType<global::ConditioningControlPanel.Avalonia.Controls.IconGlyph>(((Panel)fallback.Child!).Children[0]);
            }
            finally {
                ChaosRunHost.ForceShutdown(); meta.EquippedStartBoon = equipped; CoreAudio.PlayOneShotProvider = audio; ChaosRunState.IconResolver = resolver;
                if (prior == null) System.IO.File.Delete(path); else System.IO.File.WriteAllBytes(path, prior); if (hadCached) cache[path] = priorArt; else cache.Remove(path);
            }
            return Task.CompletedTask; });
    }
    [Fact] public async Task Closing_a_draft_retires_its_callback_and_timers() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var overlay = new ChaosOverlayWindow(); overlay.Show(); int picks = 0;
            overlay.ShowBoonDraft(1, ChaosBoonPool.All.Take(3).ToList(), _ => picks++, 30); overlay.Close();
            typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(overlay, new object?[] { null }); Assert.Equal(0, picks);
            foreach (var name in new[] { "_revealTimer", "_autoResumeTimer", "_confirmTimer", "_onBoonPick" })
                Assert.Null(typeof(ChaosOverlayWindow).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(overlay));
            return Task.CompletedTask; });
    }
    [Fact] public async Task Unsupported_saved_mantra_is_not_applied_or_offered_in_the_hub() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var meta = ChaosMeta.State; var old = meta.EquippedStartBoon; var hub = new ChaosHubWindow();
            try {
                meta.EquippedStartBoon = "aftermath"; Assert.False(hub.MantraRow(ChaosBoonPool.All.First(b => b.Id == "aftermath")).IsVisible);
                ChaosRunHost.StartRun(Cfg()); ChaosRunHost.Overlay!.FinishCountdown();
                Assert.False(ChaosRunHost.State!.AftermathEnabled); Assert.Empty(ChaosRunHost.State.RunPickTiles);
            } finally { ChaosRunHost.ForceShutdown(); hub.Close(); meta.EquippedStartBoon = old; }
            return Task.CompletedTask; });
    }
    [Fact] public async Task Draft_does_not_deal_unimplemented_effects() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var old = ChaosMeta.State.EquippedStartBoon;
            try {
                ChaosMeta.State.EquippedStartBoon = null; ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, DraftChoices = 4, AllowCurses = false });
                var state = ChaosRunHost.State!; foreach (var id in new[] { "size_queen", "welcome_shower" }) state.ApplyBoon(ChaosBoonPool.All.First(b => b.Id == id));
                var deal = typeof(ChaosRunHost).GetMethod("DealDraft", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var cards = (System.Collections.Generic.List<ChaosBoon>)deal.Invoke(null, null)!;
                Assert.NotEmpty(cards); Assert.All(cards, b => Assert.Contains(b.Id, new[] { "defuse_chain", "golden_touch", "extra_shield" }));
            } finally { ChaosRunHost.ForceShutdown(); ChaosMeta.State.EquippedStartBoon = old; }
            return Task.CompletedTask; });
    }
    [Fact] public async Task Old_draft_callbacks_cannot_change_a_new_run() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var old = ChaosMeta.State.EquippedStartBoon; ChaosMeta.State.EquippedStartBoon = null;
            try {
                void Draft() { ChaosRunHost.StartRun(Cfg()); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); ChaosRunHost.State!.RerollsLeft = 1; for (int i = 0; i < 80; i++) ChaosRunHost.Tick(); }
                Draft(); var overlay = ChaosRunHost.Overlay!; var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var pick = (System.Action<ChaosBoon?>)typeof(ChaosOverlayWindow).GetField("_onBoonPick", flags)!.GetValue(overlay)!;
                var reroll = (System.Func<(System.Collections.Generic.List<ChaosBoon> options, int rerollsLeft)?>)typeof(ChaosOverlayWindow).GetField("_rerollFunc", flags)!.GetValue(overlay)!;
                ChaosRunHost.ForceShutdown(); Draft(); var state = ChaosRunHost.State!; int shields = state.Shields;
                Assert.Null(reroll()); pick(null); Assert.Equal(1, state.RerollsLeft); Assert.Equal(1, state.WaveIndex); Assert.Equal(shields, state.Shields);
            } finally { ChaosRunHost.ForceShutdown(); ChaosMeta.State.EquippedStartBoon = old; }
            return Task.CompletedTask; });
    }
    [Fact] public async Task Selected_sin_announcement_has_the_skull() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var old = ChaosMeta.State.EquippedStartBoon; var enabled = CoreSettings.Current.ChaosAnnouncerEnabled;
            try {
                ChaosMeta.State.EquippedStartBoon = null; CoreSettings.Current.ChaosAnnouncerEnabled = true;
                ChaosRunHost.StartRun(Cfg()); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); for (int i = 0; i < 80; i++) ChaosRunHost.Tick();
                var sin = ChaosBoonPool.All.First(b => b.Id == "hair_trigger"); ChaosAnnouncerOverlay.CloseActive();
                typeof(ChaosRunHost).GetMethod("OnBoonChosen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object?[] { sin });
                Assert.Equal("☠ " + sin.Name, ChaosAnnouncerOverlay.Active!.Current!.Text);
            } finally { ChaosRunHost.ForceShutdown(); ChaosMeta.State.EquippedStartBoon = old; CoreSettings.Current.ChaosAnnouncerEnabled = enabled; }
            return Task.CompletedTask; });
    }
    // WPF ChaosModeService.cs:2034-2036 and BubbleService.cs:1669-1680,1704-1717.
    [Fact]
    public async Task Equipped_size_queen_snap_expands_a_treat_only_ripple_and_panic_clears_it() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var meta = ChaosMeta.State; var equipped = meta.EquippedStartBoon; var oldClock = BubbleOverlay.Field.Clock; var t = System.DateTime.UnixEpoch;
            try {
                meta.EquippedStartBoon = "size_queen"; ChaosRunHost.StartRun(Cfg()); ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!; state.Focus = 100; BubbleOverlay.Field.Clock = () => t = t.AddMilliseconds(32);
                var snap = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsLive = true, FuseMs = 5000, SizePx = 40 }); BubbleOverlay.Press(snap, snap.CenterX, snap.CenterY);
                for (int i = 0; i < 40 && !snap.Popping; i++) BubbleOverlay.Field.Step(); Assert.Equal(1, state.Defused); Assert.True(state.RippleEnabled);
                AmbientBubble Place(double dx, EffectBubbleSpec spec) {
                    var b = BubbleOverlay.SpawnChaos(spec); b.X = snap.CenterX + dx - b.Size / 2; b.Y = snap.CenterY - b.Size / 2; return b;
                }
                var treat = Place(50, new EffectBubbleSpec { SizePx = 20 }); var outside = Place(500, new EffectBubbleSpec { SizePx = 20 });
                var live = Place(10, new EffectBubbleSpec { IsLive = true, FuseMs = 5000, SizePx = 20 });
                var freeze = Place(10, new EffectBubbleSpec { IsFreeze = true, SizePx = 20 }); var darter = Place(10, new EffectBubbleSpec { IsDarter = true, SizePx = 20 });
                var before = state.Score; BubbleOverlay.HoldChaos(true); for (int i = 0; i < 20; i++) BubbleOverlay.StepSizeQueenRipples(); Assert.False(treat.Popping);
                BubbleOverlay.HoldChaos(false); BubbleOverlay.StepSizeQueenRipples(); Assert.False(treat.Popping); BubbleOverlay.StepSizeQueenRipples();
                Assert.True(treat.Popping); Assert.True(state.Score > before); for (int i = 0; i < 20; i++) BubbleOverlay.StepSizeQueenRipples();
                Assert.False(outside.Popping); Assert.False(live.Popping); Assert.False(freeze.Popping); Assert.False(darter.Popping);
                BubbleOverlay.TriggerSizeQueenRipple(snap); BubbleOverlay.TriggerSizeQueenRipple(snap); BubbleOverlay.BeginChaos(ChaosRunHost.Overlay!, _ => ChaosRunHost.ForceShutdown()); var exiting = Place(10, new EffectBubbleSpec { SizePx = 20 }); BubbleOverlay.StepSizeQueenRipples(); Assert.True(exiting.Popping); Assert.False(ChaosRunHost.IsActive);
                var after = Place(10, new EffectBubbleSpec { SizePx = 20 }); BubbleOverlay.StepSizeQueenRipples(); Assert.False(after.Popping);
            }
            finally { ChaosRunHost.ForceShutdown(); BubbleOverlay.Field.Bubbles.Clear(); BubbleOverlay.Field.Clock = oldClock; meta.EquippedStartBoon = equipped; }
            return Task.CompletedTask; });
    }

    // WPF ChaosModeService.cs:1441-1483,1507-1526,1529-1621; ChaosHubWindow.xaml.cs:1259-1341.
    [Fact]
    public async Task Fresh_fall_deals_discoverable_mantras_and_selection_persists() {
        await AvaloniaTestDispatcher.RunAsync(async () => {
            Setup(); var before = ChaosMeta.State; var service = new SettingsService(); CoreSettings.ServiceProvider = () => service; var settings = service.Current;
            ChaosHubWindow? hub = null; Window? clickHost = null;
            try {
                ChaosMetaStore.Save(new ChaosMetaState()); ChaosMeta.Init(); Assert.Empty(ChaosMeta.State.DiscoveredCodexIds);
                settings.ChaosRunDurationSec = 120; settings.ChaosWaveCount = 3; settings.ChaosBoonDraftEnabled = true;
                hub = new ChaosHubWindow(); hub.FallIn(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var overlay = ChaosRunHost.Overlay!; var state = ChaosRunHost.State!;
                overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests(); var priorBubble = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsLive = true, FuseMs = 5000 }); BubbleOverlay.Press(priorBubble, priorBubble.CenterX, priorBubble.CenterY); Assert.True(BubbleOverlay.Field.Held);
                for (int i = 0; i < 4 * 1200 && !overlay.FindControl<Border>("DraftPanel")!.IsVisible; i++) ChaosRunHost.Tick();
                Assert.True(overlay.FindControl<Border>("DraftPanel")!.IsVisible); Assert.DoesNotContain(priorBubble, BubbleOverlay.Field.Bubbles); Assert.False(BubbleOverlay.Field.Held); var boundary = state.ElapsedSec;
                Assert.InRange(boundary, (double)state.RunDurationSec / state.WaveCount, (double)state.RunDurationSec / state.WaveCount + 0.25); Assert.Equal(1, state.WaveIndex);
                ChaosRunHost.Tick(); Assert.Equal(boundary, state.ElapsedSec); var cards = overlay.FindControl<StackPanel>("BoonCardHost")!;
                Assert.Equal(3, cards.Children.Count); var panel = (StackPanel)((Border)cards.Children[0]).Child!;
                var description = panel.Children.OfType<TextBlock>().Select(x => x.Text).First(x => x != null); var chosen = ChaosBoonPool.All.First(b => b.Desc == description);
                Assert.True(ChaosMeta.IsDiscovered("boon:" + chosen.Id)); Assert.Contains("boon:" + chosen.Id, ChaosMetaStore.Load().DiscoveredCodexIds);
                var accept = Assert.Single(panel.Children.OfType<Button>());
                Assert.True(accept.IsEnabled); accept.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.False(accept.IsEnabled);
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (state.RunPickTiles.Count == 0 && wait.Elapsed < System.TimeSpan.FromSeconds(10)) await Task.Delay(20);
                Assert.Equal(chosen.Id, Assert.Single(state.RunPickTiles).Id); Assert.Equal(2, state.WaveIndex); ChaosRunHost.Tick(); Assert.Equal(boundary, state.ElapsedSec);
                overlay.FinishCountdown(); ChaosUnlockCardOverlay.Active?.Dismiss(); wait.Restart(); while (ChaosUnlockCardOverlay.IsShowing && wait.Elapsed < System.TimeSpan.FromSeconds(10)) await Task.Delay(20); Assert.False(ChaosUnlockCardOverlay.IsShowing); ChaosRunHost.Tick(); Assert.True(state.ElapsedSec > boundary);
                ChaosRunHost.ForceShutdown(); hub = new ChaosHubWindow();
                clickHost = new Window { Width = 500, Height = 150, Content = hub.MantraRow(chosen) }; clickHost.Show(); clickHost.UpdateLayout();
                clickHost.MouseDown(new Point(30, 30), global::Avalonia.Input.MouseButton.Left);
                clickHost.MouseUp(new Point(30, 30), global::Avalonia.Input.MouseButton.Left);
                Assert.Equal(chosen.Id, ChaosMeta.State.EquippedStartBoon);
                Assert.Equal(chosen.Id, ChaosMetaStore.Load().EquippedStartBoon);
                clickHost.Close(); clickHost = null; hub.Close(); hub = null;
                ChaosMeta.State.RunsCompleted = 2; // WPF reveal gate makes skip available on run 3
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true, SinChance = 0 });
                overlay = ChaosRunHost.Overlay!; state = ChaosRunHost.State!; state.RerollsLeft = 1;
                overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests();
                for (int i = 0; i < 80; i++) ChaosRunHost.Tick();
                var reroll = overlay.FindControl<Button>("BtnReroll")!; Assert.True(reroll.IsVisible);
                reroll.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Equal(0, state.RerollsLeft);
                cards = overlay.FindControl<StackPanel>("BoonCardHost")!; Assert.All(cards.Children.OfType<Border>(), c => { var d = ((StackPanel)c.Child!).Children.OfType<TextBlock>().Select(x => x.Text).First(x => x != null); Assert.Contains("boon:" + ChaosBoonPool.All.First(b => b.Desc == d).Id, ChaosMetaStore.Load().DiscoveredCodexIds); });
                int shields = state.Shields; var skip = overlay.FindControl<Button>("BtnSkipBoon")!; Assert.True(skip.IsVisible);
                skip.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(shields + 1, state.Shields); Assert.Equal(2, state.WaveIndex);
                ChaosRunHost.RequestStop(); overlay.FinishCountdown();
                Assert.False(ChaosRunHost.IsDescending); Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                ChaosRunHost.ForceShutdown(); ChaosMeta.State.EquippedStartBoon = "";
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true, AllowCurses = true, SinChance = 1 }); overlay = ChaosRunHost.Overlay!; state = ChaosRunHost.State!; state.MaxedBoons.Add("surrender"); state.SinExtraMult = 0.15;
                overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests(); for (int i = 0; i < 80; i++) ChaosRunHost.Tick();
                cards = overlay.FindControl<StackPanel>("BoonCardHost")!; var sinPanel = (StackPanel)((Border)cards.Children.Last()).Child!; var giveIn = Assert.Single(sinPanel.Children.OfType<Button>()); wait.Restart(); while (!giveIn.IsEnabled && wait.Elapsed < System.TimeSpan.FromSeconds(10)) await Task.Delay(20); Assert.True(giveIn.IsEnabled);
                int oldShields = state.Shields; giveIn.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); wait.Restart(); while (state.ActiveCurses.Count == 0 && wait.Elapsed < System.TimeSpan.FromSeconds(10)) await Task.Delay(20); Assert.Single(state.ActiveCurses); Assert.True(state.SurrenderShieldUsed); Assert.Equal(oldShields + 1, state.Shields); Assert.True(state.BoonMult >= 1.15); Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes));
                ChaosRunHost.ForceShutdown(); ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true }); overlay = ChaosRunHost.Overlay!; overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests(); for (int i = 0; i < 80; i++) ChaosRunHost.Tick(); ChaosRunHost.RequestStop(); overlay.FindControl<Button>("BtnSkipBoon")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
            }
            finally { ChaosRunHost.ForceShutdown(); clickHost?.Close(); hub?.Close(); ChaosMetaStore.Save(before); ChaosMeta.Init(); }
        });
    }

    [Fact]
    public async Task Picks_land_live_in_order_and_panic_closes_the_ribbon()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                Assert.Null(ChaosRunHost.BoonBar);   // countdown: not yet
                ChaosRunHost.Overlay!.FinishCountdown();
                var bar = ChaosRunHost.BoonBar!;
                Assert.True(bar.IsVisible);
                Assert.False(bar.Pill.IsVisible);    // no picks: pill collapsed

                var state = ChaosRunHost.State!;
                var boon = ChaosBoonPool.All.First(b => !b.IsCurse);
                var curse = ChaosBoonPool.All.First(b => b.IsCurse);
                state.ApplyBoon(boon);
                Assert.True(bar.Pill.IsVisible);
                Assert.Equal(0, Assert.Single(bar.Row.Children).Opacity);   // newcomer pops in from 0
                bar.StepPops(1000);
                Assert.Equal(1, bar.Row.Children[0].Opacity);

                state.ApplyBoon(curse);
                Assert.Equal(2, bar.Row.Children.Count);
                Assert.Equal(1, bar.Row.Children[0].Opacity);   // already shown: not re-animated
                Assert.Equal(0, bar.Row.Children[1].Opacity);
                Assert.Equal(new[] { boon.Name, curse.Name },
                    bar.Row.Children.Select(c => ((TextBlock)((StackPanel)ToolTip.GetTip(c)!).Children[0]).Text));

                ChaosRunHost.ForceShutdown();   // the 'chaos' panic surface
                Assert.Null(ChaosRunHost.BoonBar);
                Assert.False(bar.IsVisible);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Run_end_closes_the_ribbon()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            bool pin = global::ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop;
            try
            {
                global::ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop = false;   // WPF BornTopmost follows the pin
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var bar = ChaosRunHost.BoonBar!;
                Assert.False(bar.Topmost);
                ChaosRunHost.RequestStop();   // WPF EndRun :3235
                Assert.Null(ChaosRunHost.BoonBar);
                Assert.False(bar.IsVisible);
            }
            finally { ChaosRunHost.ForceShutdown(); global::ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop = pin; }
            return Task.CompletedTask;
        });
    }
}
