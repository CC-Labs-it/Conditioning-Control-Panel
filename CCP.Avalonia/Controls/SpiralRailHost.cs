using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>Native rail medallion; never hosts a browser or fetches a block itself.</summary>
public sealed class SpiralRailHost : Grid
{
    private readonly SpiralGlyph _glyph;
    private DescentBlock? _block;
    private bool _withheld;

    public SpiralRailHost()
    {
        Height = 56;
        Margin = new Thickness(0, 6, 0, 2);
        Cursor = new Cursor(StandardCursorType.Hand);
        IsVisible = false;
        _glyph = new SpiralGlyph
        {
            Width = 32, Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x69, 0xB4)),
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _glyph,
        });
        PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && CoreSettings.Current.DescentSpiralRailEnabled)
                this.FindAncestorOfType<MainShellWindow>()?.ShowTab(SpiralRoom.TabKey);
        };
        Loaded += (_, _) => Arm();
    }

    /// <summary>The shared descent reader will feed accepted blocks here, including withdrawals.</summary>
    public void Apply(DescentBlock? block, bool spiralWithheld)
    {
        _block = block;
        _withheld = spiralWithheld;
        Arm();
    }

    public void Arm()
    {
        IsVisible = SpiralRailState.Visible(CoreSettings.Current.DescentSpiralRailEnabled, _block is not null, _withheld);
        _glyph.RefreshMotion();
        if (!IsVisible) return;
        _glyph.Apply(_block);
        int stage = _block?.Stage?.N ?? 0;
        ToolTip.SetTip(this, DescentStageCopy.Name(stage) + "\n" + DescentStageCopy.Flavor(stage));
    }

    public void RefreshGlyphMotion() => _glyph.RefreshMotion();
    // ponytail: this head has no DescentService or migration withhold publisher yet. Stay
    // collapsed until its shared reader calls Apply; never fabricate a block or start a poll.
}
