using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Bark;
using Avalonia.Media.Imaging;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF ChaosHubWindow.Reveals.cs:145-153: the first hub open shows the intro once and marks
/// SeenIntroGuide; panic (PanicSurfaces "chaos-intro") closes it.</summary>
public sealed class ChaosIntroWindowTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void PumpUntil(Func<bool> done)
    {
        var sw = Stopwatch.StartNew();   // P73: bounded by time, not turns
        while (!done() && sw.Elapsed < TimeSpan.FromSeconds(10)) Dispatcher.UIThread.RunJobs();
        Assert.True(done(), "timed out");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reveals_stagger_pulse_then_persist_seen_and_consume_unmapped_ids(bool closeEarly)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var oldSlot = CoreSettings.Current.ChaosActiveSlot;
            var oldEngine = BarkHead.Engine;
            var oldSfx = ChaosHubWindow.PlayRevealSfx;
            var oldAvatar = CoreSettings.Current.AvatarEnabled;
            ChaosHubWindow? hub = null;
            try
            {
                ChaosMetaStore.Save(new ChaosMetaState { SeenIntroGuide = true, RunsCompleted = 1 }, 1);
                ChaosMetaStore.SetActiveSlot(1);
                hub = new ChaosHubWindow();
                RevealService.Sync("test");
                foreach (var id in RevealService.PendingIds()) RevealService.MarkSeen(id);
                ChaosMeta.State.PendingReveals.Clear();
                ChaosMeta.State.PendingReveals.Add(RevealIds.TabLookingGlass);
                ChaosMeta.State.PendingReveals.Add(RevealIds.SectionToys);
                ChaosMeta.State.PendingReveals.Add("no_hub_surface");
                int chimes = 0;
                ChaosHubWindow.PlayRevealSfx = (key, volume) =>
                {
                    Assert.Equal("reveal_chime", key);
                    Assert.Equal(0.5f, volume);
                    chimes++;
                };
                int barks = 0;
                var engine = new BarkEngine(() => new BarkRuleSet(new[]
                {
                    new BarkRule { Id = "batch", Trigger = "ChaosRevealFlash",
                        Conditions = new() { ["element"] = RevealIds.TabLookingGlass },
                        VariantPool = new() { new BarkVariant("new room") } },
                }));
                engine.Speak = _ => barks++;
                engine.Start();
                CoreSettings.Current.AvatarEnabled = true;
                BarkHead.Engine = engine;
                hub.RunRevealFlashes();
                Assert.Equal(1, barks);
                Assert.Equal(1, chimes);
                var first = hub.FindControl<Control>("TabImprove")!;
                var second = hub.FindControl<Control>("HdrToys")!;
                Assert.True(RevealService.IsSeen("no_hub_surface"));
                hub.AdvanceRevealFlashes(0.5);
                Assert.Equal(0.25, first.Opacity, 6);
                Assert.Equal(1, second.Opacity);
                Assert.Equal(1, chimes);
                Assert.True(RevealService.IsPending(RevealIds.TabLookingGlass));
                if (closeEarly)
                {
                    hub.Close();
                    Assert.Equal(1, first.Opacity);
                    Assert.Equal(1, second.Opacity);
                    Assert.True(RevealService.IsSeen(RevealIds.SectionToys));
                    hub.AdvanceRevealFlashes(1.1);
                    Assert.Equal(1, second.Opacity);
                    Assert.Equal(1, barks);
                    Assert.Equal(1, chimes);
                    return Task.CompletedTask;
                }
                hub.AdvanceRevealFlashes(1.1);
                Assert.Equal(0.25, second.Opacity, 6);
                Assert.Equal(2, chimes);
                hub.AdvanceRevealFlashes(3);
                Assert.Equal(1, first.Opacity);
                Assert.True(RevealService.IsSeen(RevealIds.TabLookingGlass));
                Assert.True(RevealService.IsPending(RevealIds.SectionToys));
                hub.AdvanceRevealFlashes(3.6);
                Assert.Equal(1, second.Opacity);
                Assert.True(ChaosMetaStore.Load(1).SeenReveals.Contains(RevealIds.SectionToys));
            }
            finally
            {
                hub?.Close();
                ChaosMetaStore.Delete(1);
                CoreSettings.Current.ChaosActiveSlot = oldSlot;
                BarkHead.Engine = oldEngine;
                ChaosHubWindow.PlayRevealSfx = oldSfx;
                CoreSettings.Current.AvatarEnabled = oldAvatar;
                ChaosMeta.Init();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task First_open_bark_is_saved_and_raised_only_once()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var old = (CoreSettings.Current.ChaosActiveSlot, BarkHead.Engine, CoreSettings.Current.AvatarEnabled);
            ChaosHubWindow? hub = null;
            try
            {
                CoreSettings.Current.AvatarEnabled = true;
                var engine = new BarkEngine(() => new BarkRuleSet(new[]
                {
                    new BarkRule { Id = "first_open", Trigger = "ChaosDollhouseFirstOpen",
                        VariantPool = new() { new BarkVariant("welcome") } },
                }));
                int spoken = 0;
                engine.BarkSpoken += (_, _) => { Assert.True(ChaosMetaStore.Load(1).SeenDollhouse); spoken++; };
                engine.Speak = _ => { };
                engine.Start();
                BarkHead.Engine = engine;
                ChaosMetaStore.Save(new ChaosMetaState { SeenIntroGuide = true }, 1);
                ChaosMetaStore.SetActiveSlot(1);
                hub = new ChaosHubWindow();
                await hub.OnHubOpenedReveals();
                Assert.Equal(1, spoken);
                await hub.OnHubOpenedReveals();
                Assert.Equal(1, spoken);
                Assert.True(ChaosMetaStore.Load(1).SeenDollhouse);
            }
            finally
            {
                hub?.Close();
                ChaosMetaStore.Delete(1);
                (CoreSettings.Current.ChaosActiveSlot, BarkHead.Engine, CoreSettings.Current.AvatarEnabled) = old;
                ChaosMeta.Init();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hero_prefers_user_art_and_falls_back_when_it_is_unreadable(bool corrupt)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var path = Path.Combine(CorePaths.UserData, "assets", "assets", "Chaos", "guide", "intro.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                if (corrupt) File.WriteAllText(path, "not an image");
                else
                {
                    using var bitmap = new WriteableBitmap(new PixelSize(23, 17), new Vector(96, 96));
                    bitmap.Save(path);
                }
                var w = new ChaosIntroWindow();
                try
                {
                    var hero = (Bitmap)w.GetLogicalDescendants().OfType<Image>().Single().Source!;
                    // The native dimensions prove which art was decoded, independently of layout.
                    Assert.Equal(corrupt ? 560 : 23, hero.PixelSize.Width);
                }
                finally { w.Close(); }
            }
            finally { File.Delete(path); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task First_hub_open_shows_the_intro_once_and_panic_closes_it_and_marks_it_seen()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var oldSlot = CoreSettings.Current.ChaosActiveSlot;
            ChaosHubWindow? hub = null, hub2 = null;
            try
            {
                ChaosMetaStore.Save(new ChaosMetaState { SeenIntroGuide = false }, 1);
                ChaosMetaStore.SetActiveSlot(1);

                hub = new ChaosHubWindow();
                hub.Show();
                PumpUntil(() => ChaosIntroWindow.Current != null);
                var intro = ChaosIntroWindow.Current!;
                Assert.False(ChaosMeta.State.SeenDollhouse);
                Assert.Empty(ChaosMeta.State.PendingReveals);
                Assert.False(ChaosMeta.State.SeenIntroGuide);   // marked only after it closes, as WPF ShowDialog
                var text = string.Join("|", intro.GetLogicalDescendants().OfType<TextBlock>()
                    .Select(t => t.Text ?? string.Concat(t.Inlines!.OfType<Run>().Select(r => r.Text))));
                Assert.Contains("DOWN THE RABBIT HOLE", text);
                Assert.Contains("follow the white rabbit. everything else down there is yours to find out.", text);

                PanicSurfaces.All.Single(s => s.Id == "chaos-intro").Stop(null);
                await hub.IntroShown!;
                Assert.Null(ChaosIntroWindow.Current);
                Assert.True(ChaosMeta.State.SeenIntroGuide);
                Assert.True(ChaosMeta.State.SeenDollhouse);
                Assert.True(ChaosMetaStore.Load(1).SeenDollhouse);
                Assert.True(ChaosMetaStore.Load(1).SeenIntroGuide);   // saved, not just in memory
                hub.Close();

                hub2 = new ChaosHubWindow();   // Init reloads the save: seen, so no card
                hub2.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(ChaosIntroWindow.Current);   // before awaiting: a shown card would never finish
                await hub2.IntroShown!;
            }
            finally
            {
                ChaosIntroWindow.CloseForPanic();
                hub?.Close();
                hub2?.Close();
                ChaosMetaStore.Delete(1);
                ChaosMeta.Init();
                CoreSettings.Current.ChaosActiveSlot = oldSlot;
            }
        });
    }

    [Fact]
    public async Task The_button_closes_the_card()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var w = new ChaosIntroWindow();
            try
            {
                w.Show();
                Assert.Same(w, ChaosIntroWindow.Current);
                w.GetLogicalDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(ChaosIntroWindow.Current);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>P51: the panic key wins over a focused button. Bound to Enter, it closes the card
    /// through the tunnel handler and the button never sees a click.</summary>
    [Fact]
    public async Task Panic_key_bound_to_Enter_closes_the_card_without_clicking_the_focused_button()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var s = CoreSettings.Current;
            var (oldKey, oldOn) = (s.PanicKey, s.PanicKeyEnabled);
            var w = new ChaosIntroWindow();
            try
            {
                s.PanicKey = "Enter";
                s.PanicKeyEnabled = true;
                w.Show();
                var btn = w.GetLogicalDescendants().OfType<Button>().Single();
                int clicks = 0;
                btn.Click += (_, _) => clicks++;
                Assert.True(btn.Focus());
                w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, clicks);
                Assert.Null(ChaosIntroWindow.Current);
            }
            finally
            {
                w.Close();
                (s.PanicKey, s.PanicKeyEnabled) = (oldKey, oldOn);
            }
            return Task.CompletedTask;
        });
    }
}
