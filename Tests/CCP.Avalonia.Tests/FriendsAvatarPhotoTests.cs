using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF FriendsLook.Avatar :184 and PaintPhotoAsync :253: a friend or request row with a picture shows it
/// over the initials, proxy paths resolve against the proxy, only https is fetched, one fetch per URL.</summary>
public sealed class FriendsAvatarPhotoTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static Border Disc(Control row) =>
        row.GetLogicalDescendants().OfType<Border>().First(b => b.CornerRadius.TopLeft == 19);

    [Fact]
    public Task RowsWithAPicturePaintItOverTheInitialsOncePerUrl() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var id = Guid.NewGuid().ToString("N");
        var fetched = new List<string>();
        var oldFetch = KnockCard.FetchPicture;
        KnockCard.FetchPicture = url => { fetched.Add(url); return Task.FromResult(Png); };
        try
        {
            var (svc, wire) = FriendsDrawerTests.Service();
            wire.StateBody = wire.StateBody
                .Replace("\"name\":\"Mia\",", $"\"name\":\"Mia\",\"avatar\":\"/img/{id}.png\",")
                .Replace("\"name\":\"Zed\",", $"\"name\":\"Zed\",\"avatar\":\"http://example.com/{id}.png\",")
                .Replace("\"name\":\"Ann\",", $"\"name\":\"Ann\",\"avatar\":\"https://example.com/{id}.png\",");
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            d.Render();
            Border Row(string tag) => d.GetLogicalDescendants().OfType<Border>().First(b => b.Tag as string == tag);
            await FriendsDrawerTests.Until(() => Disc(Row("friends-row:u_on")).Background is ImageBrush
                && Disc(Row("friends-request-in:u_req")).Background is ImageBrush);

            Assert.IsType<ImageBrush>(Disc(Row("friends-row:u_on")).Background);
            Assert.Null(Disc(Row("friends-row:u_on")).Child);
            Assert.IsType<ImageBrush>(Disc(Row("friends-request-in:u_req")).Background);
            // Plain http is never fetched and keeps the initials.
            Assert.IsNotType<ImageBrush>(Disc(Row("friends-row:u_off")).Background);
            Assert.IsType<TextBlock>(Disc(Row("friends-row:u_off")).Child);
            Assert.Equal(new[] { ProviderSubscription.ProxyBaseUrl + $"/img/{id}.png", $"https://example.com/{id}.png" },
                fetched.OrderBy(u => u.StartsWith("https://example") ? 1 : 0).ToArray());

            // A redraw reuses the decoded picture without another fetch.
            d.Render();
            Assert.IsType<ImageBrush>(Disc(Row("friends-row:u_on")).Background);
            Assert.Equal(2, fetched.Count);
        }
        finally { KnockCard.FetchPicture = oldFetch; }
    });

    [Fact]
    public Task AFailedPictureKeepsTheInitials() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var id = Guid.NewGuid().ToString("N");
        var oldFetch = KnockCard.FetchPicture;
        int calls = 0;
        KnockCard.FetchPicture = _ => { calls++; return Task.FromException<byte[]>(new InvalidOperationException("offline")); };
        try
        {
            var (svc, wire) = FriendsDrawerTests.Service();
            wire.StateBody = wire.StateBody.Replace("\"name\":\"Mia\",", $"\"name\":\"Mia\",\"avatar\":\"/img/{id}.png\",");
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            d.Render();
            await FriendsDrawerTests.Until(() => calls > 0);
            var disc = Disc(d.GetLogicalDescendants().OfType<Border>().First(b => b.Tag as string == "friends-row:u_on"));
            Assert.True(calls >= 1);   // failures are not cached (WPF :262), each draw may retry
            Assert.IsNotType<ImageBrush>(disc.Background);
            Assert.IsType<TextBlock>(disc.Child);
        }
        finally { KnockCard.FetchPicture = oldFetch; }
    });
}
