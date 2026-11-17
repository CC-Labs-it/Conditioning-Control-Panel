using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The gold SHOP tag on every door into the shop (owner, 2026-11-17: "write SHOP in bold golden on the launcher, and
/// whenever we can open the shop"). Rules of its face, where it rides, and the chip drawn offscreen at rest, hover,
/// pressed in and with a long balance. Set <c>CCP_SHOP_TAG_DUMP</c> to a folder to also write the PNGs.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ShopTagTests
{
    [Fact]
    public void Pressed_or_held_in_wins_over_hover()
    {
        Assert.Equal(ShopTag.Look.Rest, ShopTag.LookOf(false, false, false));
        Assert.Equal(ShopTag.Look.Hot, ShopTag.LookOf(true, false, false));
        Assert.Equal(ShopTag.Look.Pressed, ShopTag.LookOf(true, true, false));
        Assert.Equal(ShopTag.Look.Pressed, ShopTag.LookOf(false, false, true));
        Assert.Equal(ShopTag.Look.Pressed, ShopTag.LookOf(true, false, true));
    }

    [Fact]
    public void The_word_lifts_on_hover_sinks_in_the_well_and_holds_still_with_Gentle()
    {
        Assert.Equal(0, ShopTag.FaceDip(ShopTag.Look.Rest, false));
        Assert.True(ShopTag.FaceDip(ShopTag.Look.Hot, false) < 0);
        Assert.True(ShopTag.FaceDip(ShopTag.Look.Pressed, false) > 0);
        foreach (var look in Enum.GetValues<ShopTag.Look>()) Assert.Equal(0, ShopTag.FaceDip(look, true));
    }

    [Fact]
    public void Every_door_into_the_shop_wears_the_tag()
    {
        var root = RepoRoot();
        string Read(string rel) => File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", rel));
        var launcher = Read("Windows/Launcher/LauncherWindow.xaml");
        int chip = launcher.IndexOf("x:Name=\"WalletChip\"", StringComparison.Ordinal);
        Assert.True(chip > 0);
        Assert.InRange(launcher.IndexOf("<fx:ShopTag", chip, StringComparison.Ordinal), chip, launcher.IndexOf("</Button>", chip, StringComparison.Ordinal));
        Assert.Contains("{loc:Str shop_door_tip}", launcher.Substring(chip, 400));
        Assert.Contains("<local:ShopTag", Read("Controls/SparkleWallet.xaml"));
        Assert.Contains("new ShopTag()", Read("Controls/LookRow.cs"));
        Assert.Contains("ShopTag.Beside(", Read("Windows/EmiDesk/EmiOptionsWindow.xaml.cs"));
        Assert.Contains("ShopTag.In(chip)", Read("Controls/CounterSheetView.cs"));
        // the old DEBUG "Preview" mark beside the chip's number is gone: the chip shows SHOP
        Assert.DoesNotContain("MarkPreview(WalletChip", Read("Windows/Launcher/LauncherWindow.Choreo.cs"));
    }

    [Fact]
    public void The_launcher_chip_reads_SHOP_at_rest_hover_and_pressed_in_and_keeps_its_width()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var dump = Environment.GetEnvironmentVariable("CCP_SHOP_TAG_DUMP");
            var shots = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x16, 0x13, 0x22)) };
            double shortW = 0, longW = 0;
            foreach (var (name, balance, look) in new (string, string, ShopTag.Look)[]
                     {
                         ("rest", "1,850", ShopTag.Look.Rest), ("hover", "1,850", ShopTag.Look.Hot), ("open", "1,850", ShopTag.Look.Pressed),
                         ("long", "1,234,567", ShopTag.Look.Rest),
                     })
            {
                var (chip, tag) = LauncherChip(balance);
                if (look == ShopTag.Look.Pressed) CounterSheetView.PressChip(chip, true);
                else tag.Paint(look, animate: false);
                var cell = new Border { Padding = new Thickness(12, 8, 12, 8), Child = chip, HorizontalAlignment = HorizontalAlignment.Left };
                shots.Children.Add(cell);
                Layout(shots);
                Assert.True(tag.ActualWidth > 20 && tag.ActualHeight >= 14, $"{name}: tag {tag.ActualWidth:0}x{tag.ActualHeight:0}");
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(tag)));
                Assert.NotNull(tag.ToolTip);
                if (look == ShopTag.Look.Pressed) Assert.True(tag.PressedIn);
                if (name == "rest") shortW = chip.ActualWidth;
                if (name == "long") longW = chip.ActualWidth;
            }
            // the tag adds a fixed width: a long balance grows the chip by its digits only, and the chip stays a chip
            Assert.InRange(longW - shortW, 10, 45);
            Assert.True(longW < 190, $"long chip {longW:0} px");

            // the panel wallet's label line: the compact tag
            var compact = new ShopTag { Compact = true };
            var label = new Grid { Width = 80, Height = 12, Background = new SolidColorBrush(Color.FromRgb(0x27, 0x1A, 0x32)) };
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            label.ColumnDefinitions.Add(new ColumnDefinition());
            label.Children.Add(compact);
            var words = new TextBlock { Text = "Sparkles", FontSize = 8, Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xBC, 0xD9)), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(words, 1);
            label.Children.Add(words);
            shots.Children.Add(new Border { Padding = new Thickness(12, 8, 12, 8), Child = label, HorizontalAlignment = HorizontalAlignment.Left });
            Layout(shots);
            Assert.True(compact.ActualHeight <= 13, $"compact tag {compact.ActualHeight:0} px high");

            if (dump is { Length: > 0 }) Save(shots, Path.Combine(dump, "door-chip-launcher.png"), 3);
        });
    }

    [Fact]
    public void The_panel_wallet_and_the_desk_button_carry_the_tag()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var shots = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x16, 0x13, 0x22)) };
            foreach (var balance in new[] { "1,850", "1,234,567" })
            {
                var wallet = new SparkleWallet();
                wallet.BalanceText.Text = balance;
                wallet.WalletLabel.Text = "Sparkles";
                shots.Children.Add(new Border { Padding = new Thickness(12, 8, 12, 8), Child = wallet, HorizontalAlignment = HorizontalAlignment.Left });
                Layout(shots);
                var tag = Assert.Single(ShopTag.In(wallet));
                Assert.True(tag.Compact);
                Assert.True(tag.ActualWidth > 12, $"wallet tag {tag.ActualWidth:0} px");
                // the tag stays inside the pill's text column, clear of the "?" at the right end
                var at = tag.TranslatePoint(new Point(tag.ActualWidth, 0), wallet);
                Assert.True(at.X <= wallet.ActualWidth - 23, $"tag ends at {at.X:0} of {wallet.ActualWidth:0}");
            }
            var pressed = new SparkleWallet();
            pressed.BalanceText.Text = "1,850"; pressed.WalletLabel.Text = "Sparkles";
            shots.Children.Add(new Border { Padding = new Thickness(12, 8, 12, 8), Child = pressed, HorizontalAlignment = HorizontalAlignment.Left });
            Layout(shots);
            CounterSheetView.PressChip(pressed, true);
            Assert.True(ShopTag.In(pressed).Single().PressedIn);

            var desk = new Button { Content = ShopTag.Beside("Prize Counter"), Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF2, 0xFA)), Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)) };
            shots.Children.Add(new Border { Padding = new Thickness(12, 8, 12, 8), Child = desk, HorizontalAlignment = HorizontalAlignment.Left });
            Layout(shots);
            Assert.Single(ShopTag.In(desk));
            if (Environment.GetEnvironmentVariable("CCP_SHOP_TAG_DUMP") is { Length: > 0 } dir) Save(shots, Path.Combine(dir, "door-chip-panel-and-desk.png"), 3);
        });
    }

    /// <summary>The launcher's wallet chip as LauncherWindow.xaml draws it (LauncherBarPill: a 34 px pill on the surface
    /// brush), with the star, the balance and the tag.</summary>
    private static (Button chip, ShopTag tag) LauncherChip(string balance)
    {
        var bg = new FrameworkElementFactory(typeof(Border), "Bg");
        bg.SetValue(Border.CornerRadiusProperty, new CornerRadius(17));
        bg.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        bg.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        bg.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        bg.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bg.AppendChild(cp);
        var tag = new ShopTag();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new SparkleStar { Height = 12, VerticalAlignment = VerticalAlignment.Center });
        var n = new TextBlock { Text = balance, Margin = new Thickness(7, 0, 0, 0), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Fredoka, Segoe UI") };
        n.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
        row.Children.Add(n);
        row.Children.Add(tag);
        var chip = new Button { Template = new ControlTemplate(typeof(Button)) { VisualTree = bg }, Height = 34, Padding = new Thickness(11, 0, 6, 0), BorderThickness = new Thickness(1), Content = row, HorizontalAlignment = HorizontalAlignment.Left };
        chip.SetResourceReference(Control.BackgroundProperty, "SurfaceBgBrush");
        chip.SetResourceReference(Control.BorderBrushProperty, "GlassBorderBrush");
        return (chip, tag);
    }

    private static void Layout(FrameworkElement host)
    {
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
    }

    private static void Save(FrameworkElement host, string file, double scale)
    {
        host.LayoutTransform = new ScaleTransform(scale, scale);
        Layout(host);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.DesiredSize.Width), (int)Math.Ceiling(host.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ConditioningControlPanel.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
