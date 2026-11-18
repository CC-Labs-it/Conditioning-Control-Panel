using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Views.Controls;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Stills of the quest door hover (2026-11-18): a daily card at rest and hovered, and the weekly
/// card hovered, with the hover cues held at their END values (an offscreen render never runs the
/// clocks). Set <c>CCP_QUEST_HOVER_DUMP</c> to a folder to write the PNGs; without it the test only
/// proves the cards render.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class QuestHoverShotTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static BitmapSource Art()
    {
        var file = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "quests", "blink_drill_d.png");
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(file);
        img.EndInit();
        img.Freeze();
        return img;
    }

    private static void Hold(FrameworkElement hint)
    {
        hint.BeginAnimation(UIElement.OpacityProperty, null);
        hint.Opacity = 1;
        hint.Visibility = Visibility.Visible;
        hint.RenderTransform = Transform.Identity;
    }

    private static RenderTargetBitmap Shot(FrameworkElement e, int w, int h)
    {
        var host = new Grid { Width = w, Height = h, Background = (Brush)Application.Current.FindResource("DarkerBgBrush") };
        host.Children.Add(e);
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        return bmp;
    }

    private static void Save(BitmapSource bmp, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    private static DailyQuestCard Card()
    {
        var card = new DailyQuestCard { Margin = new Thickness(20) };
        card.Apply(new DailyQuestCardModel
        {
            Slot = 0, Icon = "", Name = "Blink drill", Description = "Finish a Blink Trainer round",
            Current = 1, Target = 3, XpText = "+40 XP", Art = Art(),
            CanReroll = true, RerollText = "Reroll", CanNavigate = true,
        });
        return card;
    }

    [Fact]
    public void The_hover_state_renders() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_QUEST_HOVER_DUMP");

        var rest = Shot(Card(), 300, 400);

        var hovered = Card();
        Hold(hovered.GoHint);
        var root = (Border)hovered.Content;
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        root.RenderTransform = new ScaleTransform(1.02, 1.02);
        root.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD7, 0x00), ShadowDepth = 0, BlurRadius = 18, Opacity = 0.5 };
        var hover = Shot(hovered, 300, 400);

        var tab = new QuestsTabView();
        tab.ImgWeeklyQuest.Source = Art();
        tab.SetWeeklyNavigable(true);
        Hold(tab.WeeklyGoHint);
        tab.WeeklyHoverRim.BeginAnimation(UIElement.OpacityProperty, null);
        tab.WeeklyHoverRim.Opacity = 1;
        var weekly = Shot(tab, 1400, 1000);

        Assert.True(rest.PixelWidth > 0 && hover.PixelWidth > 0 && weekly.PixelWidth > 0);
        if (string.IsNullOrEmpty(dir)) return;
        Save(rest, dir, "daily-rest.png");
        Save(hover, dir, "daily-hover.png");
        Save(weekly, dir, "weekly-hover-tab.png");
    });
}
