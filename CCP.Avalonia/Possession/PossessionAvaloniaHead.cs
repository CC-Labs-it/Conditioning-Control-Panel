// PORTED from ConditioningControlPanel/Services/Possession/PossessionWpfHead.cs (the head's half of the
// Core PossessionDirector + PossessionRemember) and App.xaml.cs:3506-3515 (the director, its audio and
// the remembered charge, built once next to the LockdownService), plus the effect contract bridge that
// lets the Core director drive this head's frame-stepped effects (WPF PossessionEffectBase.ApplyAsync /
// UndoAsync: charge, name, possess, apply; undo eases home).
//
// What the director reads from App.*, the dispatcher and the visual tree comes from here, unchanged in
// meaning: OnUi = the Avalonia UI thread (inline when already there), the main host = the shell window,
// attribution = this head's EmberAttribution, barks = BarkHead's Core engine with WPF BarkService's
// context fields (BarkService.cs:398-416).
//
// Not on this head yet (honest no-ops, recorded in docs/avalonia-parity.html shell-possession):
// the scenes (Scenes = none), the reactive adapter (WPF PossessionEvents), the warden (Warden stays
// null, so every warden verb is skipped as WPF does when the tube is unavailable).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession
{
    /// <summary>What an Avalonia window adds to <see cref="IPossessionHost"/> (WPF IWpfPossessionHost):
    /// the window, the ghost layer, the frame source effects and overlays step on, and the coordinate
    /// reads.</summary>
    internal interface IAvaloniaPossessionHost : IPossessionHost
    {
        Window? PossessionWindow { get; }
        /// <summary>Transparent, non-hit-testable Canvas over the whole window, above everything.</summary>
        Canvas? GhostLayer { get; }
        IFrameSource PossessionFrames { get; }
        /// <summary>The runtime the bridged effects lease controls through (one per window).</summary>
        PossessionRuntime PossessionEffectRuntime { get; }
        /// <summary>Last pointer position in ghost-layer coordinates; (0,0) until the pointer has moved.</summary>
        Point PointerPosition { get; }
        PossessionPointer PossessionPointer { get; }
        /// <summary>A control's bounds in ghost-layer coordinates, render transform included; null when it has none.</summary>
        Rect? BoundsOnGhostLayer(Control element);
    }

    /// <summary>
    /// One of this head's frame-stepped effects behind the Core <see cref="IPossessionEffect"/> contract, so
    /// the Core <see cref="PossessionDirector"/> picks, books, holds and undoes it exactly as WPF does. The
    /// grammar is WPF PossessionEffectBase's: ember charge BEFORE anything moves, then the name (big effects
    /// only), then the possessed outline for as long as the control stays moved.
    /// </summary>
    internal sealed class DirectedHeadEffect : IPossessionEffect
    {
        private readonly HeadEffect _inner;
        private PossessionRuntime? _runtime;
        private IDisposable? _possessed;
        private CancellationTokenSource? _cts;
        private bool _applying;

        public DirectedHeadEffect(HeadEffect inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        internal HeadEffect Inner => _inner;
        public string Id => _inner.Id;
        public PossessionRung MinRung => _inner.MinRung;
        public PossessionIntensity MinIntensity => _inner.MinIntensity;
        /// <summary>Nudge, drift, breathe and wobble are micro-tics on WPF (IsBig false): charged and
        /// tinted, never named by the warden.</summary>
        public bool IsBig => false;
        public bool UsesFlicker => _inner.UsesFlicker;
        public double Weight => _inner.Weight;
        public TimeSpan HoldFor => _inner.HoldFor;
        public IReadOnlyList<PossessionRole> Roles => _inner.Roles;
        public bool IsLive => _applying || _runtime?.IsRunning(_inner) == true;

        /// <summary>WPF PossessionEffectBase.CanApply: a target of a fitting role that is visible, has a size,
        /// is not off limits (PossessionTree) and is not already leased; never while this effect is live.</summary>
        public bool CanApply(PossessionContext ctx, PossessionTarget? target)
        {
            try
            {
                if (IsLive || ctx?.Host is not IAvaloniaPossessionHost) return false;
                if (target?.Visual is not Control c) return false;
                if (!Contains(Roles, target.Role)) return false;
                if (!c.IsEffectivelyVisible || c.Bounds.Width <= 0 || c.Bounds.Height <= 0) return false;
                if (PossessionTree.IsOffLimits(c, _inner is WobbleHeadEffect && target.Role == PossessionRole.Timer && c is TextBlock && c.Name == "TxtLockdownTimer") || PossessionLease.IsLeased(c)) return false;
                if (ctx.Photosafe && UsesFlicker) return false;
                // The effect's own gate (text effects: an unbound, plain TextBlock with something to change).
                return _inner.CanApply(c);
            }
            catch { return false; }
        }

        public async Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            if (IsLive || ctx.Host is not IAvaloniaPossessionHost host || target?.Visual is not Control c) return;
            _applying = true;
            try
            {
                _cts?.Dispose();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var token = _cts.Token;
                try { await ctx.Attribution.ChargeAsync(c, token).ConfigureAwait(true); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Serilog.Log.Warning("Possession {Id} charge failed: {Error}", Id, ex.Message); }
                if (token.IsCancellationRequested) return;

                var runtime = _runtime = host.PossessionEffectRuntime;
                runtime.Photosafe = ctx.Photosafe;
                if (!runtime.TryApply(_inner, new PossessionHeadTarget(c, target.Role, target.Key), directed: true)) return;
                try { _possessed = ctx.Attribution.Possess(c); }
                catch (Exception ex) { Serilog.Log.Warning("Possession {Id} possess failed: {Error}", Id, ex.Message); }
            }
            finally { _applying = false; }
        }

        /// <summary>Safe when Apply never ran, was cancelled half-way, or already undid itself.</summary>
        public Task UndoAsync(TimeSpan duration)
        {
            try { _cts?.Cancel(); } catch { }
            try { _possessed?.Dispose(); } catch { }
            _possessed = null;
            var runtime = _runtime;
            if (runtime == null) return Task.CompletedTask;
            try { return runtime.Return(_inner, duration); }
            catch (Exception ex)
            {
                Serilog.Log.Warning("Possession {Id} undo failed: {Error}", Id, ex.Message);
                return Task.CompletedTask;
            }
        }

        private static bool Contains(IReadOnlyList<PossessionRole> roles, PossessionRole role)
        {
            for (int i = 0; i < roles.Count; i++) if (roles[i] == role) return true;
            return false;
        }
    }

    /// <summary>The Avalonia head's half of the Possession engine (WPF PossessionWpfHead).</summary>
    internal static class PossessionAvaloniaHead
    {
        /// <summary>The director's effect catalog on this head (WPF PossessionEffectCatalog.CreateAll):
        /// every ported effect, bridged.</summary>
        internal static List<IPossessionEffect> CreateEffects()
        {
            var list = new List<IPossessionEffect>();
            foreach (var e in PossessionRuntime.CreateAll()) list.Add(new DirectedHeadEffect(e));
            list.Add(new RetitleEffect());
            list.Add(new DokiDialogEffect());
            list.Add(new DeleteDialogEffect());
            list.Add(new DodgeEffect());
            list.Add(new SliderCreepEffect());
            list.Add(new ScrollHijackEffect());
            return list;
        }

        /// <summary>True when the catalog carries a Full-Doki-only effect (the themed fake dialogs).
        /// The consent text names those scares only when this head can actually show one.</summary>
        internal static bool HasFullDokiEffects()
        {
            foreach (var e in CreateEffects())
                if (e.Id is "dokidialog" or "deletedialog") return true;
            return false;
        }

        internal static PossessionDirectorHead CreateDirectorHead() => new()
        {
            OnUi = OnUi,
            UiAlive = () => Application.Current != null && !_shutdownStarted,
            Settings = () => CoreSettings.Service?.Current,
            MainHost = () => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as IPossessionHost,
            Attribution = AttributionFor,
            Pointer = () => LastPointer(),
            BoundsOf = (host, el) => host is IAvaloniaPossessionHost h && el is Control c && h.BoundsOnGhostLayer(c) is { } r
                ? (r.X, r.Y, r.Width, r.Height)
                : null,
            IsVisible = el => el is Control c && c.IsEffectivelyVisible,
            ParentOf = node => node switch
            {
                Visual v when v.GetVisualParent() is { } p => p,
                ILogical l => l.LogicalParent,
                _ => null,
            },
            BarkRung = rung => Raise(PossessionBarkTriggers.RungChanged, c => c.Set("rung", (double)rung)),
            BarkTimerRestarted = (reason, restart) => Raise(PossessionBarkTriggers.TimerRestarted,
                c => { c.Set("reason", reason ?? ""); c.Set("restart", (double)restart); }),
            // WPF BarkService.cs:403: an absent display name must not leave a hole in the sentence.
            BarkEffect = (id, target) => Raise(PossessionBarkTriggers.Effect,
                c => { c.Set("effect", id); c.Set("target", string.IsNullOrWhiteSpace(target) ? "that one" : target!); }),
            BarkTripwire = (kind, repeat, total) => Raise(PossessionBarkTriggers.Tripwire,
                c => { c.Set("kind", kind); c.Set("repeat", (double)repeat); c.Set("total", (double)total); }),
            // WPF App.ScreenShake (ScreenShakeService) is this head's Deeper ScreenShake: every visible app window's
            // content jitters, panic row "screen-shake" stops it. The director only asks when not photosafe.
            Shake = (intensity, ms) => Views.Deeper.ScreenShake.Shake(intensity, ms),
        };

        internal static IPossessionAttribution AttributionFor(IPossessionHost host) =>
            host is IAvaloniaPossessionHost h
                ? new EmberAttribution(h, () => CoreSettings.Service?.Current?.LockdownPhotosafe == true)
                : NullAttribution.Instance;

        /// <summary>WPF App.xaml.cs:3506-3515 + 2975: the director over this lockdown, the ember tick / dip
        /// cues, the remembered charge and the companion prompt's possession state.</summary>
        internal static PossessionDirector Install(LockdownService lockdown)
        {
            HookShutdown();
            var director = new PossessionDirector(lockdown, CreateEffects(), CreateDirectorHead());
            PossessionAudio.Install(lockdown, director);
            InstallRemember(lockdown);
            global::ConditioningControlPanel.Services.Companion.Brain.PromptAssembler.PossessionState = () =>
                (director.IsHaunting ? (int)director.CurrentRung : null, PossessionRemember.EscapeAttempts);
            return director;
        }

        /// <summary>WPF PossessionWpfHead.InstallRemember: seed the head hooks, then install on the lockdown.</summary>
        internal static void InstallRemember(LockdownService lockdown)
        {
            PossessionRemember.Settings = () => CoreSettings.Service?.Current;
            PossessionRemember.StartTimer = (interval, tick) =>
            {
                var timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => tick());
                timer.Start();
                return new Stopper(timer);
            };
            PossessionRemember.WindowReady = () => Shell() is { IsVisible: true } s && s.IsLoaded;
            PossessionRemember.HasCompanion = () => Views.AvatarTube.AvatarTubeWindow.Live != null;
            PossessionRemember.ChargeDoor = ChargeDoor;
            PossessionRemember.SayRemembered = SayRemembered;
            PossessionRemember.Install(lockdown);
        }

        private sealed class Stopper(DispatcherTimer timer) : IDisposable
        {
            public void Dispose() => timer.Stop();
        }

        private static Views.Windows.MainShellWindow? Shell() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as Views.Windows.MainShellWindow;

        /// <summary>WPF ChargeDoor: one ember ripple (600 ms) on the most Lockdown-ish thing on screen - the
        /// rail's Lockdown entry when its door is open, otherwise the Play door holding it.</summary>
        private static bool ChargeDoor()
        {
            if (Shell() is not { } shell) return false;
            Control? door = null;
            foreach (var name in new[] { "BtnNavLockdown", "DoorPlay" })
            {
                if (shell.Named<Control>(name) is { IsEffectivelyVisible: true } c && c.Bounds.Width > 1 && c.Bounds.Height > 1)
                {
                    door = c;
                    break;
                }
            }
            if (door == null) return false;
            Serilog.Log.Information("Possession: spending the remembered charge on {Door}", door.Name);
            _ = AttributionFor(shell).ChargeAsync(door, CancellationToken.None, 600);
            return true;
        }

        /// <summary>WPF SayRemembered: the packs own the words (rule poss_remember); the bubble fallback only
        /// covers a mod pack without that rule.</summary>
        private static void SayRemembered()
        {
            try
            {
                if (Platform.BarkHead.Engine?.Raise(PossessionBarkTriggers.Remember, guaranteed: true) == true) return;
                Views.AvatarTube.AvatarTubeWindow.Live?.GigglePriority("i remember.", playSound: false,
                    aiGenerated: false, mood: "possessive");
            }
            catch (Exception ex) { Serilog.Log.Debug("PossessionRemember line failed: {Error}", ex.Message); }
        }

        /// <summary>The first-run rules bark (WPF BarkService.NotifyPossessionRules, guaranteed).</summary>
        internal static bool BarkRules() => Platform.BarkHead.Engine?.Raise(PossessionBarkTriggers.Rules, guaranteed: true) == true;

        private static bool Raise(string trigger, Action<global::ConditioningControlPanel.Services.Bark.BarkContext> fill)
        {
            try { return Platform.BarkHead.Engine?.Raise(trigger, fill) == true; }
            catch (Exception ex) { Serilog.Log.Debug("Possession bark {Trigger} failed: {Error}", trigger, ex.Message); return false; }
        }

        private static (double X, double Y) LastPointer()
        {
            if (Shell() is IAvaloniaPossessionHost h)
            {
                var p = h.PointerPosition;
                return (p.X, p.Y);
            }
            return (0, 0);
        }

        private static void OnUi(Action a)
        {
            if (Dispatcher.UIThread.CheckAccess()) a();
            else Dispatcher.UIThread.Post(a);
        }

        private static volatile bool _shutdownStarted;
        private static int _shutdownHooked;

        private static void HookShutdown()
        {
            if (Interlocked.Exchange(ref _shutdownHooked, 1) == 1) return;
            try { Dispatcher.UIThread.ShutdownStarted += (_, _) => _shutdownStarted = true; }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Possession: could not watch dispatcher shutdown"); }
        }

        private sealed class NullAttribution : IPossessionAttribution
        {
            public static readonly NullAttribution Instance = new();
            public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
            public IDisposable Possess(object target) => new CancellationTokenSource();
            public void EdgePulse(double strength) { }
            public bool AnyPossessed => false;
            public void ReleaseAll() { }
        }
    }
}
