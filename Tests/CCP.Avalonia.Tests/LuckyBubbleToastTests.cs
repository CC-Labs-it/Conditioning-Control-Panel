using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class LuckyBubbleToastTests
{
    private sealed class FixedRoll(double roll) : Random
    {
        public override double NextDouble() => roll;
    }

    // WPF BubbleService.cs:1082-1108 and SkillTreeService.cs:216-223: notify on the roll,
    // including a dry XP bucket; the gold burst and chime still require a payout.
    [Fact]
    public Task BubblePopNotifiesOnLuckyRollEvenWhenTheBucketIsEmpty() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var skills = s.UnlockedSkills.ToList();
        var (rng, probe, quiet, day, budget, xp, audio) = (BubbleOverlay.Field.Random, LuckyToast.GameOwnsScreen,
            s.SuppressPerkNotifications, s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday,
            CoreProgression.AddXPProvider, CoreAudio.PlayOneShotProvider);
        double paid = 0;
        try
        {
            s.UnlockedSkills.Clear();
            s.SuppressPerkNotifications = false;
            LuckyToast.GameOwnsScreen = () => false;
            CoreProgression.AddXPProvider = (amount, _) => paid += amount;
            CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => { };
            LuckyToast.Close();
            BubbleOverlay.Field.Random = new FixedRoll(0);
            BubbleOverlay.Pop(new AmbientBubble()); // no skill
            Dispatcher.UIThread.RunJobs();
            Assert.Null(LuckyToast.Popup);

            s.UnlockedSkills.Add("lucky_bubbles");
            BubbleOverlay.Field.Random = new FixedRoll(0.99);
            BubbleOverlay.Pop(new AmbientBubble()); // miss
            Dispatcher.UIThread.RunJobs();
            Assert.Null(LuckyToast.Popup);

            BubbleOverlay.Field.Random = new FixedRoll(0);
            s.AmbientBubbleXpDayKey = DateTime.Now.ToString("yyyy-MM-dd");
            s.AmbientBubbleXpPaidToday = AmbientBubbleXp.DailyXpCap;
            var dry = new AmbientBubble();
            var before = paid;
            BubbleOverlay.Pop(dry);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, paid);
            Assert.False(dry.Lucky);
            Assert.NotNull(LuckyToast.Popup);
            Assert.True(LuckyToast.Popup!.IsVisible);
            Assert.Contains(Loc.GetF("lucky_toast_xp", 20), LuckyToast.Popup.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

            LuckyToast.HoldElapsed();
            LuckyToast.FadeOutDone();
            BubbleOverlay.Pop(dry); // a second press must not roll or refresh
            Dispatcher.UIThread.RunJobs();
            Assert.False(LuckyToast.Popup.IsVisible);

            s.AmbientBubbleXpPaidToday = 0;
            var hit = new AmbientBubble();
            BubbleOverlay.Pop(hit);
            Dispatcher.UIThread.RunJobs();
            Assert.True(hit.Lucky);
            Assert.Equal(before + 100, paid);
            Assert.True(LuckyToast.Popup.IsVisible);

            LuckyToast.HoldElapsed();
            LuckyToast.FadeOutDone();
            s.SuppressPerkNotifications = true;
            BubbleOverlay.Pop(new AmbientBubble());
            Dispatcher.UIThread.RunJobs();
            Assert.False(LuckyToast.Popup.IsVisible);
            Assert.Equal(before + 200, paid);
        }
        finally
        {
            LuckyToast.Close();
            BubbleOverlay.Field.Random = rng;
            LuckyToast.GameOwnsScreen = probe;
            s.SuppressPerkNotifications = quiet;
            s.AmbientBubbleXpDayKey = day;
            s.AmbientBubbleXpPaidToday = budget;
            s.UnlockedSkills.Clear();
            s.UnlockedSkills.AddRange(skills);
            CoreProgression.AddXPProvider = xp;
            CoreAudio.PlayOneShotProvider = audio;
        }
        return Task.CompletedTask;
    });
}
