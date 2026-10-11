using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF caps text at N lines with a MaxHeight sized to its own (shorter) line box. Avalonia's line
/// box is taller, so the same MaxHeight either drops a line or cuts the last one in half. The ported caps are
/// MaxLines; this proves a long localized string shows exactly the WPF line count, every line whole.</summary>
public sealed class TextLineCapTests
{
    private static string Long => string.Join(" ", Enumerable.Repeat(Loc.Get("label_are_you_a_content_creator_join_our_discord_to"), 3));

    private static void AssertWholeLines(TextBlock tb, int lines, string what)
    {
        tb.Text = Long;
        Dispatcher.UIThread.RunJobs();
        TopLevel.GetTopLevel(tb)!.UpdateLayout();
        var layout = tb.TextLayout;
        Assert.True(layout.TextLines.Count == lines, $"{what}: {layout.TextLines.Count} lines shown, WPF shows {lines}");
        // The whole last line fits inside the box: no half line clipped off the bottom.
        Assert.True(layout.Height <= tb.Bounds.Height - tb.Padding.Top - tb.Padding.Bottom + 0.5,
            $"{what}: text {layout.Height:0.#} px tall in a {tb.Bounds.Height:0.#} px box");
    }

    [Fact]
    public void LongLocalizedCopy_ShowsTheWpfLineCount_WithAWholeLastLine()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory("ccp-text-cap-").FullName;
            Window? quest = null, ach = null, assets = null;
            try
            {
                // WPF DailyQuestCard.xaml:135 (11 pt, MaxHeight 31 = two lines).
                var card = new DailyQuestCard();
                quest = new Window { Width = 400, Height = 500, Content = card };
                quest.Show();
                Dispatcher.UIThread.RunJobs();
                AssertWholeLines(card.FindControl<TextBlock>("TxtDesc")!, 2, "daily quest description");

                // WPF AssetsTabView.xaml:578 (10 pt, MaxHeight 52 = four WPF lines; Avalonia's 14 px box fits three).
                var assetsView = new AssetsTabView();
                assets = new Window { Width = 1400, Height = 1000, Content = assetsView };
                assets.Show();
                Dispatcher.UIThread.RunJobs();
                assetsView.FindControl<Border>("PacksSection")!.IsVisible = true;   // shown once packs load
                Dispatcher.UIThread.RunJobs();
                var creator = Loc.Get("label_are_you_a_content_creator_join_our_discord_to");
                AssertWholeLines(assetsView.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == creator), 4, "creator card");

                // WPF MainWindow.AchievementsTab.cs:315,332 (name two lines, info three).
                var view = new AchievementsTabView(new AchievementEngine(new AchievementStore(Path.Combine(dir, "a.json"))));
                ach = new Window { Width = 1200, Height = 900, Content = view };
                ach.Show();
                Dispatcher.UIThread.RunJobs();
                var texts = view.Cards.SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>()).ToArray();
                AssertWholeLines(texts.First(t => t.FontSize == 13 && t.FontWeight == FontWeight.SemiBold), 2, "achievement name");
                // Info: three lines bare, two when the progress meter sits under it (WPF ApplyAchievementMeter).
                var infos = texts.Where(t => t.LineHeight == 14 && t.Parent is StackPanel).ToArray();
                AssertWholeLines(infos.First(t => ((StackPanel)t.Parent!).Children.Count == 1), 3, "achievement info");
                AssertWholeLines(infos.First(t => ((StackPanel)t.Parent!).Children.Count == 2), 2, "achievement info + meter");
            }
            finally
            {
                quest?.Close();
                ach?.Close();
                assets?.Close();
                try { Directory.Delete(dir, true); } catch { }
            }
        });
    }

    /// <summary>Guard: a TextBlock line cap is MaxLines, never a pixel MaxHeight (WPF line metrics do not port).</summary>
    [Fact]
    public void NoTextBlockInTheAvaloniaHeadCapsItsHeightInPixels()
    {
        var head = Path.GetFullPath(Path.Combine(Here(), "..", "..", "CCP.Avalonia"));
        var offenders = Directory.EnumerateFiles(head, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(Regex.Replace(File.ReadAllText(f), "<!--.*?-->", "", RegexOptions.Singleline),
                    @"<TextBlock\b[^>]*\bMaxHeight=")
                .Select(m => Path.GetRelativePath(head, f)))
            .ToArray();
        Assert.True(offenders.Length == 0, "TextBlock with MaxHeight (use MaxLines): " + string.Join(", ", offenders));
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string p = "") => Path.GetDirectoryName(p)!;
}
