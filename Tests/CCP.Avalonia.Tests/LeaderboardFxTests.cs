using System;
using System.Linq;
using System.IO;
using Avalonia.Media;
using Avalonia.Diagnostics;
using Avalonia.Data;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LeaderboardFxTests
{
    private sealed class BoardWire : HttpMessageHandler
    {
        public string? Response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response ?? """
                { "entries": [
                    { "unified_id": "one", "display_name": "Winner", "total_xp_earned": 300 },
                    { "unified_id": "two", "display_name": "Second", "total_xp_earned": 200 },
                    { "unified_id": "three", "display_name": "Third", "total_xp_earned": 100 },
                    { "unified_id": "four", "display_name": "Fourth", "total_xp_earned": 50 }
                ], "total_users": 4 }
                """) });
    }

    private sealed class Time : TimeProvider
    {
        private long _ticks;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(double seconds) { _ticks += TimeSpan.FromSeconds(seconds).Ticks; BeatLoop.StepAll(); }
    }

    [Fact]
    public Task Podium_BreathesGoldOnSharedBeat_AndParksWithMotionVisibilityAndWindow() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (global::Avalonia.Application.Current == null)
                global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            // Other filtered suites can have started the shared test app without the shipped font collection.
            if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily("fonts:Inter#Inter"),
                    FontStyle.Normal, FontWeight.Bold), out _))
                FontManager.Current.AddFontCollection(new global::Avalonia.Fonts.Inter.InterFontCollection());
            var settings = CoreSettings.Current;
            var old = (settings.MotionLevel, settings.PerformanceMode, settings.OfflineMode, LeaderboardTabView.NewClient, BeatLoop.Time);
            var path = Path.Combine(CorePaths.UserData, "leaderboard_ranks.json");
            Assert.StartsWith(Path.GetTempPath(), path);
            var saved = File.Exists(path) ? File.ReadAllText(path) : null;
            Window? window = null;
            try
            {
                settings.MotionLevel = MotionLevel.Full;
                settings.PerformanceMode = settings.OfflineMode = false;
                LeaderboardTabView.NewClient = () => new LeaderboardClient(new BoardWire());
                var time = new Time();
                BeatLoop.Time = time;
                var tab = new LeaderboardTabView();
                window = new Window { Content = tab, Width = 1200, Height = 850 };
                window.Show(); window.Activate(); Dispatcher.UIThread.RunJobs();
                await tab.RefreshLeaderboardAsync();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var podium = tab.FindControl<ItemsControl>("PodiumHost")!;
                var cards = podium.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("lbpodium")).ToArray();
                Assert.Equal(3, cards.Length);
                Assert.All(cards, c => Assert.Null(c.Effect));
                var glow = Assert.Single(podium.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Name == "PodiumGoldGlow" && b.IsVisible));
                Assert.NotEqual(default, glow.BoxShadow);
                var loop = Assert.Single(BeatLoop.Running.Where(b => ReferenceEquals(b.Owner, glow)));
                Assert.True(loop.OnFrameBeat);
                Assert.Equal(.16, glow.Opacity, 3);
                var slots = podium.GetVisualDescendants().OfType<Grid>()
                    .Where(g => g.Classes.Contains("lbpodium-slot")).ToArray();
                Assert.Equal(3, slots.Length);
                Assert.All(slots, slot => Assert.Equal(0, slot.Opacity));
                time.Advance(.02);
                Assert.True(slots[0].Opacity > 0);
                Assert.Equal(0, slots[1].Opacity);
                time.Advance(.10);
                Assert.True(slots[0].Opacity > slots[2].Opacity);
                time.Advance(.22);
                Assert.All(slots, slot =>
                {
                    Assert.Equal(1, slot.Opacity);
                    Assert.Equal(0, ((TranslateTransform)slot.RenderTransform!).Y);
                });
                time.Advance(4.2 - .34);
                Assert.Equal(.36, glow.Opacity, 3);
                Dispatcher.UIThread.RunJobs();
                using (var frame = window.CaptureRenderedFrame())
                {
                    Assert.NotNull(frame);
                    var proof = Path.Combine(Path.GetTempPath(), "ccp-render-proofs", Environment.ProcessId.ToString());
                    Directory.CreateDirectory(proof);
                    frame.Save(Path.Combine(proof, "LeaderboardPodiumFx.png"));
                }
                time.Advance(4.2);
                Assert.Equal(.16, glow.Opacity, 3);

                settings.MotionLevel = MotionLevel.Reduced;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.DoesNotContain(loop, BeatLoop.Running);
                Assert.Equal(.28, glow.Opacity, 3);
                settings.MotionLevel = MotionLevel.Off;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.Equal(.28, glow.Opacity, 3);
                Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, glow));
                settings.MotionLevel = MotionLevel.Full;
                settings.PerformanceMode = true;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.Equal(.28, glow.Opacity, 3);
                Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, glow));
                settings.PerformanceMode = false;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Assert.True(glow.IsVisible);
                Assert.Contains(BeatLoop.Running, b => ReferenceEquals(b.Owner, glow));
                tab.IsVisible = false;
                Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, glow));
                Assert.Equal(.28, glow.Opacity, 3);
                tab.IsVisible = true;
                window.WindowState = WindowState.Minimized;
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, glow));
                window.WindowState = WindowState.Normal;
                window.Activate(); Dispatcher.UIThread.RunJobs();
                Assert.Contains(BeatLoop.Running, b => ReferenceEquals(b.Owner, glow));
                window.Close(); Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(BeatLoop.Running, b => ReferenceEquals(b.Owner, glow));
                Assert.False(tab.FindControl<Border>("SeasonRecapHost")!.IsVisible); // WPF retired seasons.
            }
            finally
            {
                window?.Close();
                (settings.MotionLevel, settings.PerformanceMode, settings.OfflineMode, LeaderboardTabView.NewClient, BeatLoop.Time) = old;
                if (saved == null) File.Delete(path); else File.WriteAllText(path, saved);
                LeaderboardRankSnapshotService.ResetForTests();
            }
        });

    [Fact]
    public Task RankWashes_OnlyOnRefresh_CappedAtTwelve_AndClearedBeforeRecycling() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current == null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            // Other filtered suites can have started the shared test app without the shipped font collection.
            if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily("fonts:Inter#Inter"),
                    FontStyle.Normal, FontWeight.Bold), out _))
                FontManager.Current.AddFontCollection(new global::Avalonia.Fonts.Inter.InterFontCollection());
            var settings = CoreSettings.Current;
            var old = (settings.MotionLevel, settings.PerformanceMode, settings.OfflineMode, LeaderboardTabView.NewClient, BeatLoop.Time);
            var path = Path.Combine(CorePaths.UserData, "leaderboard_ranks.json");
            Assert.StartsWith(Path.GetTempPath(), path);
            var saved = File.Exists(path) ? File.ReadAllText(path) : null;
            var oldId = CoreAccount.UnifiedUserId;
            Window? window = null;
            try
            {
                CoreAccount.UnifiedUserId = "fx10";
                Directory.CreateDirectory(CorePaths.UserData);
                File.Delete(path);
                LeaderboardRankSnapshotService.ResetForTests();
                settings.MotionLevel = MotionLevel.Full;
                settings.PerformanceMode = settings.OfflineMode = false;
                string Board(bool moved) => JsonConvert.SerializeObject(new
                {
                    entries = Enumerable.Range(1, moved ? 21 : 20).Select(i => new
                    {
                        unified_id = "fx" + i, display_name = "Subject " + i,
                        total_xp_earned = i == 21 ? 0 : moved && i > 3 ? 500 + i : 1000 - i
                    }), total_users = 21, your_rank = moved ? 14 : 10
                });
                var wire = new BoardWire { Response = Board(false) };
                LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
                var time = new Time(); BeatLoop.Time = time;
                var tab = new LeaderboardTabView();
                window = new Window { Content = tab, Width = 1200, Height = 1600 };
                window.Show(); window.Activate(); Dispatcher.UIThread.RunJobs();
                await tab.RefreshLeaderboardAsync();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var list = tab.FindControl<ListBox>("LstLeaderboard")!;
                ListBoxItem[] Rows() => list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                static bool HasWash(ListBoxItem row) =>
                    row.GetDiagnostic(ListBoxItem.BackgroundProperty).Priority == BindingPriority.LocalValue;
                Assert.All(Rows(), r => Assert.False(HasWash(r))); // NEW is not movement
                var youDelta = tab.FindControl<TextBlock>("TxtYouDelta")!;
                Assert.Equal(Loc.Get("lb_delta_new"), youDelta.Text);
                time.Advance(1);
                wire.Response = Board(true);
                await tab.RefreshLeaderboardAsync();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var decorated = Rows().Where(r => HasWash(r)).ToArray();
                Assert.Equal(12, decorated.Length);
                Assert.Equal("\u25BC4", youDelta.Text); // WPF Leaderboard.cs:1318-1327, read before the snapshot write
                var up = decorated.First(r => ((LeaderboardRow)r.DataContext!).DeltaState == "up");
                var down = decorated.First(r => ((LeaderboardRow)r.DataContext!).DeltaState == "down");
                Assert.NotEqual(((SolidColorBrush)up.Background!).Color, ((SolidColorBrush)down.Background!).Color);
                list.SelectedItem = up.DataContext;
                var border = up.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ContainerBorder");
                Assert.Same(up.Background, border.Background); // a wash must actually reach the template
                time.Advance(.07);
                Assert.Equal(.30, ((SolidColorBrush)decorated[0].Background!).Opacity, 3);
                Assert.Equal(0, ((SolidColorBrush)decorated[3].Background!).Opacity, 3); // stagger
                time.Advance(.35);
                Assert.False(HasWash(decorated[0])); // clear each row at its own completion, not the cascade's end
                Assert.True(HasWash(decorated[^1]));
                time.Advance(1);
                Assert.All(decorated, r => Assert.False(HasWash(r)));
                Assert.Equal("new", tab.RankedPage!.Entries!.Single(r => r.UnifiedId == "fx21").DeltaState);

                // Another successful refresh flashes movement against the same 12 h baseline.
                await tab.RefreshLeaderboardAsync();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.NotEmpty(Rows().Where(r => HasWash(r)));
                tab.FindControl<TextBox>("TxtLeaderboardSearch")!.Text = "Subject";
                Assert.All(decorated, r => Assert.False(HasWash(r)));
                Assert.All(Rows(), r => Assert.False(HasWash(r))); // search is not a refresh
                tab.FindControl<TextBox>("TxtLeaderboardSearch")!.Text = "";
                await tab.RefreshLeaderboardAsync();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var current = Rows().Where(r => HasWash(r)).ToArray();
                Assert.NotEmpty(current);
                tab.IsVisible = false;
                Assert.All(current, r => Assert.False(HasWash(r)));
            }
            finally
            {
                window?.Close();
                (settings.MotionLevel, settings.PerformanceMode, settings.OfflineMode, LeaderboardTabView.NewClient, BeatLoop.Time) = old;
                if (saved == null) File.Delete(path); else File.WriteAllText(path, saved);
                LeaderboardRankSnapshotService.ResetForTests();
                CoreAccount.UnifiedUserId = oldId;
            }
        });

}
