// Port of ConditioningControlPanel/MainWindow/MainWindow.DescentFuse.cs: the header spark, its
// hover readout and the corner clock, driven by Core DescentCountdownService (App.DescentCountdown).
// Plus the live zero opener of WPF Services/Descent/DescentShowDirector.cs:130-170.
//
// ponytail: zero opens only the Live show; the catch-up crack, the post-zero retry and the
// ceremony focus/hand-off (DescentShowDirector, DescentMigrationService) remain WPF-only.
using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private static readonly IBrush FuseGoldBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x52));
        private static readonly IBrush FuseNeutralDigits = new SolidColorBrush(Color.FromRgb(0xC9, 0xC4, 0xD6));

        private int _fuseLastDimStep;
        private bool _fuseClosed;
        private TextBlock? _fuseTooltipDigits;
        private global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop? _fuseBreath;

        /// <summary>WPF InitializeDescentFuse: two subscriptions and one catch-up paint.</summary>
        internal void InitializeDescentFuse()
        {
            try
            {
                var fuse = App.DescentCountdown;
                if (fuse is null) return;
                void PhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e) { if (!_fuseClosed) ApplyFusePhase(e.Current); }
                void Tick(object? sender, TimeSpan remaining) { if (!_fuseClosed) OnFuseTick(remaining); }
                void Zero(object? sender, EventArgs e) { if (!_fuseClosed) _ = OpenLiveFuseShow(); }
                var settings = CoreSettings.Current;
                void SettingsChanged(object? sender, PropertyChangedEventArgs e)
                {
                    if (e.PropertyName != nameof(settings.DescentMigrationCompleted)) return;
                    // An ack can arrive after zero has disarmed the countdown's timer.
                    if (Dispatcher.UIThread.CheckAccess()) RefreshFuseChrome();
                    else Dispatcher.UIThread.Post(RefreshFuseChrome);
                }
                fuse.PhaseChanged += PhaseChanged;
                fuse.Tick += Tick;
                fuse.ZeroReached += Zero;
                settings.PropertyChanged += SettingsChanged;
                Closed += (_, _) =>
                {
                    _fuseClosed = true;
                    fuse.PhaseChanged -= PhaseChanged;
                    fuse.Tick -= Tick;
                    fuse.ZeroReached -= Zero;
                    settings.PropertyChanged -= SettingsChanged;
                    _fuseBreath?.Stop();
                };
                // AttachModSwitch already painted the live step; avoid a second palette rewrite.
                _fuseLastDimStep = fuse.DimStep;
                ApplyFusePhase(fuse.LastAnnouncedPhase);
            }
            catch (Exception ex) { Log.Debug("[Fuse] Header surfaces could not be wired: {E}", ex.Message); }
        }

        private void RefreshFuseChrome()
        {
            if (_fuseClosed) return;
            var step = App.DescentCountdown?.DimStep ?? 0;
            if (step == _fuseLastDimStep) return;
            _fuseLastDimStep = step;
            RefreshThemeAwareElements();
        }

        private void OnFuseTick(TimeSpan remaining)
        {
            var text = DescentFuseCopy.TMinus(remaining);
            if (_fuseTooltipDigits != null) _fuseTooltipDigits.Text = text;
            if (Named<Border>("FuseCornerReadout") is { IsVisible: true })
            {
                if (Named<TextBlock>("FuseCornerDigits") is { } digits) digits.Text = text;
                ApplyFusePresence();
            }
            RefreshFuseChrome();
        }

        /// <summary>WPF ApplyFusePhase: the whole surface state for a phase, from scratch.</summary>
        internal void ApplyFusePhase(DescentFusePhase phase)
        {
            if (Named<Grid>("FuseSparkHost") is { } spark)
            {
                var show = phase >= DescentFusePhase.Whisper && phase < DescentFusePhase.Zero;
                spark.IsVisible = show;
                var glyph = Named<Control>("FuseSparkGlyph");
                if (show && _fuseBreath is null && glyph != null)
                {
                    // WPF MotionFx.GlowBreath(glyph, 0.45, 1.0, 3.8) (MotionFx.cs:230-246: AutoReverse,
                    // SineEase InOut, 30 fps) on the beat (P82); reduced motion parks it lit.
                    _fuseBreath = new global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop(glyph, t =>
                        glyph.Opacity = 0.45 + 0.55 * global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Breath(t, 3.8));
                    if (ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowAmbientLoops) _fuseBreath.Start();
                }
                else if (!show && _fuseBreath != null)
                {
                    _fuseBreath.Stop();
                    _fuseBreath = null;
                    if (glyph != null) glyph.Opacity = 1.0;
                }
                // The last-hour size bump, snapped (WPF FuseSparkScale).
                if (spark.RenderTransform is ScaleTransform scale)
                    scale.ScaleX = scale.ScaleY = show && phase >= DescentFusePhase.Vigil ? 1.25 : 1.0;

                // The hover readout arrives at Clock and not before.
                if (phase >= DescentFusePhase.Clock && phase < DescentFusePhase.Zero)
                {
                    var remaining = DescentFuseCopy.TMinus(App.DescentCountdown?.Remaining ?? TimeSpan.Zero);
                    // Built once and kept (WPF BuildFuseTooltip), so ticks retype it in place.
                    if (_fuseTooltipDigits != null) _fuseTooltipDigits.Text = remaining;
                    else ToolTip.SetTip(spark, NewFuseTip(_fuseTooltipDigits = new TextBlock
                    {
                        Text = remaining,
                        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                        FontSize = 14,
                        Foreground = FuseNeutralDigits,
                    }));
                }
                else
                {
                    ToolTip.SetTip(spark, null);
                    _fuseTooltipDigits = null;
                }
            }

            if (Named<Border>("FuseCornerReadout") is { } corner)
            {
                var show = phase >= DescentFusePhase.Vigil && phase < DescentFusePhase.Zero;
                corner.IsVisible = show;
                if (show && Named<TextBlock>("FuseCornerDigits") is { } digits)
                {
                    // Terminal turns the digits gold.
                    digits.Foreground = phase >= DescentFusePhase.Terminal ? FuseGoldBrush : FuseNeutralDigits;
                    digits.Text = DescentFuseCopy.TMinus(App.DescentCountdown?.Remaining ?? TimeSpan.Zero);
                    ApplyFusePresence();
                }
            }
            RefreshFuseChrome();
        }

        private static Border NewFuseTip(TextBlock digits) => new()
        {
            Child = digits,
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x05, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xE0, 0xB0, 0x52)),
            BorderThickness = new global::Avalonia.Thickness(1),
            Padding = new global::Avalonia.Thickness(9, 4, 9, 4),
        };

        /// <summary>WPF ApplyFusePresence: only when the server actually said N (0 is a reading).</summary>
        private void ApplyFusePresence()
        {
            if (Named<TextBlock>("FuseCornerPresence") is not { } line) return;
            var count = App.DescentCountdown?.VigilCount;
            line.IsVisible = count is >= 0;
            if (count is >= 0) line.Text = DescentFuseCopy.Presence(count.Value);
        }

        /// <summary>WPF DescentShowDirector live zero: nothing to reveal to an answered account.</summary>
        internal static DescentFuseWindow? OpenLiveFuseShow()
        {
            var s = CoreSettings.Current;
            if (s.DescentMigrationCompleted || DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice))
            {
                Log.Information("[Fuse] Zero, but the question is already answered - no live show.");
                return null;
            }
            return DescentFuseWindow.Open(DescentShowKind.Live);
        }
    }
}
