using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
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
    private BeatLoop? _faucetChargeBeat;
    private bool _faucetWired, _faucetCharging, _faucetKeyHeld;
    private double _faucetBudget;
    private int _faucetPours, _faucetRungs;
    private DateTime _faucetDay;
    private T? FaucetControl<T>(string name) where T : Control => ProfilePage?.FindControl<T>(name);
    private void ApplyFaucetReading() // WPF MainWindow.ProfileVat.cs:268-334. Read server truth through the shared hold.
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
        PositionVatTickGlyphs(); // WPF MainWindow.ProfileVat.cs:280: the MAX tick moves with the lip
        if (_vatOnScreen) MaybeShowFeatureIntro("descent-vat", "discord"); // WPF MainWindow.ProfileVat.cs:300
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
        UpdateVatReadout(); // WPF MainWindow.ProfileVat.cs:340-344: cap retunes may keep the whole percent unchanged
        UpdateFaucetPresentation();
    }
    private void ArmFaucet(VatGlassCanvas glass, double w, double h) // WPF MainWindow.ProfileFaucet.cs:141-205, 280-292, 552-603.
    {
        var tap = FaucetControl<Grid>("ProfileVatFaucet");
        if (tap == null) return;
        double left = Math.Round(w * .10 - 8), top = Math.Round(h * .175 - 36);
        tap.Margin = new Thickness(left, top, 0, 0);
        tap.IsVisible = true;
        glass.ExternalSpoutXFraction = (left + 27) / w;
        PositionVatTickGlyphs(); // WPF MainWindow.ProfileFaucet.cs:176
        if (FaucetControl<Canvas>("ProfileVatSparks") is { } sparks) { sparks.Width = w; sparks.Height = h; }
        if (FaucetControl<Grid>("ProfileVatChargeRing") is { } ring)
            ring.Margin = new Thickness(left - 2, top + 1, 0, 0);
        if (FaucetControl<Border>("ProfileVatChip") is { } chip)
            chip.Margin = new Thickness(0, Math.Round(h * .175) + 8, 0, 0);
        if (_faucetWired) return;
        _faucetWired = true;
        // The canvas answers tick positions from its laid-out size: re-hang the legend when it changes.
        glass.SizeChanged += (_, _) => { if (_vatArmed) PositionVatTickGlyphs(); };
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
        _faucetChargeBeat = new BeatLoop(tap, seconds => AdvanceFaucetCharge(seconds * 1000)) { OnParked = () => CancelFaucetCharge() };
    }
    /// <summary>WPF MainWindow.ProfileFaucet.cs:189-217: the moon, mortarboard and crown hang just inside
    /// their tick lines, from the canvas's own tick maths; a mark with no line (no lip, no layout) hides.</summary>
    private void PositionVatTickGlyphs()
    {
        if (VatGlass is not { } glass) return;
        double innerX = glass.TickInnerX;
        Place("ProfileVatTickDrain", VatGlassCanvas.VatTickMark.Drain);
        Place("ProfileVatTickCap", VatGlassCanvas.VatTickMark.Cap);
        Place("ProfileVatTickMax", VatGlassCanvas.VatTickMark.Max);
        void Place(string name, VatGlassCanvas.VatTickMark mark)
        {
            if (FaucetControl<IconGlyph>(name) is not { } glyph) return;
            double? y = glass.TickCenterY(mark);
            glyph.IsVisible = y != null && innerX > 0;
            if (glyph.IsVisible)
                glyph.Margin = new Thickness(Math.Round(innerX - 3 - glyph.Size), Math.Round(y!.Value - glyph.Size / 2), 0, 0);
        }
    }
    private void UpdateFaucetPresentation() // WPF MainWindow.ProfileFaucet.cs:296-361. Existing localized copy, still waiting badge.
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
    private void BeginFaucetCharge() // WPF MainWindow.ProfileFaucet.cs:609-683, 1080-1095. Same budget and daily compact cut.
    {
        if (_faucetCharging || !_vatArmed || !_vatOnScreen || VatGlass?.IsPouring != false || _faucetHold.HeldXp <= 0) return;
        if (_faucetDay != DateTime.UtcNow.Date) { _faucetDay = DateTime.UtcNow.Date; _faucetPours = 0; }
        _faucetBudget = Math.Min(1400, 700 + _faucetHold.HeldXp / 4.0);
        if (_faucetPours >= 3) _faucetBudget *= .6;
        _faucetCharging = true;
        _faucetRungs = 1;
        PlayFaucetSfx("faucet_charge_1.wav", .15f);
        DrawFaucetRing(0);
        _faucetChargeBeat?.Start();
        UpdateFaucetPresentation();
    }
    private void AdvanceFaucetCharge(double ms)
    {
        if (!_faucetCharging)
        {
            if (VatGlass?.IsPouring != true) { _faucetChargeBeat?.Stop(); UpdateFaucetPresentation(); }
            return;
        }
        double t = Math.Clamp(ms / _faucetBudget, 0, 1);
        DrawFaucetRing(t);
        int want = Math.Min(6, (int)(t * 6) + 1);
        while (_faucetRungs < want) PlayFaucetSfx($"faucet_charge_{++_faucetRungs}.wav", .15f);
        if (t < 1) return;
        CancelFaucetCharge();
        var glass = VatGlass;
        if (!_vatArmed || glass == null || _faucetHold.HeldXp <= 0) return;
        bool wasOver = glass.IsPastBrim(glass.Fill);
        var step = _faucetHold.PourAll();
        StartFaucetFeedback(true);
        PlayFaucetSfx("faucet_pour.wav", .35f);
        glass.PourTo(step.Fill, userGesture: true); FireFaucetPourHaptic();
        if (!wasOver && glass.IsPastBrim(step.Fill)) { glass.PulseOverflow(); PlayFaucetSfx("faucet_brim.wav", .30f); }
        _faucetPours++;
        if (glass.IsPouring) _faucetChargeBeat?.Start();
        UpdateFaucetPresentation();
    }
    // WPF MainWindow.ProfileFaucet.cs:694-717. Cancellation never stamps the ledger.
    private void CancelFaucetCharge(bool feedback = false)
    {
        bool charging = _faucetCharging;
        _faucetCharging = false;
        _faucetChargeBeat?.Stop();
        if (FaucetControl<Grid>("ProfileVatChargeRing") is { } ring) { ring.IsVisible = false; ring.Opacity = 0; }
        if (!feedback && (charging || !_vatOnScreen || !_vatArmed || !IsActive || !IsVisible)) StopFaucetFeedback();
        if (charging && feedback) { StartFaucetFeedback(false); PlayFaucetSfx("faucet_charge_drop.wav", .15f); }
        UpdateFaucetPresentation();
    }
    private void DrawFaucetRing(double t) // WPF MainWindow.ProfileFaucet.cs:779-860. Reduced motion uses a whole still circle.
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
    private static void PlayFaucetSfx(string file, float scale) // WPF MainWindow.ProfileFaucet.cs:1112-1124. The existing audio seam owns device lifetime.
    {
        float volume = Math.Clamp((float)CoreSettings.Current.MasterVolume / 100 * scale, 0, 1);
        if (volume > 0) CoreAudio.PlayOneShot(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", file), volume, "vat-faucet");
    }
}
