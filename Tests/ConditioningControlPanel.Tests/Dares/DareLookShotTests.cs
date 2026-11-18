using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Dialogs.Dares;
using ConditioningControlPanel.Services.Dares;
using Xunit;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>
/// Stills of the Dares cards (the slip, the offer, the debt card, the intro), drawn off screen, for
/// a look without launching the app. Writes nothing unless <c>CCP_DARES_SHOTS</c> names a folder.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class DareLookShotTests
{
    private sealed class FakeDares : IDaresService
    {
        public event Action? Changed { add { } remove { } }
        public DaresSnapshot Snapshot { get; set; } = DareUiTestKit.Snap();
        public bool IsPreview => true;
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<DareCreateOutcome> CreateAsync(string friendId, DareSlip slip, CancellationToken ct = default)
            => Task.FromResult(new DareCreateOutcome(DareCreateStatus.Sent));
        public Task<DareActResult> ActAsync(string betId, DareOp op, CancellationToken ct = default)
            => Task.FromResult(DareActResult.Done);
    }

    private static T Make<T>(params object?[] args) where T : Window
        => (T)Activator.CreateInstance(typeof(T), BindingFlags.NonPublic | BindingFlags.Instance, null, args, null)!;

    private static void Call(object o, string method, params object?[] args)
        => o.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.Invoke(o, args);

    private static void Settle(int ms)
    {
        var frame = new DispatcherFrame();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
        t.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Shot(Window w, string dir, string name)
    {
        var host = (FrameworkElement)w.Content;
        w.Content = null;
        Shot(host, w.Width, dir, name);
    }

    private static void Shot(FrameworkElement host, double width, string dir, string name)
    {
        Settle(900);
        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, host.DesiredSize.Height)));
        host.UpdateLayout();
        Settle(200);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        enc.Save(fs);
    }

    [Fact]
    public void Shots_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_DARES_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        WpfRenderHarness.OnStaThread(() =>
        {
            var seen = DareIntro.Seen;
            DareIntro.Seen = () => true;
            try
            {
                var svc = new FakeDares();
                var friends = new List<DareFriend> { DareUiTestKit.Moth, new("u-pix", "Pixel", null), new("u-juno", "Juno", null) };

                var w1 = Make<DareSlipWindow>(svc, DareUiTestKit.Moth, friends, null);
                var d = w1.Draft;
                DareSlipRules.SetType(d, DareType.Cant);
                DareSlipRules.PickKind(d, DareKind.Pics);
                Call(w1, "Render");
                Shot(w1, dir, "1-slip-what");

                var w2 = Make<DareSlipWindow>(svc, DareUiTestKit.Moth, friends, null);
                d = w2.Draft;
                DareSlipRules.SetType(d, DareType.Cant);
                DareSlipRules.PickKind(d, DareKind.Pics);
                DareSlipRules.PickTarget(d, DareSlipRules.TargetsFor(DareType.Cant, DareKind.Pics)[1]);
                DareSlipRules.SetDeadline(d, DareDeadline.Today);
                DareSlipRules.PickStakeKind(d, DareStakeKind.Sp);
                DareSlipRules.PickAmount(d, DareGrammar.AmountsFor(DareStakeKind.Sp)[1]);
                DareSlipRules.PickStakeKind(d, DareStakeKind.Lockdown);
                Call(w2, "Render");
                Shot(w2, dir, "2-slip-stake");

                var w3 = Make<DareSlipWindow>(svc, DareUiTestKit.Moth, friends, null);
                d = w3.Draft;
                DareSlipRules.SetType(d, DareType.Duel);
                DareSlipRules.PickKind(d, DareKind.Video);
                DareSlipRules.SetDeadline(d, DareDeadline.Today);
                DareSlipRules.PickStakeKind(d, DareStakeKind.Lockdown);
                DareSlipRules.PickAmount(d, DareGrammar.AmountsFor(DareStakeKind.Lockdown)[1]);
                DareSlipRules.PickStakeKind(d, DareStakeKind.Lockdown);
                DareSlipRules.PickAmount(d, DareGrammar.AmountsFor(DareStakeKind.Lockdown)[1]);
                Call(w3, "Render");
                Shot(w3, dir, "3-slip-camera");

                var w4 = Make<DareSlipWindow>(svc, DareUiTestKit.Moth, friends, null);
                d = w4.Draft;
                DareSlipRules.SetType(d, DareType.Cant);
                DareSlipRules.PickKind(d, DareKind.Minutes);
                DareSlipRules.PickTarget(d, DareSlipRules.TargetsFor(DareType.Cant, DareKind.Minutes)[0]);
                DareSlipRules.SetDeadline(d, DareDeadline.Weekend);
                DareSlipRules.PickStakeKind(d, DareStakeKind.Lock);
                DareSlipRules.PickAmount(d, DareGrammar.AmountsFor(DareStakeKind.Lock)[0]);
                DareSlipRules.PickStakeKind(d, DareStakeKind.Sp);
                DareSlipRules.PickAmount(d, DareGrammar.AmountsFor(DareStakeKind.Sp)[0]);
                DareSlipRules.SetCamera(d, true);
                Call(w4, "Render");
                Shot(w4, dir, "4-slip-send");

                var offer = DareUiTestKit.Bet(me: DareSide.To, state: DareState.Sent, camera: true,
                    from: new DareStake(DareStakeKind.Sp, 100), to: new DareStake(DareStakeKind.Lockdown, 20));
                svc.Snapshot = DareUiTestKit.Snap(offer);
                var w5 = Make<DareOfferCard>(svc, offer);
                Shot(w5, dir, "5-offer");

                var w6 = Make<DareOfferCard>(svc, offer);
                Call(w6, "AnswerAsync", DareOp.Accept);
                Shot(w6, dir, "6-offer-accepted");

                var debt = DareUiTestKit.Bet(state: DareState.Settled, from: new DareStake(DareStakeKind.Lockdown, 20));
                var w7 = Make<DareDebtCard>(svc, debt);
                Shot(w7, dir, "7-debt");

                var w8 = Make<DareDebtCard>(svc, debt);
                Call(w8, "OnPay");
                Shot(w8, dir, "8-debt-consent");

                var w9 = Make<DareSlipWindow>(svc, DareUiTestKit.Moth, friends, null);
                ((Border)((Grid)w9.Content).Children[1]).Child = DareIntro.Build(() => { });
                Shot(w9, dir, "9-intro");

                var strip = new StackPanel { Background = ConditioningControlPanel.Controls.Friends.FriendsLook.PanelBrush };
                foreach (var seg in new[]
                {
                    new DareSeg(DareSegKind.Chip, "watch 30 min of video", DareStep.What, DareTone.Video),
                    new DareSeg(DareSegKind.Chip, "20 min Lockdown", DareStep.Mine, DareTone.Lockdown),
                    new DareSeg(DareSegKind.Chip, "100 Sparkles", DareStep.Mine, DareTone.Sp),
                })
                {
                    var f = DareLook.Fill(seg, null, small: true);
                    f.Margin = new Thickness(12, 6, 12, 6);
                    strip.Children.Add(f);
                }
                Shot(strip, 300, dir, "10-drawer-fills");
            }
            finally { DareIntro.Seen = seen; }
        });
    }
}
