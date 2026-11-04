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

    [Theory]
    [InlineData("en", "just now", "20s ago", "2m ago", "3h ago", "1 fire today", "2 fires today")]
    [InlineData("de", "gerade eben", "vor 20 Sek.", "vor 2 Min.", "vor 3 Std.", "heute 1 Treffer", "heute 2 Treffer")]
    [InlineData("es", "ahora mismo", "hace 20 s", "hace 2 min", "hace 3 h", "1 activación hoy", "2 activaciones hoy")]
    [InlineData("fr", "à l’instant", "il y a 20 s", "il y a 2 min", "il y a 3 h", "1 déclenchement aujourd’hui", "2 déclenchements aujourd’hui")]
    [InlineData("it", "ora", "20 s fa", "2 min fa", "3 h fa", "1 attivazione oggi", "2 attivazioni oggi")]
    [InlineData("ja", "たった今", "20秒前", "2分前", "3時間前", "今日1回検出", "今日2回検出")]
    [InlineData("ko", "방금 전", "20초 전", "2분 전", "3시간 전", "오늘 1회 감지", "오늘 2회 감지")]
    [InlineData("pt-BR", "agora mesmo", "há 20 s", "há 2 min", "há 3 h", "1 ativação hoje", "2 ativações hoje")]
    [InlineData("ru", "только что", "20 с назад", "2 мин назад", "3 ч назад", "1 срабатывание сегодня", "Срабатываний сегодня: 2")]
    [InlineData("zh-CN", "刚刚", "20秒前", "2分钟前", "3小时前", "今天触发 1 次", "今天触发 2 次")]
    public void CountsAndTimesUseTheSelectedLanguage(string language, string nowText, string seconds, string minutes, string hours, string one, string many)
    {
        var loc = LocalizationManager.Instance;
        var old = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(language);
            var now = new DateTime(2026, 11, 4, 15, 45, 0);
            Assert.Equal(nowText, AwarenessTabView.FormatPulseTime(now.AddSeconds(-9), now));
            Assert.Equal(seconds, AwarenessTabView.FormatPulseTime(now.AddSeconds(-20), now));
            Assert.Equal(minutes, AwarenessTabView.FormatPulseTime(now.AddMinutes(-2), now));
            Assert.Equal(hours, AwarenessTabView.FormatPulseTime(now.AddHours(-3), now));
            Assert.Equal("15:45", AwarenessTabView.FormatPulseTime(now.AddDays(-1), now));
            Assert.Equal(one, AwarenessTabView.FormatPulseCount(1));
            Assert.Equal(many, AwarenessTabView.FormatPulseCount(2));
        }
        finally { loc.SetLanguage(old); }
    }

    [Fact]
    public void GermanCountsAndWpfTimeCutoffs()
    {
        var loc = LocalizationManager.Instance;
        var old = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("de");
            Assert.Equal("heute 1 Treffer", AwarenessTabView.FormatPulseCount(1));
            Assert.Equal("heute 2 Treffer", AwarenessTabView.FormatPulseCount(2));
            var now = new DateTime(2026, 11, 4, 15, 45, 0);
            Assert.Equal("gerade eben", AwarenessTabView.FormatPulseTime(now.AddSeconds(-9), now));
            Assert.Equal("vor 10 Sek.", AwarenessTabView.FormatPulseTime(now.AddSeconds(-10), now));
            Assert.Equal("vor 59 Sek.", AwarenessTabView.FormatPulseTime(now.AddSeconds(-59), now));
            Assert.Equal("vor 1 Min.", AwarenessTabView.FormatPulseTime(now.AddSeconds(-60), now));
            Assert.Equal("vor 59 Min.", AwarenessTabView.FormatPulseTime(now.AddMinutes(-59), now));
            Assert.Equal("vor 1 Std.", AwarenessTabView.FormatPulseTime(now.AddMinutes(-60), now));
            Assert.Equal("vor 23 Std.", AwarenessTabView.FormatPulseTime(now.AddHours(-23), now));
            Assert.Equal("15:45", AwarenessTabView.FormatPulseTime(now.AddHours(-24), now));
        }
        finally { loc.SetLanguage(old); }
    }

    [Fact]
    public void ChangingLanguageRefreshesVisibleFeed() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var loc = LocalizationManager.Instance;
        var old = loc.CurrentLanguage;
        var engine = new KeywordAudioRuntime();
        var view = new AwarenessTabView { PulseEngine = engine };
        var host = new Window { Content = view };
        try
        {
            loc.SetLanguage("en");
            engine.Fires.Record(new KeywordTrigger { Keyword = "test" }, "Text", DateTime.Now.AddSeconds(-20));
            host.Show();
            var count = view.FindControl<TextBlock>("TxtAwarenessFireCount")!;
            var feed = view.FindControl<StackPanel>("AwarenessPulseFeed")!;
            Assert.Equal("1 fire today", count.Text);
            loc.SetLanguage("de");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("heute 1 Treffer", count.Text);
            var grid = Assert.IsType<Grid>(Assert.IsType<Border>(feed.Children[0]).Child);
            Assert.StartsWith("vor ", Assert.IsType<TextBlock>(grid.Children[4]).Text);
        }
        finally { host.Close(); loc.SetLanguage(old); }
    });

}
