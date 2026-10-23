using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Rows ctrl-sparkle-wallet / shell-sparkle-wallet: the header Sparkle wallet (WPF
/// Controls/SparkleWallet.xaml(.cs), MainWindow.SparkleWallet.cs). From shell startup it shows the one
/// SkillPoints balance, follows every change, plays the reward cue only for a SparklePointRewards
/// award and clears it after 1.6 s; its popups toggle like WPF's; the croupier sprite paints.</summary>
public sealed class SparkleWalletTests
{
    private static void Platform()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Step(SteppedTime time, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        BeatLoop.StepAll();
    }

    [Fact]
    public void ShellWalletFollowsTheBalanceAndCuesOnlyAwards() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        var s = CoreSettings.Current;
        var old = (s.SkillPoints, s.MotionLevel, s.PerformanceMode);
        var time = new SteppedTime();
        BeatLoop.Time = time;
        MainShellWindow? shell = null;
        try
        {
            (s.SkillPoints, s.MotionLevel, s.PerformanceMode) = (1234, MotionLevel.Full, false);
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var wallet = shell.Named<SparkleWallet>("HeaderSparkleWallet")!;
            Assert.Equal(1234, wallet.Balance);

            // A balance change (purchase, sync) refreshes but never cues a reward (WPF :38, xaml.cs:16).
            s.SkillPoints = 1200;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1200, wallet.Balance);
            Assert.False(wallet.RewardShowing);

            // A settled local credit: refresh + "+N" badge, which accumulates and clears after 1.6 s.
            s.SkillPoints = 1201;
            SparklePointRewards.PublishCredit(1200, 1201, SparklePointSource.LevelUp);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1201, wallet.Balance);
            Assert.True(wallet.RewardShowing);
            Assert.Contains("1", wallet.GainLabel);
            Step(time, 0.5);
            SparklePointRewards.PublishCredit(1201, 1203, SparklePointSource.BubbleMilestone);
            Assert.Contains("3", wallet.GainLabel);
            Step(time, 1.0);
            Assert.True(wallet.RewardShowing);  // the window restarted on the second award
            Step(time, 0.7);
            Assert.False(wallet.RewardShowing);

            // Closing the shell unsubscribes (WPF ShutdownSparkleWallet): the static award event no
            // longer holds the shell (a detached wallet would ignore rewards anyway, so check the list).
            Assert.True(AwardSubscribed(shell));
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(AwardSubscribed(shell));
            shell = null;
        }
        finally
        {
            shell?.Close();
            (s.SkillPoints, s.MotionLevel, s.PerformanceMode) = old;
            BeatLoop.Time = TimeProvider.System;
        }
    });

    private static bool AwardSubscribed(object target) =>
        (typeof(SparklePointRewards).GetField(nameof(SparklePointRewards.Awarded),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as Delegate)?.GetInvocationList().Any(d => ReferenceEquals(d.Target, target)) == true;

    [Fact]
    public void WalletPopupsWaveAndVisitBehaveLikeWpf() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode);
        var time = new SteppedTime();
        BeatLoop.Time = time;
        Window? w = null;
        try
        {
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
            var wallet = new SparkleWallet { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
            w = new Window { Width = 400, Height = 200, Content = wallet };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var walletButton = wallet.FindControl<Button>("WalletButton")!;
            var help = wallet.FindControl<Button>("HelpButton")!;
            int visits = 0;
            wallet.VisitRequested += (_, _) => visits++;

            // Wallet click toggles the invite; "?" opens help and closes the invite (xaml.cs:49-72).
            walletButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(wallet.InviteOpen);
            help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(wallet.InviteOpen);
            Assert.True(wallet.HelpOpen);
            // Escape closes (xaml.cs:79-83).
            wallet.FindControl<SparkleWalletHelpCard>("HelpCard")!
                .RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(wallet.HelpOpen);
            // Visit closes the popup and asks the shell for the Back Room (xaml.cs:62).
            walletButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            wallet.FindControl<SparkleWalletInviteCard>("InviteCard")!.FindControl<Button>("VisitButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(wallet.InviteOpen);
            Assert.Equal(1, visits);

            // Hover waves the croupier's arm once (620 ms), then it rests; hidden it stops (P01).
            walletButton.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, walletButton, null!, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.True(wallet.Waving);
            Step(time, 0.1);
            Assert.InRange(wallet.ArmAngle, -17.5, -16.5);
            Step(time, 0.6);
            Assert.False(wallet.Waving);
            Assert.Equal(0, wallet.ArmAngle);
            walletButton.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, walletButton, null!, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
            wallet.IsVisible = false;
            Assert.False(wallet.Waving);

            // Motion Off: no wave at all (AnimateInteractions, xaml.cs:168).
            wallet.IsVisible = true;
            s.MotionLevel = MotionLevel.Off;
            walletButton.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, walletButton, null!, w, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.False(wallet.Waving);
        }
        finally
        {
            w?.Close();
            (s.MotionLevel, s.PerformanceMode) = old;
            BeatLoop.Time = TimeProvider.System;
        }
    });

    [Fact]
    public void WalletVisorUsesTheEmiGeometryRenderer() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        var wallet = new SparkleWallet();
        var face = wallet.FindControl<Control>("MascotFace");
        Assert.NotNull(face);
        // WPF draws the ink outline in Noto Sans Mono, not a scaled Press Start text label.
        Assert.IsNotType<TextBlock>(face);
        Assert.Equal("^_^", Assert.IsType<WalletEmiFace>(face).Face);
        Assert.Equal(389, face.Width);
        Assert.Equal(349, face.Height);
        Assert.Equal(new Thickness(320, 297, 0, 0), face.Margin);
    });

    [Fact]
    public void WalletFacePaintsTheFittedPinkInkAndClears() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        var face = new WalletEmiFace { Width = 152, Height = 137 };
        face.Draw("^_^");
        var ink = WalletEmiFace.FitInk("^_^", out var geometry);
        Assert.NotNull(geometry);
        // WPF EmiFace.Paint: 95% fit box less the 5 px stroke on each side.
        Assert.InRange(ink.Width, 1, 152 * .95 - 10);
        Assert.InRange(ink.Height, 1, 137 * .95 - 10);
        face.Measure(new Size(152, 137));
        face.Arrange(new Rect(0, 0, 152, 137));
        using var frame = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(152, 137));
        frame.Render(face);
        var px = new byte[152 * 137 * 4];
        void Read()
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { frame.CopyPixels(new PixelRect(new PixelSize(152, 137)), handle.AddrOfPinnedObject(), px.Length, 152 * 4); }
            finally { handle.Free(); }
        }
        Read();
        int count = 0, left = 152, right = 0, top = 137, bottom = 0;
        for (int y = 0; y < 137; y++)
            for (int x = 0; x < 152; x++)
            {
                int i = (y * 152 + x) * 4;
                // Accept BGRA and RGBA: EMI pink has both outer channels above green.
                if (px[i + 3] < 200 || Math.Min(px[i], px[i + 2]) <= px[i + 1] + 30) continue;
                count++; left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        Assert.True(count > 400, $"expected EMI's pink glyph ink, found {count} pixels");
        Assert.InRange((left + right) / 2.0, 74, 78);
        Assert.InRange((top + bottom) / 2.0, 63.5, 67.5); // 137/2 - 137*.02, stroke centred
        face.Draw(null);
        using var cleared = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(152, 137));
        cleared.Render(face);
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { cleared.CopyPixels(new PixelRect(new PixelSize(152, 137)), pin.AddrOfPinnedObject(), px.Length, 152 * 4); }
        finally { pin.Free(); }
        Assert.All(px, b => Assert.Equal((byte)0, b));
    });

    /// <summary>WPF SparkleWalletRenderTests: the croupier sprite really paints (a missing resource
    /// link would leave the mascot column empty, silently).</summary>
    [Fact]
    public void WalletPaintsTheCroupierEmi() => AvaloniaTestDispatcher.Run(() =>
    {
        Platform();
        Window? w = null;
        try
        {
            w = new Window { Width = 300, Height = 100, Background = global::Avalonia.Media.Brushes.Black,
                Content = new SparkleWallet { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top } };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = w.CaptureRenderedFrame()!;
            var stride = frame.PixelSize.Width * 4;
            var px = new byte[stride * frame.PixelSize.Height];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { frame.CopyPixels(new PixelRect(frame.PixelSize), handle.AddrOfPinnedObject(), px.Length, stride); }
            finally { handle.Free(); }
            int gold = 0;
            for (int y = 0; y < 29; y++)
                for (int x = 0; x < 31; x++)
                {
                    int i = y * stride + x * 4;
                    // Byte order differs by platform format (Bgra/Rgba): red is the larger outer channel.
                    int r = Math.Max(px[i], px[i + 2]), b = Math.Min(px[i], px[i + 2]), g = px[i + 1];
                    if (r > 120 && g > 90 && r > b + 40 && g > b + 20) gold++;
                }
            Assert.True(gold > 50, $"expected the gold croupier casing in the mascot column, found {gold} gold pixels");
        }
        finally { w?.Close(); }
    });
}
