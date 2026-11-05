using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsDrawerFinishTests
{
    private static void Init()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static T? Find<T>(Control d, string tag) where T : Control =>
        d.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);
    private static void Click(Control d, string tag) =>
        Assert.IsType<Button>(Find<Button>(d, tag)).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task LockDayAndTierPlatesAppearOnTheRealRows(bool tiers) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var (svc, wire) = FriendsDrawerTests.Service();
        wire.StateBody = wire.StateBody.Replace("\"tier\":0", "\"tier\":2");
        var oldArt = FriendsDrawer.TierArt;
        using var art = new WriteableBitmap(new PixelSize(8, 4), new Vector(96, 96));
        FriendsDrawer.TierArt = _ => art;
        try
        {
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc) { MeTier = () => 1 };
            d.Render();
            var row = Find<Border>(d, "friends-row:u_on")!;
            if (tiers)
            {
                Assert.Equal(15, Find<Image>(d, "friends-tier-1")!.Height);
                Assert.Equal(12, Find<Image>(row, "friends-tier-2")!.Height);
                Assert.Null(FriendsDrawer.TierPlate(0, 12));
                FriendsDrawer.TierArt = _ => null;
                d.Render();
                Assert.Null(Find<Image>(d, "friends-tier-1"));
                Assert.Null(Find<Image>(d, "friends-tier-2"));
            }
            else
            {
                var chip = Find<Border>(row, "friends-lock")!;
                Assert.Contains(chip.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.GetF("friends_lock_day", 3));
                Assert.Null(Find<Border>(Find<Border>(d, "friends-row:u_off")!, "friends-lock"));
            }
        }
        finally { FriendsDrawer.TierArt = oldArt; svc.Dispose(); }
    });

}
