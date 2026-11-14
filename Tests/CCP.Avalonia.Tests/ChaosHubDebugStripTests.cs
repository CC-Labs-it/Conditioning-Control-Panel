using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHubDebugStripTests
{
    [Fact]
    public void Strip_is_flagged_and_actions_refresh_the_hub() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldFlag = ChaosHubWindow.DebugStripEnabled;
        var oldSlot = ConditioningControlPanel.CoreSettings.Current.ChaosActiveSlot;
        ChaosHubWindow? hub = null;
        try
        {
            ChaosMetaStore.Save(new ChaosMetaState { SeenIntroGuide = true, SeenDollhouse = true }, 1);
            ChaosMetaStore.SetActiveSlot(1);
            ChaosHubWindow.DebugStripEnabled = () => false;
            hub = new ChaosHubWindow(); hub.Show();
            Assert.Null(hub.DebugStrip);
            hub.Close();
            ChaosHubWindow.DebugStripEnabled = () => true;
            hub = new ChaosHubWindow(); hub.Show();
            var strip = Assert.IsType<WrapPanel>(hub.DebugStrip);
            var boxes = strip.Children.OfType<TextBox>().ToArray();
            void Click(string text) => strip.Children.OfType<Button>().Single(b => (string?)b.Content == text)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            boxes[0].Text = "-1"; Click("set runs"); Assert.Equal(0, ChaosMeta.State.RunsCompleted);
            boxes[0].Text = "10"; Click("set runs");
            Assert.Equal(10, ChaosMeta.State.RunsCompleted);
            Assert.Equal("Slipping", hub.FindControl<TextBlock>("MenuRank")!.Text);
            Click("+500 sparks"); Assert.Equal(500, ChaosMeta.State.Sparks);
            Assert.Equal("500", hub.FindControl<TextBlock>("MenuSparks")!.Text);
            Click("+500 gold"); Assert.Equal(500, ChaosMeta.State.Gold);
            Assert.Equal("500", hub.FindControl<TextBlock>("MenuGold")!.Text);
            Click("complete lesson"); Assert.True(ChaosLessons.IsComplete("slow_fuses"));
            boxes[1].Text = "vibe_popping"; Click("complete lesson"); Assert.True(ChaosLessons.IsComplete("vibe_popping"));
            boxes[2].Text = "debug_test_reveal"; Click("force reveal");
            Assert.True(ChaosMeta.State.SeenReveals.Contains("debug_test_reveal") || ChaosMeta.State.PendingReveals.Contains("debug_test_reveal"));
            Click("reset meta"); Assert.Equal(0, ChaosMeta.State.RunsCompleted); Assert.Equal(0, ChaosMeta.State.Gold);
            Assert.Equal("0", hub.FindControl<TextBlock>("MenuGold")!.Text);
            Assert.NotEqual("Slipping", hub.FindControl<TextBlock>("MenuRank")!.Text);
            Assert.False(ChaosLessons.IsComplete("slow_fuses"));
        }
        finally
        {
            hub?.Close(); ChaosHubWindow.DebugStripEnabled = oldFlag;
            ChaosMetaStore.Delete(1); ConditioningControlPanel.CoreSettings.Current.ChaosActiveSlot = oldSlot; ChaosMeta.Init();
        }
    });
}
