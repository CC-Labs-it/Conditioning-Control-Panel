using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using System.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;
public class LobbyDepthSeamTests
{
    /// <summary>A headless shell at Motion Off (moved here with the #3802 coin tests, now covered by NavRailCoinTests).</summary>
    internal static void WithShell(System.Action<global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow> body) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup(); global::ConditioningControlPanel.CoreSettings.Current.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Off;
        var w = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try { body(w); } finally { w.Close(); NavBadges.ResetForTests(); }
    });
    internal static void Setup()
    {
        if (Application.Current != null) return;
        var fonts = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
        var mappings = new System.Collections.Generic.Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
        mappings["Segoe UI"] = mappings["Segoe UI Black"] = new(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
        fonts.FontFamilyMappings = mappings;
        global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Fact]
    public void JoinRendersWpfTypographyHoverAndPressedFace() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var view = new AvailableSubjectsTabView();
        var button = new Button { Theme = (ControlTheme)view.Resources["LobbyJoin"]!, Content = new Border { Width = 20, Height = 10 } };
        button.ApplyTemplate(); button.Measure(new Size(200, 80)); button.Arrange(new Rect(0, 0, 200, 80));
        Assert.Equal(Color.Parse("#2A0618"), ((ISolidColorBrush)button.Foreground!).Color);
        Assert.Contains("Fredoka", button.FontFamily.Name);
        Assert.Equal(13.5, button.FontSize); Assert.Equal(FontWeight.Bold, button.FontWeight);
        Assert.Equal(new Thickness(15, 5, 15, 6), button.Padding);
        Assert.Equal("Hand", button.Cursor!.ToString());
        var face = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "face");
        var margin = face.Margin; var bounds = face.Bounds;
        ((IPseudoClasses)button.Classes).Set(":pointerover", true);
        Assert.Equal(0.92, face.Opacity);
        ((IPseudoClasses)button.Classes).Set(":pressed", true);
        Assert.Equal(3, Assert.IsType<TranslateTransform>(face.RenderTransform).Y);
        Assert.Equal(margin, face.Margin); Assert.Equal(bounds, face.Bounds);
        ((IPseudoClasses)button.Classes).Set(":pressed", false);
        ((IPseudoClasses)button.Classes).Set(":pointerover", false);
        Assert.Equal(1, face.Opacity); Assert.Null(face.RenderTransform);
    });

    [Fact]
    public void LobbyCardsAndJoinKeyUseTheDepthPaint() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var view = new AvailableSubjectsTabView();
        var row = new LobbyCardView(new LobbyRow { Key = "p_1", Game = LobbyGame.Chess }, default);
        var card = ((IDataTemplate)view.Resources["LobbyRowTemplate"]!).Build(row)!;
        card.DataContext = row;
        var host = new Window { Content = card };
        try
        {
            host.Show(); Dispatcher.UIThread.RunJobs();
            var band = card.GetVisualDescendants().OfType<Rectangle>().Single(r => (string?)r.Tag == "lobby-float");
            Assert.Same(row.CardShadow, band.Fill); Assert.Equal(row.CardShadowPx, band.Height);
            Assert.Same(row.CardShadow, new LobbyCardView(new LobbyRow(), default).CardShadow);
            var stops = Assert.IsAssignableFrom<ILinearGradientBrush>(band.Fill).GradientStops;
            var c = Color.FromUInt32(DepthLaw.ShadowColor(NavStripTable.AccentRgb(NavSections.Social) | 0xFF000000, DepthLaw.FloatAlpha));
            Assert.Equal(c, stops[0].Color); Assert.Equal(Color.FromArgb(0, c.R, c.G, c.B), stops[1].Color);
            Assert.Same(row.CardRim, ((Grid)card).Children.OfType<Border>().Single().BorderBrush);
            // WPF AvailableSubjectsTabView.xaml:22,143: the card glass runs #24FFFFFF at the top to #0CFFFFFF.
            var glass = Assert.IsAssignableFrom<ILinearGradientBrush>(((Grid)card).Children.OfType<Border>().Single().Background).GradientStops;
            Assert.Equal(Color.Parse("#24FFFFFF"), glass[0].Color); Assert.Equal(Color.Parse("#0CFFFFFF"), glass[1].Color);
            var face = card.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "face");
            Assert.Same(row.ButtonBevel, face.BorderBrush); Assert.Same(row.ButtonSheen, ((Border)face.Child!).Background);
        }
        finally { host.Close(); }
    });
    [Fact]
    public void WashFeedsTheExistingRailAndQuestsPainters() => WithShell(w =>
    {
        foreach (var tab in new[] { "play", "quests" })
        {
            w.ShowTab(tab); Dispatcher.UIThread.RunJobs();
            var hue = Color.FromUInt32(NavStripTable.AccentRgb(NavSections.SectionForTab(tab)!) | 0xFF000000);
            var shadow = w.FindControl<Rectangle>("NavRailShadow")!;
            Assert.Equal(6, shadow.Width); Assert.False(shadow.IsHitTestVisible);
            Assert.Equal(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), 0xBF / 255.0)),
                Assert.IsType<LinearGradientBrush>(shadow.Fill).GradientStops[0].Color);
            var band = w.Named<QuestsTabView>("QuestsTab")!.FindControl<Border>("WeeklyFloatBand")!;
            Assert.Equal(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.FloatAlpha)),
                Assert.IsType<LinearGradientBrush>(band.Background).GradientStops[0].Color);
        }
    });
}
