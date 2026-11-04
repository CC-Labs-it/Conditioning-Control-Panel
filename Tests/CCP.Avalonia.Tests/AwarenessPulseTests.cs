using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Localization;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class AwarenessPulseTests
{
    [Fact]
    public void FeedShowsSnapshotsAndRefreshesAfterWorkerFire() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var engine = new KeywordAudioRuntime();
        var view = new AwarenessTabView { PulseEngine = engine };
        var host = new Window { Content = view };
        var premium = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            host.Show();
            var feed = view.FindControl<StackPanel>("AwarenessPulseFeed")!;
            var count = view.FindControl<TextBlock>("TxtAwarenessFireCount")!;
            Assert.Single(feed.Children);
            Assert.Equal("", count.Text);
            for (var i = 0; i < 6; i++)
                engine.Fires.Record(new KeywordTrigger { Keyword = $"word{i}", Actions = new() { new PlayAudioAction { Enabled = true } } }, "Keyboard", DateTime.Now);
            view.RefreshPulse();
            Assert.Equal(5, feed.Children.Count);
            Assert.Equal("6 fires today", count.Text);
            var card = Assert.IsType<Border>(feed.Children[0]);
            Assert.Equal(new Thickness(12, 8), card.Padding);
            Assert.Equal(new Thickness(1), card.BorderThickness);
            Assert.Equal(new CornerRadius(6), card.CornerRadius);
            Assert.Equal(Color.Parse("#2A2A40"), Assert.IsAssignableFrom<ISolidColorBrush>(card.BorderBrush).Color);
            Assert.NotNull(card.Background);
            var first = Assert.IsType<Grid>(card.Child);
            Assert.Equal(GridUnitType.Star, first.ColumnDefinitions[3].Width.GridUnitType);
            var accent = Assert.IsType<Rectangle>(first.Children[0]);
            Assert.Equal(3, accent.Width);
            Assert.NotNull(accent.Fill);
            Assert.Equal("\"word5\"", Assert.IsType<TextBlock>(first.Children[1]).Text);
            var chip = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(first.Children[2]).Children[0]);
            Assert.Equal("🔊", chip.Text);
            Assert.Equal(Loc.Get("pulse_tip_audio"), ToolTip.GetTip(chip));
            Assert.Equal(200, ToolTip.GetShowDelay(chip));
            Assert.Equal(0, ToolTip.GetBetweenShowDelay(chip));
            Assert.Equal(4, Grid.GetColumn(first.Children[3]));
            Assert.Equal(5, Grid.GetColumn(first.Children[4]));
            Assert.Equal("KEYBOARD", Assert.IsType<TextBlock>(Assert.IsType<Border>(first.Children[3]).Child).Text);
            Assert.Equal("\"word1\"", Assert.IsType<TextBlock>(Assert.IsType<Grid>(Assert.IsType<Border>(feed.Children[4]).Child).Children[1]).Text);
            Task.Run(() => engine.FireDemo("live", "Text")).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("7 fires today", count.Text);
            Assert.Equal("\"live\"", Assert.IsType<TextBlock>(Assert.IsType<Grid>(Assert.IsType<Border>(feed.Children[0]).Child).Children[1]).Text);
        }
        finally { host.Close(); CoreEntitlement.HasPremiumProvider = premium; }
    });

    [Theory]
    [InlineData("PlayAudio", "🔊", "Plays an audio clip when the word is detected")]
    [InlineData("Highlight", "👁", "Draws a glowing box around the matched word on screen (OCR matches only)")]
    [InlineData("Haptic", "💥", "Fires a haptic vibration pattern on connected devices")]
    [InlineData("AvatarComment", "💬", "Makes the avatar comment on the matched word (AI + canned fallback)")]
    [InlineData("ExtendSession", "⏱", "Extends the current session")]
    [InlineData("ExtendSession:12", "⏱", "Extends the current session by 12 minutes")]
    [InlineData("ChasterAddTime", "🔒", "Adds time to the Chaster lock")]
    [InlineData("ChasterAddTime:3", "🔒", "Adds 3 minutes to the Chaster lock")]
    [InlineData("VisualEffect:SubliminalFlash", "✨", "Flashes a random word from your subliminal pool")]
    [InlineData("VisualEffect:ExactSubliminal", "🔤", "Flashes the matched keyword itself as subliminal text")]
    [InlineData("VisualEffect:ImageFlash", "⚡", "Fires a flash burst image when the word is detected")]
    [InlineData("VisualEffect:OverlayPulse", "🌫", "Briefly intensifies the screen overlay")]
    [InlineData("VisualEffect:MindWipe", "🧠", "Triggers the MindWipe effect")]
    [InlineData("VisualEffect:Bubbles", "🫧", "Spawns bubbles on screen")]
    [InlineData("VisualEffect:Other", "✨", "Fires visual effect: Other")]
    [InlineData("AddXp:5", "", "")]
    public void ActionDescriptionsMatchWpf(string key, string icon, string tip)
    {
        var loc = LocalizationManager.Instance;
        var old = loc.CurrentLanguage;
        try { loc.SetLanguage("en"); Assert.Equal((icon, tip), AwarenessTabView.PulseChip(key)); }
        finally { loc.SetLanguage(old); }
    }

}
