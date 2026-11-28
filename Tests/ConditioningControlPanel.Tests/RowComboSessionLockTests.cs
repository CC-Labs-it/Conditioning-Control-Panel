using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Localization;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 review (wave 8): RowComboFit (full value in the tip when the value is trimmed) and SessionLock
/// (lock reason in the tip while a session owns the box) both save and restore the same ToolTip. Four
/// row combos are both: CmbRampCurve, BubblePop CmbMotion, BubbleCount CmbDifficulty, Wallpaper CmbWpFit.
/// The lock reason always wins while locked and is never taken for the box's own tip; after unlock the box
/// wears its own tip, or the full-value tip only when the value is trimmed at that moment.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowComboSessionLockTests
{
    private const string Reason = "lock reason";
    private const double Wide = 600;
    private const double Narrow = 200;

    private static (ComboBox box, Grid row, string authored) Build(double width)
    {
        var style = (Style)Application.Current.TryFindResource("RowComboBoxStyle")!;
        var c = new ComboBox { Style = style, Width = 150 };
        c.SetBinding(FrameworkElement.ToolTipProperty,
            new Binding("[tooltip_devices_cam_pick]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
        string authored = (string)c.ToolTip;
        c.Items.Add(new ComboBoxItem { Content = "Default (follow multi-monitor)" });
        var row = new Grid { Width = width };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(c, 1);
        row.Children.Add(c);
        c.SelectedIndex = 0;
        Resize(row, c, width);
        return (c, row, authored);
    }

    private static void Resize(Grid row, ComboBox c, double width)
    {
        row.Width = width;
        RowsRender.Layout(row, width);
        RowComboFit.Refresh(c);
        RowsRender.Layout(row, width);
    }

    private static void AssertAuthored(ComboBox c, string authored)
    {
        Assert.Equal(authored, c.ToolTip);
        Assert.NotNull(BindingOperations.GetBindingBase(c, FrameworkElement.ToolTipProperty));
    }

    /// <summary>Lock first, trimmed later: the lock reason stays the tip and never sticks after the session.</summary>
    [Fact]
    public void A_lock_reason_is_never_kept_as_the_boxs_own_tip()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (c, row, authored) = Build(Wide);
            Assert.False(RowComboFit.SelectionCut(c));
            AssertAuthored(c, authored);

            SessionLock.ApplyLockToolTip(c, true, Reason);
            Resize(row, c, Narrow);
            Assert.True(RowComboFit.SelectionCut(c));
            Assert.Equal(Reason, c.ToolTip);

            Resize(row, c, Wide);
            Assert.Equal(Reason, c.ToolTip);
            SessionLock.ApplyLockToolTip(c, false, null);
            AssertAuthored(c, authored);

            Resize(row, c, Narrow);
            var tip = Assert.IsType<StackPanel>(c.ToolTip);
            var lines = tip.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(authored, lines);
            Assert.DoesNotContain(Reason, lines);
            Resize(row, c, Wide);
            AssertAuthored(c, authored);
        });
    }

    /// <summary>Lock while trimmed, unlock while it still is: the full-value tip comes back at once, over the
    /// box's own tip, never the lock reason.</summary>
    [Fact]
    public void Unlock_while_trimmed_shows_the_full_value_over_the_boxs_own_tip()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (c, row, authored) = Build(Wide);
            SessionLock.ApplyLockToolTip(c, true, Reason);
            Resize(row, c, Narrow);
            SessionLock.ApplyLockToolTip(c, false, null);

            var tip = Assert.IsType<StackPanel>(c.ToolTip);
            var lines = tip.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Equal("Default (follow multi-monitor)", lines[0]);
            Assert.Contains(authored, lines);
            Assert.DoesNotContain(Reason, lines);

            Resize(row, c, Wide);
            AssertAuthored(c, authored);
        });
    }

    /// <summary>Trimmed first, fits during the session: unlock gives the box its own tip, not a stale
    /// full-value tip saved at lock time.</summary>
    [Fact]
    public void A_value_tip_saved_before_the_lock_is_not_put_back_after_it()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (c, row, authored) = Build(Narrow);
            Assert.True(RowComboFit.SelectionCut(c));
            Assert.IsType<StackPanel>(c.ToolTip);

            SessionLock.ApplyLockToolTip(c, true, Reason);
            Assert.Equal(Reason, c.ToolTip);
            Resize(row, c, Wide);
            Assert.False(RowComboFit.SelectionCut(c));
            Assert.Equal(Reason, c.ToolTip);

            SessionLock.ApplyLockToolTip(c, false, null);
            AssertAuthored(c, authored);
            Resize(row, c, Wide);
            AssertAuthored(c, authored);
        });
    }

    /// <summary>Trimmed through the whole session: unlock shows the value tip again with the box's own tip.</summary>
    [Fact]
    public void Trimmed_through_the_session_gets_its_value_tip_back()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (c, row, authored) = Build(Narrow);
            SessionLock.ApplyLockToolTip(c, true, Reason);
            SessionLock.ApplyLockToolTip(c, true, Reason);
            Resize(row, c, Narrow);
            Assert.Equal(Reason, c.ToolTip);

            SessionLock.ApplyLockToolTip(c, false, null);
            var tip = Assert.IsType<StackPanel>(c.ToolTip);
            var lines = tip.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(authored, lines);
            Assert.DoesNotContain(Reason, lines);
            Resize(row, c, Wide);
            AssertAuthored(c, authored);
        });
    }
}
