using System;
using System.Diagnostics;
using System.Linq;
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
                Assert.False(ChaosMeta.State.SeenIntroGuide);   // marked only after it closes, as WPF ShowDialog
                var text = string.Join("|", intro.GetLogicalDescendants().OfType<TextBlock>()
                    .Select(t => t.Text ?? string.Concat(t.Inlines!.OfType<Run>().Select(r => r.Text))));
                Assert.Contains("DOWN THE RABBIT HOLE", text);
                Assert.Contains("follow the white rabbit. everything else down there is yours to find out.", text);

                PanicSurfaces.All.Single(s => s.Id == "chaos-intro").Stop(null);
                await hub.IntroShown!;
                Assert.Null(ChaosIntroWindow.Current);
                Assert.True(ChaosMeta.State.SeenIntroGuide);
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
