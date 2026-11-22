using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Stills of a friend's expanded card in the drawer (FriendCardShotTests): with a leash to offer,
/// without one, the Goon tile locked to a free account, a greyed and a sent offer, and German.
/// Drawn off screen at the drawer's real width; writes nothing unless <c>CCP_FRIENDCARD_SHOTS</c>
/// names a folder (<c>CCP_FRIENDCARD_TAG</c> prefixes the file names, "after" by default).
/// </summary>
public partial class FriendsDrawerTests
{
    [Fact]
    public void Friend_card_shots_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_FRIENDCARD_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        var tag = Environment.GetEnvironmentVariable("CCP_FRIENDCARD_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "after";
        Directory.CreateDirectory(dir);
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        var service = LeashLocator.Service;
        var goon = InviteCodes.GoonCode;
        var canHost = InviteCodes.CanHostGoon;
        try
        {
            foreach (var lang in new[] { "en", "de" })
            {
                loc.SetLanguage(lang);
                WpfRenderHarness.OnStaThread(() =>
                {
                    InviteCodes.GoonCode = () => null;
                    var leash = FakeLeashService.Sample();
                    LeashLocator.Service = () => leash;
                    var now = DateTimeOffset.UtcNow;
                    var sam = new Friend("sam", "Sam", null, 2, true, new FriendPresence(PresenceActivity.BackRoom, null, now), false);
                    var noor = new Friend("noor", "Noor Longername", null, 0, false, new FriendPresence(PresenceActivity.Offline, null, now.AddDays(-10)), false);
                    var snap = Sample() with { Friends = new[] { sam, noor } };

                    InviteCodes.CanHostGoon = () => true;
                    Card(snap, "sam", null, dir, $"{tag}-{lang}-1-leash");
                    Card(snap, "sam", "poke", dir, $"{tag}-{lang}-2-poke");
                    Card(snap, "sam", "watch", dir, $"{tag}-{lang}-3-watch");
                    InviteCodes.CanHostGoon = () => false;
                    Card(snap, "noor", "invite", dir, $"{tag}-{lang}-4-locked-greyed");
                    InviteCodes.CanHostGoon = () => true;

                    leash.OfferAsync("sam").GetAwaiter().GetResult();
                    LeashSurfaces.SentOffers.Add("sam");
                    Card(snap, "sam", null, dir, $"{tag}-{lang}-5-sent");
                    LeashSurfaces.SentOffers.Remove("sam");

                    var off = new FakeLeashService { Available = false };
                    LeashLocator.Service = () => off;
                    Card(snap, "sam", "poke", dir, $"{tag}-{lang}-6-no-leash");
                });
            }
        }
        finally
        {
            loc.SetLanguage(previous);
            LeashLocator.Service = service;
            InviteCodes.GoonCode = goon;
            InviteCodes.CanHostGoon = canHost;
        }
    }

    private static void Card(FriendsSnapshot snap, string id, string? picker, string dir, string name)
    {
        var d = NewDrawer(new FakeFriends(snap));
        if (picker == null) d.Toggle(id);
        else d.OpenPickerFor(id, picker);
        ShotSettle(500);
        d.Measure(new Size(FriendsDrawer.DrawerWidth, 2000));
        d.Arrange(new Rect(0, 0, FriendsDrawer.DrawerWidth, d.DesiredSize.Height));
        d.UpdateLayout();
        ShotSettle(200);
        var row = d.RowFor(id)!;
        double w = row.ActualWidth + 16, h = row.ActualHeight + 16;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(FriendsLook.PanelBrush, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(row) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(8, 8, row.ActualWidth, row.ActualHeight));
        }
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * 2), (int)Math.Ceiling(h * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        enc.Save(fs);
    }

    private static void ShotSettle(int ms)
    {
        var frame = new DispatcherFrame();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
        t.Start();
        Dispatcher.PushFrame(frame);
    }
}
