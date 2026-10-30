using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Path = Avalonia.Controls.Shapes.Path;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private readonly VatFaucetHold _faucetHold = new(new AppSettingsVatPourLedger());
    private readonly VatFillCoordinator _faucetCoordinator = new();
    private readonly Stopwatch _faucetClock = new();
    private DispatcherTimer? _faucetChargeTimer;
    private bool _faucetWired, _faucetCharging, _faucetKeyHeld;
    private double _faucetBudget;
    private int _faucetPours, _faucetRungs;
    private DateTime _faucetDay;
    private T? FaucetControl<T>(string name) where T : Control => ProfilePage?.FindControl<T>(name);

    // WPF MainWindow.ProfileVat.cs:268-334. Read server truth through the shared hold.
    private void ApplyFaucetReading()
    {
        var glass = VatGlass;
        if (glass == null) return;
        var block = _vatDescent?.Current;
        var read = _faucetCoordinator.Apply(block);
        if (block?.Vat == null)
        {
            CancelFaucetCharge();
            _faucetHold.Reset();
            DisarmVat();
            return;
        }
        ArmVat(glass, read.Cap);
        glass.SetLip(read.Lip);
        var step = _faucetHold.Fold(read, glass.IsPouring);
        switch (step.Action)
        {
            case FaucetActionKind.Snap: glass.SnapTo(step.Fill); break;
            case FaucetActionKind.Ease: glass.EaseTo(step.Fill); break;
            case FaucetActionKind.Pour:
                if (glass.IsPresenting) glass.PourTo(step.Fill, userGesture: true);
                else glass.EaseTo(step.Fill);
                break;
        }
        UpdateFaucetPresentation();
    }

    // WPF MainWindow.ProfileFaucet.cs:141-205, 280-292, 552-603.
    private void ArmFaucet(VatGlassCanvas glass, double w, double h)
    {
        var tap = FaucetControl<Grid>("ProfileVatFaucet");
        if (tap == null) return;
        double left = Math.Round(w * .10 - 8), top = Math.Round(h * .175 - 36);
        tap.Margin = new Thickness(left, top, 0, 0);
        tap.IsVisible = true;
        glass.ExternalSpoutXFraction = (left + 27) / w;
        if (FaucetControl<Grid>("ProfileVatChargeRing") is { } ring)
            ring.Margin = new Thickness(left - 2, top + 1, 0, 0);
        if (FaucetControl<Border>("ProfileVatChip") is { } chip)
            chip.Margin = new Thickness(0, Math.Round(h * .175) + 8, 0, 0);
        if (_faucetWired) return;
        _faucetWired = true;
        tap.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tap).Properties.IsLeftButtonPressed) return;
            e.Handled = true; tap.Focus(); e.Pointer.Capture(tap); BeginFaucetCharge();
        };
        tap.PointerReleased += (_, e) => { e.Handled = true; CancelFaucetCharge(true); e.Pointer.Capture(null); };
        tap.PointerExited += (_, _) => CancelFaucetCharge(true);
        tap.PointerCaptureLost += (_, _) => CancelFaucetCharge();
        tap.LostFocus += (_, _) => { _faucetKeyHeld = false; CancelFaucetCharge(); };
        tap.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Space or Key.Enter)) return;
            e.Handled = true;
            if (_faucetKeyHeld) return; // WPF ProfileFaucet.cs:589: one press, including after a completed pour
            _faucetKeyHeld = true;
            BeginFaucetCharge();
        };
        tap.KeyUp += (_, e) =>
        {
            if (e.Key is not (Key.Space or Key.Enter)) return;
            e.Handled = true; _faucetKeyHeld = false; CancelFaucetCharge(true);
        };
        _faucetChargeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _faucetChargeTimer.Tick += (_, _) => AdvanceFaucetCharge();
    }

    // WPF MainWindow.ProfileFaucet.cs:296-361. Existing localized copy, still waiting badge.
    private void UpdateFaucetPresentation()
    {
        int held = _faucetHold.HeldXp;
        bool waiting = held > 0 && VatGlass?.IsPouring != true && !_faucetCharging;
        if (FaucetControl<Grid>("ProfileVatFaucet") is { } tap)
        {
            var text = new StackPanel { MaxWidth = 320 };
            text.Children.Add(new TextBlock { Text = held > 0 ? Loc.GetF("profile_faucet_tip_held", held) : Loc.Get("profile_faucet_tip_empty"), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = Loc.Get("profile_vat_help_tip"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = .72, Margin = new Thickness(0, 5, 0, 0) });
            ToolTip.SetTip(tap, text);
        }
        if (FaucetControl<Border>("ProfileVatChip") is { } chip) chip.IsVisible = waiting;
        if (FaucetControl<TextBlock>("ProfileVatChipText") is { } label) label.Text = Loc.GetF("profile_vat_chip", held);
        if (FaucetControl<Control>("ProfileVatFaucetBadge") is { } badge) badge.IsVisible = held > 0;
    }

    // WPF MainWindow.ProfileFaucet.cs:609-683, 1080-1095. Same budget and daily compact cut.
    private void BeginFaucetCharge()
    {
        if (_faucetCharging || !_vatArmed || !_vatOnScreen || VatGlass?.IsPouring != false || _faucetHold.HeldXp <= 0) return;
        if (_faucetDay != DateTime.UtcNow.Date) { _faucetDay = DateTime.UtcNow.Date; _faucetPours = 0; }
        _faucetBudget = Math.Min(1400, 700 + _faucetHold.HeldXp / 4.0);
        if (_faucetPours >= 3) _faucetBudget *= .6;
        _faucetCharging = true;
        _faucetRungs = 1;
        _faucetClock.Restart();
        PlayFaucetSfx("faucet_charge_1.wav", .15f);
        DrawFaucetRing(0);
        _faucetChargeTimer?.Start();
        UpdateFaucetPresentation();
    }

    private void AdvanceFaucetCharge()
    {
        if (!_faucetCharging)
        {
            if (VatGlass?.IsPouring != true) { _faucetChargeTimer?.Stop(); UpdateFaucetPresentation(); }
            return;
        }
        double t = Math.Clamp(_faucetClock.Elapsed.TotalMilliseconds / _faucetBudget, 0, 1);
        DrawFaucetRing(t);
        int want = Math.Min(6, (int)(t * 6) + 1);
        while (_faucetRungs < want) PlayFaucetSfx($"faucet_charge_{++_faucetRungs}.wav", .15f);
        if (t < 1) return;
        CancelFaucetCharge();
        var glass = VatGlass;
        if (!_vatArmed || glass == null || _faucetHold.HeldXp <= 0) return;
        bool wasOver = glass.IsPastBrim(glass.Fill);
        var step = _faucetHold.PourAll();
        PlayFaucetSfx("faucet_pour.wav", .35f);
        glass.PourTo(step.Fill, userGesture: true);
        if (!wasOver && glass.IsPastBrim(step.Fill)) { glass.PulseOverflow(); PlayFaucetSfx("faucet_brim.wav", .30f); }
        _faucetPours++;
        _faucetChargeTimer?.Start();
        UpdateFaucetPresentation();
    }

    // WPF MainWindow.ProfileFaucet.cs:694-717. Cancellation never stamps the ledger.
    private void CancelFaucetCharge(bool feedback = false)
    {
        bool charging = _faucetCharging;
        _faucetCharging = false;
        _faucetClock.Stop();
        _faucetChargeTimer?.Stop();
        if (FaucetControl<Grid>("ProfileVatChargeRing") is { } ring) { ring.IsVisible = false; ring.Opacity = 0; }
        if (charging && feedback) PlayFaucetSfx("faucet_charge_drop.wav", .15f);
        UpdateFaucetPresentation();
    }

    // WPF MainWindow.ProfileFaucet.cs:779-860. Reduced motion uses a whole still circle.
    private void DrawFaucetRing(double t)
    {
        if (FaucetControl<Grid>("ProfileVatChargeRing") is { } ring) { ring.IsVisible = true; ring.Opacity = 1; }
        if (FaucetControl<Path>("ProfileVatChargeArc") is not { } arc) return;
        if (AmbientFxCanvas.Env.Level != MotionLevel.Full || t >= .999)
        { arc.Data = new EllipseGeometry(new Rect(2, 2, 30, 30)); return; }
        var geometry = new StreamGeometry();
        if (t > .002)
        {
            using var ctx = geometry.Open();
            ctx.BeginFigure(new Point(17, 2), false);
            double a = t * 2 * Math.PI;
            ctx.ArcTo(new Point(17 + 15 * Math.Sin(a), 17 - 15 * Math.Cos(a)), new Size(15, 15), 0, t > .5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        arc.Data = geometry;
    }

    // WPF MainWindow.ProfileFaucet.cs:1112-1124. The existing audio seam owns device lifetime.
    private static void PlayFaucetSfx(string file, float scale)
    {
        float volume = Math.Clamp((float)CoreSettings.Current.MasterVolume / 100 * scale, 0, 1);
        if (volume > 0) CoreAudio.PlayOneShot(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", file), volume, "vat-faucet");
    }
}
