using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    [Fact]
    public Task RailChipDressingTracksDrawer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var owner = new Window { Content = chip, Width = 400, Height = 400 };
        owner.Show();
        try
        {
            var pill = Tagged<Border>(chip, "friends-chip-online")!;
            var badge = Tagged<Border>(chip, "friends-chip-unread")!;
            var face = Assert.IsType<Border>(chip.Children[0]);
            Assert.Equal(new Thickness(2), pill.BorderThickness);
            Assert.Equal(Color.FromRgb(0x0E, 0x09, 0x19), Assert.IsAssignableFrom<ISolidColorBrush>(pill.BorderBrush).Color);
            Assert.Equal(8, pill.BoxShadow[0].Blur);
            Assert.Equal(Color.FromArgb(0xB3, 0x5F, 0xFF, 0xD0), pill.BoxShadow[0].Color);
            Assert.Equal(8, badge.BoxShadow[0].Blur);
            Assert.Equal(Color.FromArgb(0xB3, 0xFF, 0x5F, 0xB4), badge.BoxShadow[0].Color);
            Assert.Equal(new Thickness(1), face.BorderThickness);
            chip.RefreshFace(); // repeated refresh must not alter the open/close wiring
            chip.Toggle();
            Assert.True(chip.IsOpen);
            Assert.Same(FriendsDrawer.Line2, face.BorderBrush);
            chip.Toggle();
            Assert.False(chip.IsOpen);
            Assert.Same(Brushes.Transparent, face.BorderBrush);
            await Task.CompletedTask;
        }
        finally { owner.Close(); svc.Dispose(); }
    });
}
