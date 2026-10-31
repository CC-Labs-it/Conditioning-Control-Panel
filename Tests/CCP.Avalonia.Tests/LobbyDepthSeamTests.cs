using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LobbyDepthSeamTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "CCP.Avalonia"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void JoinRendersWpfTypographyHoverAndPressedFace() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
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
    public void LobbyCardsAndJoinKeyUseTheDepthPaint()
    {
        var doc = XDocument.Load(Path.Combine(Root(), "CCP.Avalonia/Views/Tabs/AvailableSubjectsTabView.axaml"));
        var elements = doc.Descendants().ToArray();
        Assert.Contains(elements, e => (string?)e.Attribute("Tag") == "lobby-float"
            && (string?)e.Attribute("Fill") == "{Binding CardShadow}"
            && (string?)e.Attribute("Height") == "{Binding CardShadowPx}");
        Assert.Contains(elements, e => (string?)e.Attribute("BorderBrush") == "{Binding CardRim}");
        Assert.Contains(elements, e => (string?)e.Attribute("BorderBrush") == "{Binding ButtonBevel}");
        Assert.Contains(elements, e => (string?)e.Attribute("Background") == "{Binding ButtonSheen}");
        Assert.Contains(elements, e => (string?)e.Attribute("Theme") == "{StaticResource LobbyJoin}");
    }

    [Fact]
    public void WashFeedsTheExistingRailAndQuestsPainters()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia/Views/Windows/MainShellWindow.SectionChrome.cs"));
        Assert.Contains("PaintDepthRail(hue);", source);
        Assert.Contains("?.PaintDepthQuests(hue);", source);
    }
}
