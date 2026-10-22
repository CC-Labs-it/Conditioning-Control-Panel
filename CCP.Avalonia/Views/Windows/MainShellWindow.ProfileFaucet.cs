// Charge-hold slice adapted from WPF MainWindow.ProfileFaucet.cs and parity/trunk a4ee52d40.
// Server polling is follow-up lane wire-shell-profile-vat-b; the motion is MainShellWindow.ProfileFaucetFx.cs.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {

        private readonly VatFaucetHold _faucetHold = new(new AppSettingsVatPourLedger());
        private readonly VatFaucetChargePolicy _faucetChargePolicy = new();
        private BeatLoop? _faucetChargeTimer;
        private DateTime _faucetChargeStart;
        private double _faucetChargeBudgetMs;
        private int _faucetChargeRungsPlayed;
        private bool _faucetCharging, _faucetChargeCompact, _faucetKeyHeld, _faucetWired;
        private IPointer? _faucetPointer;
        private T? Vat<T>(string name) where T : Control => ProfilePage?.FindControl<T>(name);
        internal int FaucetHeldXp => _faucetHold.HeldXp;
        internal bool FaucetCharging => _faucetCharging;

        // ApplyDescentToVat (WPF MainWindow.ProfileVat.cs:267-345), which folds each accepted
        // server read into this hold, is owned by follow-up lane wire-shell-profile-vat-b.
        internal VatFaucetHold FaucetHold => _faucetHold;

        private void ArmFaucet(VatGlassCanvas glass, double jarW, double jarH)
        {
            var faucet = Vat<Grid>("ProfileVatFaucet");
            if (faucet == null) return;
            WireFaucet(faucet);
            double left = Math.Round(jarW * .10 - 8), top = Math.Round(jarH * .175 - 36);
            faucet.Margin = new Thickness(left, top, 0, 0);
            faucet.IsVisible = true;
            if (Vat<Grid>("ProfileVatChargeRing") is { } ring)
                ring.Margin = new Thickness(left + 15 - 17, top + 18 - 17, 0, 0);
            if (Vat<Canvas>("ProfileVatSparks") is { } sparks) { sparks.Width = jarW; sparks.Height = jarH; }
            if (Vat<Border>("ProfileVatChip") is { } chip) chip.Margin = new Thickness(0, Math.Round(jarH * .175) + 8, 0, 0);
            glass.ExternalSpoutXFraction = (left + 27) / jarW;
            PositionVatTickGlyphs();
            UpdateFaucetPresentation();
        }

        private void PositionVatTickGlyphs()
        {
            if (VatGlass is not { } glass) return;
            Place("ProfileVatTickDrain", VatGlassCanvas.VatTickMark.Drain);
            Place("ProfileVatTickCap", VatGlassCanvas.VatTickMark.Cap);
            Place("ProfileVatTickMax", VatGlassCanvas.VatTickMark.Max);
            void Place(string name, VatGlassCanvas.VatTickMark mark)
            {
                if (Vat<IconGlyph>(name) is not { } glyph) return;
                var y = glass.TickCenterY(mark);
                glyph.IsVisible = y != null && glass.TickInnerX > 0;
                if (glyph.IsVisible)
                    glyph.Margin = new Thickness(Math.Round(glass.TickInnerX - 3 - glyph.Size), Math.Round(y!.Value - glyph.Size / 2), 0, 0);
            }
        }

        private void DisarmFaucet()
        {
            CancelFaucetCharge(silent: true);
            _faucetHold.Reset();
            StopFaucetFx();
            foreach (string name in new[] { "ProfileVatFaucet", "ProfileVatChip", "ProfileVatChargeRing", "ProfileVatTickDrain", "ProfileVatTickCap", "ProfileVatTickMax" })
                if (Vat<Control>(name) is { } control) control.IsVisible = false;
            Vat<Canvas>("ProfileVatSparks")?.Children.Clear();
            if (VatGlass is { } glass) glass.ExternalSpoutXFraction = null;
        }

        private void WireFaucet(Grid faucet)
        {
            if (_faucetWired) return;
            _faucetWired = true;
            faucet.RenderTransform = new TransformGroup { Children = { FaucetShake, FaucetScale, FaucetTilt } };
            if (Vat<Grid>("ProfileVatHandle") is { } handle) handle.RenderTransform = FaucetHandleScale;
            if (Vat<Control>("ProfileVatFaucetDrop") is { } drop) drop.RenderTransform = FaucetDropFall;
            if (Vat<Border>("ProfileVatChip") is { } chip) chip.RenderTransform = FaucetChipScale;
            faucet.PointerEntered += (_, _) => { if (!_faucetCharging) AnimateFaucetScale(1.16); };
            faucet.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(faucet).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                faucet.Focus();
                _faucetPointer = e.Pointer;
                e.Pointer.Capture(faucet);
                BeginFaucetCharge();
            };
            faucet.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                e.Handled = true;
                CancelFaucetCharge(silent: false);
                e.Pointer.Capture(null);
            };
            // Sliding off mid-charge is an early release: the hold is kept and the tap shivers.
            faucet.PointerExited += (_, _) => { if (_faucetCharging) CancelFaucetCharge(silent: false); else AnimateFaucetScale(1); };
            faucet.PointerCaptureLost += (_, _) => CancelFaucetCharge(silent: true);
            faucet.LostFocus += (_, _) => { _faucetKeyHeld = false; CancelFaucetCharge(silent: true); };
            faucet.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Space && e.Key != Key.Enter) return;
                e.Handled = true;
                if (_faucetKeyHeld) return;
                _faucetKeyHeld = true;
                BeginFaucetCharge();
            };
            faucet.KeyUp += (_, e) =>
            {
                if (e.Key != Key.Space && e.Key != Key.Enter) return;
                e.Handled = true;
                _faucetKeyHeld = false;
                CancelFaucetCharge(silent: false);
            };
            if (ProfilePage is { } page)
                page.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !page.IsVisible) OnFaucetVatOffScreen(); };
            if (VatGlass is { } glass)
            {
                glass.FillPercentChanged += OnVatFillPercentChanged;
                glass.SizeChanged += (_, _) => PositionVatTickGlyphs();
            }
            Deactivated += (_, _) => OnFaucetVatOffScreen();
            Closed += (_, _) => CancelFaucetCharge(silent: true);
        }

        private void OnFaucetVatOffScreen()
        {
            _faucetKeyHeld = false;
            CancelFaucetCharge(silent: true);
        }

        private void UpdateFaucetPresentation()
        {
            var tap = Vat<Grid>("ProfileVatFaucet");
            if (tap == null || !tap.IsVisible) return;
            int held = _faucetHold.HeldXp;
            bool waiting = held > 0 && VatGlass?.IsPouring != true && !_faucetCharging;
            var tip = new StackPanel { MaxWidth = 320 };
            tip.Children.Add(new TextBlock { Text = held > 0 ? Loc.GetF("profile_faucet_tip_held", held) : Loc.Get("profile_faucet_tip_empty"), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
            tip.Children.Add(new TextBlock { Text = Loc.Get("profile_vat_help_tip"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = .72, Margin = new Thickness(0, 5, 0, 0) });
            ToolTip.SetTip(tap, tip);
            if (Vat<Border>("ProfileVatChip") is { } chip) chip.IsVisible = waiting;
            if (Vat<TextBlock>("ProfileVatChipText") is { } text) text.Text = Loc.GetF("profile_vat_chip", held);
            if (Vat<Control>("ProfileVatFaucetBadge") is { } badge) badge.IsVisible = held > 0 && !AmbientFxCanvas.Env.AllowAmbientLoops;
            // The wobble and the chip's breath (its sparkle and tick ride the wobble) are ambient loops;
            // a hidden tab or minimised window parks their BeatLoop the way WPF's _vatOnScreen stops them.
            SetFaucetAmbient(waiting && AmbientFxCanvas.Env.AllowAmbientLoops);
        }

        internal void BeginFaucetCharge()
        {
            if (_faucetCharging || !_vatArmed || VatGlass?.IsPouring != false || _faucetHold.HeldXp <= 0) return;
            // WPF wraps begin/tick/complete so a failing sound or haptic cancels the hold instead
            // of reaching the dispatcher (MainWindow.ProfileFaucet.cs:624-628, 660-664).
            try { StartFaucetCharge(); }
            catch (Exception ex) { Serilog.Log.Debug("BeginFaucetCharge: {E}", ex.Message); CancelFaucetCharge(silent: true); }
        }

        private void StartFaucetCharge()
        {
            _faucetCharging = true;
            _faucetChargeRungsPlayed = 1;
            _faucetChargeCompact = _faucetChargePolicy.IsCompact(DateTime.UtcNow);
            _faucetChargeBudgetMs = VatFaucetChargePolicy.BudgetMilliseconds(_faucetHold.HeldXp, _faucetChargeCompact);
            _faucetChargeStart = DateTime.UtcNow;
            AnimateFaucetScale(.94, 90); // THE BOUNCE, on the way down
            // The ring fades in over 120 ms on the charge beat (WPF ShowChargeRing), at once without transitions.
            bool fade = AmbientFxCanvas.Env.AllowTransitions;
            if (Vat<Grid>("ProfileVatChargeRing") is { } ring) { ring.IsVisible = true; ring.Opacity = fade ? 0 : 1; }
            UpdateChargeRing(AmbientFxCanvas.Env.Level == MotionLevel.Full ? 0 : 1, force: true);
            PlayFaucetSfx("faucet_charge_1.wav", .15f);
            UpdateFaucetPresentation();
            // Rides the window's shared 30 fps beat (AmbientBeatLoopsTests), not its own frame timer.
            if (_faucetChargeTimer == null && Vat<Grid>("ProfileVatChargeRing") is { } owner)
                _faucetChargeTimer = new BeatLoop(owner, t =>
                {
                    if (AmbientFxCanvas.Env.AllowTransitions) owner.Opacity = Math.Clamp(t / .12, 0, 1);
                    if (t > 0) AdvanceFaucetCharge((DateTime.UtcNow - _faucetChargeStart).TotalMilliseconds / _faucetChargeBudgetMs);
                });
            _faucetChargeTimer?.Start();
        }

        internal void AdvanceFaucetCharge(double progress)
        {
            try { StepFaucetCharge(progress); }
            catch (Exception ex) { Serilog.Log.Debug("Faucet charge: {E}", ex.Message); CancelFaucetCharge(silent: true); }
        }

        private void StepFaucetCharge(double progress)
        {
            if (!_faucetCharging || !double.IsFinite(progress)) return;
            progress = Math.Clamp(progress, 0, 1);
            UpdateChargeRing(progress);
            int want = VatFaucetChargePolicy.RungsAt(progress);
            while (_faucetChargeRungsPlayed < want)
                PlayFaucetSfx($"faucet_charge_{++_faucetChargeRungsPlayed}.wav", .15f);
            if (progress < 1) return;
            _faucetCharging = false;
            HideChargeRing();
            if (!_vatArmed || VatGlass is not { } glass || _faucetHold.HeldXp <= 0) { AnimateFaucetScale(1); UpdateFaucetPresentation(); return; }
            bool wasOver = glass.IsPastBrim(glass.Fill);
            var step = _faucetHold.PourAll();
            AnimateFaucetScale(1, 120);
            FaucetThud();
            PlayFaucetSfx("faucet_pour.wav", .35f); // SAME FRAME as the thud
            AnimateFaucetTilt(14);
            glass.PourTo(step.Fill, userGesture: true);
            var rule = CoreSettings.Current.Haptics?.V2?.Rule(HapticEventKind.LevelUp);
            if (rule is { Enabled: true, Intensity: > 0 } && CoreHaptics.Service is { } haptics)
                _ = haptics.PostEvent(HapticEventKind.LevelUp, Math.Clamp(rule.Intensity * .35, 0, 1));
            if (!wasOver && glass.IsPastBrim(step.Fill)) { glass.PulseOverflow(); PlayFaucetSfx("faucet_brim.wav", .30f); }
            if (!_faucetChargeCompact) SpawnFaucetSparkles();
            _faucetChargePolicy.NotePour(DateTime.UtcNow);
            StartFaucetSettleWatch();
            UpdateFaucetPresentation();
        }

        internal void CancelFaucetCharge(bool silent)
        {
            bool wasCharging = _faucetCharging;
            _faucetCharging = false;
            HideChargeRing();
            var pointer = _faucetPointer;
            _faucetPointer = null;
            pointer?.Capture(null);
            if (wasCharging) AnimateFaucetScale(1);
            if (wasCharging && !silent) { FaucetShiverAndDrip(); PlayFaucetSfx("faucet_charge_drop.wav", .15f); }
            UpdateFaucetPresentation();
        }

        private void HideChargeRing()
        {
            _faucetChargeTimer?.Stop();
            if (Vat<Grid>("ProfileVatChargeRing") is { } ring) { ring.IsVisible = false; ring.Opacity = 0; }
            if (Vat<Path>("ProfileVatChargeArc") is { } arc) arc.Data = null;
        }

        private void UpdateChargeRing(double progress, bool force = false)
        {
            if (!force && AmbientFxCanvas.Env.Level != MotionLevel.Full) return;
            if (Vat<Path>("ProfileVatChargeArc") is not { } arc) return;
            if (progress <= .002) { arc.Data = null; return; }
            if (progress >= .999) { arc.Data = new EllipseGeometry(new Rect(2, 2, 30, 30)); return; }
            double angle = progress * 2 * Math.PI;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(17, 2), false);
                ctx.ArcTo(new Point(17 + 15 * Math.Sin(angle), 17 - 15 * Math.Cos(angle)), new Size(15, 15), 0, progress > .5, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            arc.Data = geometry;
        }

        private static void PlayFaucetSfx(string file, float scale)
        {
            float volume = Math.Clamp((float)CoreSettings.Current.MasterVolume / 100 * scale, 0, 1);
            if (volume > 0) CoreAudio.PlayOneShot(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", file), volume, "vat-faucet");
        }
    }
}
