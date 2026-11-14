using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The sub cuts the leash any time: the user's own cut prompt (panic key held five seconds) closes an
/// open "Let go of X?" confirm and opens, never the other way round. Before, a Let go confirm left
/// open on the holder card was only brought forward, so it blocked the cut and its Yes released the
/// friend instead. Windows are never shown here (<see cref="LeashCutConfirmWindow.PresentForTests"/>).
/// Pairs with restack b226 (#5078) on the Avalonia head.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LeashCutPromptTests
{
    [Fact]
    public void Only_the_cut_prompt_replaces_an_open_prompt()
    {
        const string cut = LeashCutConfirmWindow.CutTag;
        Assert.True(LeashCutConfirmWindow.ReplacesOpen("leash-release-confirm", cut));
        Assert.False(LeashCutConfirmWindow.ReplacesOpen(cut, cut));
        Assert.False(LeashCutConfirmWindow.ReplacesOpen(cut, "leash-release-confirm"));
        Assert.False(LeashCutConfirmWindow.ReplacesOpen("leash-release-confirm", "leash-release-confirm"));
        Assert.False(LeashCutConfirmWindow.ReplacesOpen(null, cut));
    }

    [Fact]
    public void Cut_prompt_closes_an_open_let_go_confirm_and_opens()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            LeashFx.ForceStill = true;
            var presented = new List<(LeashCutConfirmWindow W, bool IsNew)>();
            LeashCutConfirmWindow.PresentForTests = (w, isNew) => presented.Add((w, isNew));
            try
            {
                var svc = FakeLeashService.Sample();
                var card = new LeashHolderCard(svc.Snapshot.Holding[0], () => svc);
                var menu = card.Menu();
                var letGo = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, "leash-menu-release"));

                letGo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var release = LeashCutConfirmWindow.Current!;
                Assert.Equal("leash-release-confirm", release.ConfirmTag);
                var releaseClosed = 0;
                release.Closed += (_, _) => releaseClosed++;

                var cut = 0;
                LeashCutConfirmWindow.Ask("Mika", () => cut++);
                var prompt = LeashCutConfirmWindow.Current!;
                Assert.NotSame(release, prompt);
                Assert.Equal(LeashCutConfirmWindow.CutTag, prompt.ConfirmTag);
                Assert.Equal(1, releaseClosed);
                Assert.Contains((prompt, true), presented);

                // A Let go request, or a second hold, while the cut prompt is up only brings it forward.
                letGo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Same(prompt, LeashCutConfirmWindow.Current);
                LeashCutConfirmWindow.Ask("Mika", () => cut += 100);
                Assert.Same(prompt, LeashCutConfirmWindow.Current);
                Assert.Equal((prompt, false), presented[^1]);

                prompt.YesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(1, cut);
                Assert.DoesNotContain(svc.Calls, c => c.StartsWith("release:", StringComparison.Ordinal));
                Assert.Null(LeashCutConfirmWindow.Current);
            }
            finally
            {
                LeashCutConfirmWindow.PresentForTests = null;
                LeashCutConfirmWindow.Current?.Close();
                LeashFx.ForceStill = false;
            }
        });
    }
}
