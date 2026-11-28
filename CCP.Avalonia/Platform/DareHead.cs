using System;
using System.IO;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Webcam;
using ConditioningControlPanel.Avalonia.Views.Overlays;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF DareHost and DareMeterHooks on the Avalonia dispatcher.</summary>
internal static class DareHead
{
    internal static DaresService? Service { get; set; }
    /// <summary>WPF App._daresPreview: the DEBUG local preview, in place of <see cref="Service"/>.</summary>
    internal static PreviewDaresService? Preview { get; set; }
    private static MeterHooks? _hooks;
    private static Host? _host;

    /// <summary>WPF App.xaml.cs:3640-3663: the service (or the preview), then the meter hooks.</summary>
    internal static void Start(bool preview = false)
    {
        var url = FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"),
            Environment.GetEnvironmentVariable(FriendsHead.EnvVar));
        if ((url == null && !preview) || Service != null || Preview != null) return;
        var host = new Host();
        _host = host;
        if (preview)
        {
            var p = PreviewDaresService.Create(CorePaths.UserData, host, () => FriendsHead.Service, out var previewMeter);
            var hooks = _hooks = new MeterHooks(previewMeter);
            Preview = p;
            p.Changed += hooks.Wake;
            p.CameraProbe = () => hooks.Camera;
            p.Start(new FriendsHead.Timer());
        }
        else
        {
            var meter = new DareMeter(new DareMeter.FileStore(Path.Combine(CorePaths.UserData, "dares_meter.json")));
            var keeper = new DareLockdownKeeper(host, new DareLockdownKeeper.FileStore(Path.Combine(CorePaths.UserData, "dares_lockdown_serve.json")));
            var service = new DaresService(new DaresApi(identity: FriendsHead.Identity, baseUrl: url!), host, meter, keeper);
            var hooks = _hooks = new MeterHooks(meter);
            Service = service;
            service.BetsRead += hooks.Wake;
            service.CameraProbe = () => hooks.Camera;
            AccountSeed.ProfileLoaded += Kick;
            service.Start(new FriendsHead.Timer());
        }
        _hooks.Attach();
        _hooks.Wake();
    }

    private static void Kick() => Service?.Kick();

    private static volatile bool _held;

    /// <summary>Panic and shutdown: cancel what is on the wire and hold the service until the user starts again.</summary>
    internal static void StopForSafety()
    {
        if (Service != null || Preview != null) _held = true;   // no service, nothing to hold (and no static left latched)
        Cancel();
    }

    /// <summary>An ordinary engine stop: cancel what is on the wire, then keep polling and reporting
    /// (WPF keeps Dares running past a stop, App.xaml.cs:3654-3663) unless a panic holds it.</summary>
    internal static void Interrupt()
    {
        Cancel();
        if (!_held) Continue();
    }

    /// <summary>A user start lifts a panic hold.</summary>
    internal static void Resume()
    {
        _held = false;
        Continue();
    }

    private static void Cancel()
    {
        try { Service?.CancelPending(); Preview?.CancelPending(); }
        finally { try { _host?.Invalidate(); } finally { _hooks?.Suspend(); } }
    }

    private static void Continue()
    {
        _hooks?.Resume();
        Service?.Resume();
        Preview?.Resume();
    }

    internal static void Shutdown()
    {
        StopForSafety();
        AccountSeed.ProfileLoaded -= Kick;
        try { _hooks?.Dispose(); }
        finally
        {
            try { Service?.Dispose(); Preview?.Dispose(); }
            finally
            {
                _host?.Dispose();
                _hooks = null;
                Service = null;
                Preview = null;
                _host = null;
                _held = false;
            }
        }
    }

    internal sealed class Host : IDareHost, IDisposable
    {
        private readonly LockdownService? _lockdown = LockdownService.Current;
        private TimeSpan _armed;
        private readonly Func<string?> _account;
        private int _generation;
        private readonly object _walletGate = new();
        internal Host(Func<string?>? account = null)
        {
            _account = account ?? (() => FriendsHead.Identity()?.UnifiedId);
            if (_lockdown == null) return;
            _lockdown.LockdownActivated += Activated;
            _lockdown.LockdownDeactivated += Deactivated;
        }
        private void Activated() => _armed = _lockdown?.Duration ?? TimeSpan.Zero;
        private void Deactivated()
        {
            var armed = _armed;
            _armed = TimeSpan.Zero;
            try { LockdownEnded?.Invoke(armed, _lockdown?.LastActiveDuration ?? TimeSpan.Zero); }
            catch (Exception ex) { Serilog.Log.Debug("Dares lockdown end failed: {E}", ex.Message); }
        }
        public event Action<TimeSpan, TimeSpan>? LockdownEnded;
        public string? Account() { try { return _account(); } catch { return null; } }
        public int TzMinutes()
        {
            try { return (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes); } catch { return 0; }
        }
        public bool ChasterReady() { try { return StakeHead.TimeOk(); } catch { return false; } }
        public int BookLockSeconds(int seconds)
        {
            if (seconds <= 0) return 0;
            try { StakeHead.EnableLossRow(); return Math.Max(0, StakeHead.BookLoss("pvp_loss", seconds)); }
            catch (Exception ex) { Serilog.Log.Debug("Dares lock booking failed: {E}", ex.Message); return 0; }
        }
        public void AdoptSp(string account, int sp)
        {
            var generation = Volatile.Read(ref _generation);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    lock (_walletGate)
                    {
                        if (generation != _generation) return;
                        var settings = CoreSettings.Current;
                        var same = string.Equals(Account(), account, StringComparison.Ordinal);
                        if (!V2WalletAdoption.Decide(same, fromBuy: true, sp, settings.SkillPoints, out var next)) return;
                        SparklesLedger.Write(settings, next, SparkleSource.Stake);
                    }
                    CoreSettings.Save();
                }
                catch (Exception ex) { Serilog.Log.Debug("Dares wallet adoption failed: {E}", ex.Message); }
            });
        }
        public int? Sparkles() { try { return CoreSettings.Current.SkillPoints; } catch { return null; } }
        public bool LockdownActive => _lockdown?.IsActive == true;
        public bool StartLockdown(TimeSpan duration)
        {
            try
            {
                if (_lockdown == null || _lockdown.IsActive) return false;
                _lockdown.Activate(duration);
                return _lockdown.IsActive;
            }
            catch (Exception ex) { Serilog.Log.Warning("Dares lockdown start failed: {E}", ex.Message); return false; }
        }
        internal void Invalidate() { lock (_walletGate) Interlocked.Increment(ref _generation); }
        public void Dispose()
        {
            Invalidate();
            if (_lockdown == null) return;
            _lockdown.LockdownActivated -= Activated;
            _lockdown.LockdownDeactivated -= Deactivated;
        }
    }

    internal sealed class MeterHooks : IDisposable
    {
        private readonly DareMeter _meter;
        private readonly DareCameraCheck _camera = new(new CameraProbe());
        private readonly object _cameraGate = new();
        private readonly object _activityGate = new();
        private DispatcherTimer? _sampler;
        private DispatcherTimer? _cameraTimer;
        private DateTimeOffset _last;
        private volatile bool _suspended;
        internal DareCameraState Camera => _camera.State;
        internal MeterHooks(DareMeter meter) { _meter = meter; }
        internal void Attach()
        {
            FlashOverlay.DareImagesDisplayed += OnFlash;
            BubbleOverlay.Popped += OnPop;
            DareTaps.SessionFinished += OnSession;
        }
        internal void Wake()
        {
            lock (_activityGate)
            {
                if (_suspended || _sampler != null || !_meter.AnyInWindow(ServerClock.UtcNow)) return;
                _last = ServerClock.UtcNow;
                _sampler = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _sampler.Tick += Sample;
                _sampler.Start();
            }
        }
        private static DareLive Live() => new(CoreEngine.IsRunning, App.Sessions?.IsRunning == true,
            MandatoryVideoOverlay.Instance.CountsForDares || Views.Tabs.SettingsTabView.DareBrowserMediaActive);
        private void Sample(object? sender, EventArgs e)
        {
            bool finished;
            lock (_activityGate)
            {
                if (_suspended) return;
                var now = ServerClock.UtcNow;
                finished = !_meter.AnyInWindow(now);
                if (!finished)
                {
                    var live = Live();
                    _meter.Sample((now - _last).TotalSeconds, live, now);
                    _last = now;
                    if (_cameraTimer == null && _meter.CameraWanted(now, live))
                    {
                        _cameraTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DareGrammar.CameraSampleMs) };
                        _cameraTimer.Tick += CameraTick;
                        _cameraTimer.Start();
                    }
                }
            }
            if (finished) { StopTimers(); _meter.Save(); }
        }
        private void CameraTick(object? sender, EventArgs e)
        {
            lock (_cameraGate)
            {
                if (_suspended) return;
                if (_camera.Step(_meter, Live(), ServerClock.UtcNow)) return;
            }
            if (_cameraTimer != null) { _cameraTimer.Stop(); _cameraTimer.Tick -= CameraTick; _cameraTimer = null; }
        }
        private void OnFlash(int count) { lock (_activityGate) if (!_suspended) _meter.NotePics(count, ServerClock.UtcNow); }
        private void OnPop() { lock (_activityGate) if (!_suspended) _meter.NoteBubbles(1, ServerClock.UtcNow); }
        private void OnSession(SessionDifficulty difficulty)
        {
            lock (_activityGate)
            {
                if (_suspended) return;
                _meter.NoteSession(difficulty, ServerClock.UtcNow);
            }
            _meter.Save();
        }
        private void StopTimers()
        {
            if (_sampler != null) { _sampler.Stop(); _sampler.Tick -= Sample; _sampler = null; }
            if (_cameraTimer != null) { _cameraTimer.Stop(); _cameraTimer.Tick -= CameraTick; _cameraTimer = null; }
            lock (_cameraGate) _camera.Step(_meter, default, ServerClock.UtcNow);
        }
        internal void Suspend()
        {
            lock (_activityGate) _suspended = true;
            lock (_cameraGate) _camera.Step(_meter, default, ServerClock.UtcNow);
            if (Dispatcher.UIThread.CheckAccess()) StopTimers();
            else Dispatcher.UIThread.Post(() => { if (_suspended) StopTimers(); });
            _meter.Save();
        }
        internal void Resume()
        {
            lock (_activityGate) _suspended = false;
            // A stop hook may run off the UI thread; the sampler is a DispatcherTimer. Wake rechecks _suspended.
            if (Dispatcher.UIThread.CheckAccess()) Wake();
            else Dispatcher.UIThread.Post(Wake);
        }
        public void Dispose()
        {
            Suspend();
            FlashOverlay.DareImagesDisplayed -= OnFlash;
            BubbleOverlay.Popped -= OnPop;
            DareTaps.SessionFinished -= OnSession;
        }
    }

    private sealed class CameraProbe : IDareCameraProbe
    {
        public bool Running => WebcamTracker.Instance.IsRunning;
        public bool FaceSeen => WebcamTracker.Instance.State == WebcamTrackingState.Tracking;
        public bool HeldOff => CoreSettings.Current.CameraHeldOff;
        public bool TryStart()
        {
            if (!WebcamConsent.IsCurrent(CoreSettings.Current)) return false;
            _ = WebcamTracker.Instance.StartAsync();
            return true;
        }
        public void Stop()
        {
            if (!CoreSettings.Current.FocusGazeEnabled) _ = WebcamTracker.Instance.StopAsync();
        }
    }
}
