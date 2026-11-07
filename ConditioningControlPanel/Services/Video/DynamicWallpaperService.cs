using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Safety;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinForms = System.Windows.Forms;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// DYNAMIC wallpaper: the same collage as the static one (<see cref="CollageComposer"/>'s geometry and
    /// look), but up to <see cref="AnimatedCap"/> of its gif/webp tiles MOVE, live, behind the desktop icons.
    /// One collage PER MONITOR, each planned at that monitor's own pixel size, rotating together.
    ///
    /// Why it looks like this: the 24H2+ "raised desktop" refuses normal window painting (GDI, WPF and
    /// windowed WebView2 all show black under the icons). Only a DirectComposition visual gets composited
    /// in that slot. So, per monitor: a WS_CHILD of Progman z-ordered just below the icons
    /// (SHELLDLL_DefView), a DComp target, a visual, a composition swapchain, drawn with Direct2D. Every
    /// frame is decoded once, baked to its tile shape and uploaded, so the render loop never decodes.
    ///
    /// It never writes a wallpaper, so nothing needs restoring after a crash: the windows die with the
    /// process and a slideshow keeps running underneath.
    ///
    /// House rules it keeps:
    /// <list type="bullet">
    /// <item>Stop never waits: panic runs through it on the UI thread (<see cref="Stop"/>).</item>
    /// <item>Gentle visuals slows the gifs and lengthens the crossfade (<see cref="GentleVisuals"/>).</item>
    /// <item>It draws nothing while the desktop is covered, and holds still on battery, so it never takes
    /// frames from a game (<see cref="UpdateDesktopState"/>).</item>
    /// <item>Frames live in GPU memory under one budget (<see cref="DynamicFrameBudget"/>).</item>
    /// <item>A lost GPU, an Explorer restart or a display change rebuilds it (<see cref="RequestRestart"/>).</item>
    /// </list>
    ///
    /// Threads: the windows, DComp and the render loop live on one dedicated STA thread (so CCP's UI thread
    /// is never tied to Explorer's desktop and never waits on a vsync); collages are built on a below-normal
    /// thread through a second Direct2D context and handed over finished.
    /// </summary>
    public sealed partial class DynamicWallpaperService : IDisposable
    {
        // ---- tuning (19 moving of ~40 tiles = ~37 fps on two monitors in the original test) ----
        private const int AnimatedCap = 19;             // most tiles that move; the rest are frozen stills
        private const int RenderTickMs = 16;
        private const int CrossfadeMs = 900;            // new collage over the old; Gentle only lengthens it
        private const int MinRotateSeconds = 30;        // a build decodes every frame: never back to back
        private const int MaxRestarts = 3;              // lost GPU in a row before Static takes over
        private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(4);   // Explorer needs a moment

        // ---- run state (UI thread) ----
        private volatile bool _active;
        private Thread? _thread;
        private volatile SynchronizationContext? _threadSync;   // the wallpaper thread's WinForms context
        private Task? _stopTask;                                // the last Stop still finishing off-thread
        private readonly ManualResetEventSlim _gpuReady = new(false);
        private volatile bool _gpuFailed;
        private volatile bool _unavailable;                     // could not start here: Static for this run
        private CancellationTokenSource? _runCts;
        private System.Windows.Threading.DispatcherTimer? _rotateTimer, _restartTimer;
        private Models.AppSettings? _hookedSettings;
        private int _restarts;
        private DateTime _lastRestartUtc;
        private volatile bool _restartAsked;

        // ---- GPU (made and released on the wallpaper thread; builders only touch _loadCtx, under _gpuLock) ----
        private readonly object _gpuLock = new();
        private bool _gpuGone;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _d3dCtx;
        private IDXGIFactory2? _dxgiFactory;
        private ID2D1Factory1? _d2dFactory;
        private ID2D1Multithread? _d2dLock;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _drawCtx, _loadCtx;
        private IDCompositionDevice? _dcomp;
        private static readonly BitmapProperties1 FrameProps = new(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.None);

        // ---- desktop + render (wallpaper thread only) ----
        private CollageSet? _current, _outgoing;
        private CollageSet? _pending;                 // builder to render thread hand-off (Interlocked)
        private bool _deviceLost;
        private long _lastTick, _fadeStart, _fadeMs;

        /// <summary>True when the wallpaper should run as Dynamic: on, Dynamic picked, NOT in performance
        /// mode, and this PC could start it.</summary>
        public static bool ShouldRun(Models.AppSettings? s)
            => s != null && s.CollageWallpaperEnabled && s.CollageMode == Models.WallpaperMode.Dynamic && !s.PerformanceMode
               && App.DynamicWallpaper?._unavailable != true;

        /// <summary>Dynamic could not start on this PC, so Static is standing in until the next launch.</summary>
        public bool IsUnavailable => _unavailable;

        public DynamicWallpaperService()
        {
            // Off in performance mode: a saved Dynamic choice under it falls back to Static.
            var s = App.Settings?.Current;
            if (s != null && s.PerformanceMode && s.CollageMode == Models.WallpaperMode.Dynamic)
            {
                s.CollageMode = Models.WallpaperMode.Static;
                App.Settings?.Save();
            }
            HookSettings(s);
            if (App.Settings != null) App.Settings.CurrentReplaced += OnSettingsReplaced;
        }

        // ================================================================ lifecycle (UI thread)

        /// <summary>Put the moving collage behind the icons. UI thread; idempotent. Called through
        /// <see cref="WallpaperService.StartCollage"/>.</summary>
        public void Start()
        {
            if (_active) return;
            _active = true;

            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            // A Stop may still be finishing off-thread: the render thread shares this instance's fields, so
            // the next one starts only after the last one is gone.
            var prior = _stopTask;
            if (prior is { IsCompleted: false })
                prior.ContinueWith(_ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(BeginRun)), TaskScheduler.Default);
            else
                BeginRun();
        }

        private void BeginRun()
        {
            if (!_active || _thread != null) return;
            _runCts = new CancellationTokenSource();
            _gpuReady.Reset();
            _gpuFailed = false;
            _restartAsked = false;
            _thread = new Thread(WallpaperThreadMain)
            {
                IsBackground = true,
                Name = "CCP dynamic wallpaper",
                Priority = ThreadPriority.BelowNormal,
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            App.Logger?.Information("[DynamicWallpaper] Started (rotate={Rotate}, every {Sec}s)",
                WallpaperService.RotateOn(), WallpaperService.CollageInterval());
            RequestBuild();
            RefreshSchedule();
        }

        /// <summary>Take the moving collage down. Never waits: the render thread drops its windows on its
        /// next message (one frame), and the rest finishes off-thread. Panic runs through here. UI thread.</summary>
        public void Stop()
        {
            if (!_active) return;
            _active = false;

            try { _rotateTimer?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex, "dynamic rotate timer stop"); }
            try { _restartTimer?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex, "dynamic restart timer stop"); }
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;

            var thread = _thread;
            var cts = _runCts;
            var build = _buildTask;
            _thread = null;
            _runCts = null;
            if (thread == null) return;   // a Start still waiting for the last Stop: nothing of ours is up

            try { cts?.Cancel(); } catch (Exception ex) { Diag.Swallowed(ex, "dynamic build cancel"); }
            try { _threadSync?.Post(_ => WinForms.Application.ExitThread(), null); }
            catch (Exception ex) { Diag.Swallowed(ex, "dynamic thread exit post"); }
            _threadSync = null;

            _stopTask = Task.Run(() =>
            {
                try { build?.Wait(3000); } catch (Exception ex) { Diag.Swallowed(ex, "dynamic build wait"); }
                if (!thread.Join(5000))
                    App.Logger?.Warning("[DynamicWallpaper] Render thread did not exit within 5s");
                try { cts?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex, "dynamic cts dispose"); }
                RepaintDesktop();
                App.Logger?.Information("[DynamicWallpaper] Stopped; desktop refreshed");
            });
        }

        /// <summary>Exit and logoff only: wait for the last Stop to finish. False on timeout.</summary>
        public bool WaitStopped(TimeSpan timeout)
        {
            try { return _stopTask?.Wait(timeout) ?? true; }
            catch (Exception ex) { Diag.Swallowed(ex, "dynamic stop wait"); return true; }
        }

        /// <summary>Arm or disarm rotation to match Rotate + interval, live.</summary>
        public void RefreshSchedule()
        {
            if (!_active) return;
            if (_rotateTimer == null)
            {
                _rotateTimer = new System.Windows.Threading.DispatcherTimer();
                _rotateTimer.Tick += (_, _) => RequestBuild();
            }
            if (WallpaperService.RotateOn())
            {
                _rotateTimer.Interval = TimeSpan.FromSeconds(Math.Max(MinRotateSeconds, WallpaperService.CollageInterval()));
                _rotateTimer.Start();
            }
            else
            {
                _rotateTimer.Stop();
            }
        }

        public void Dispose()
        {
            Stop();
            WaitStopped(TimeSpan.FromSeconds(3));
            HookSettings(null);
            if (App.Settings != null) App.Settings.CurrentReplaced -= OnSettingsReplaced;
        }

        private void OnModChanged(object? sender, Models.ModPackage e)
        {
            if (_active) RequestBuild();
        }

        private void OnDisplayChanged(object? sender, EventArgs e) => RequestRestart("display change", countsAsFailure: false);

        /// <summary>The windows no longer match the desktop (lost GPU, Explorer restart, display change):
        /// take them down and come back shortly. Any thread. A lost GPU that keeps coming back ends in
        /// Static for this run.</summary>
        private void RequestRestart(string why, bool countsAsFailure)
        {
            if (_restartAsked) return;
            _restartAsked = true;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_active) return;
                App.Logger?.Warning("[DynamicWallpaper] Rebuilding: {Why}", why);
                if (countsAsFailure)
                {
                    if (DateTime.UtcNow - _lastRestartUtc > TimeSpan.FromMinutes(10)) _restarts = 0;
                    _lastRestartUtc = DateTime.UtcNow;
                    if (++_restarts > MaxRestarts) { FallBackToStatic(); return; }
                }
                if (_restartTimer == null)
                {
                    _restartTimer = new System.Windows.Threading.DispatcherTimer { Interval = RestartDelay };
                    _restartTimer.Tick += (_, _) =>
                    {
                        _restartTimer.Stop();
                        if (!_active) return;
                        Stop();
                        Start();
                    };
                }
                _restartTimer.Stop();
                _restartTimer.Start();
            }));
        }

        private void HookSettings(Models.AppSettings? settings)
        {
            if (_hookedSettings != null) _hookedSettings.PropertyChanged -= OnSettingChanged;
            _hookedSettings = settings;
            if (_hookedSettings != null) _hookedSettings.PropertyChanged += OnSettingChanged;
        }

        private void OnSettingsReplaced()
        {
            try { HookSettings(App.Settings?.Current); }
            catch (Exception ex) { Diag.Swallowed(ex, "dynamic wallpaper settings rebind"); }
        }

        /// <summary>Performance mode switched on while Dynamic is picked: the saved mode becomes Static and
        /// a running Dynamic wallpaper hands over.</summary>
        private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Models.AppSettings.PerformanceMode)) return;
            var s = App.Settings?.Current;
            if (s == null || !s.PerformanceMode || s.CollageMode != Models.WallpaperMode.Dynamic) return;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    s.CollageMode = Models.WallpaperMode.Static;
                    App.Settings?.Save();
                    App.Logger?.Information("[DynamicWallpaper] Performance mode on - wallpaper switched to Static");
                    if (_active) App.Wallpaper?.RestartCollage();
                }
                catch (Exception ex) { App.Logger?.Warning("[DynamicWallpaper] Performance-mode switch failed: {Error}", ex.Message); }
            }));
        }

        // ================================================================ wallpaper thread

        private void WallpaperThreadMain()
        {
            WinForms.Timer? render = null, slow = null;
            try
            {
                if (!InitGpu()) { _gpuFailed = true; return; }

                SetupDesktopLayer();
                if ((_isRaised ? _progman : _workerW) == IntPtr.Zero)
                {
                    App.Logger?.Warning("[DynamicWallpaper] No desktop layer available - not starting");
                    _gpuFailed = true;
                    return;
                }

                // The monitor windows are made and put under the icons only once the first collage is ready
                // (RenderTick): until then the user's own wallpaper stays up, and a build that yields
                // nothing never blanks the desktop.
                SynchronizationContext.SetSynchronizationContext(new WinForms.WindowsFormsSynchronizationContext());
                _threadSync = SynchronizationContext.Current;
                render = new WinForms.Timer { Interval = RenderTickMs };
                render.Tick += (_, _) => RenderTick();
                slow = new WinForms.Timer { Interval = 1000 };
                slow.Tick += (_, _) => { KeepBelowIcons(); UpdateDesktopState(); };
                _lastTick = Environment.TickCount64;
                render.Start();
                slow.Start();
                _gpuReady.Set();

                if (_active) WinForms.Application.Run(new WinForms.ApplicationContext());   // until Stop posts ExitThread
            }
            catch (Exception ex)
            {
                _gpuFailed = true;
                App.Logger?.Error(ex, "[DynamicWallpaper] Render thread failed");
            }
            finally
            {
                _gpuReady.Set();   // never leave a builder waiting
                try { render?.Stop(); render?.Dispose(); slow?.Stop(); slow?.Dispose(); }
                catch (Exception ex) { Diag.Swallowed(ex, "dynamic timers dispose"); }
                Teardown();
                if (_gpuFailed && _active)
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(FallBackToStatic));
            }
        }

        /// <summary>This PC could not run the moving wallpaper: Static stands in for the rest of this run.</summary>
        private void FallBackToStatic()
        {
            _unavailable = true;
            App.Logger?.Warning("[DynamicWallpaper] Could not run on this PC - using the Static wallpaper instead");
            try
            {
                if (_active) App.Wallpaper?.RestartCollage();
            }
            catch (Exception ex) { App.Logger?.Warning("[DynamicWallpaper] Static fallback failed: {Error}", ex.Message); }
        }

        private bool InitGpu()
        {
            try
            {
                var hr = D3D11.D3D11CreateDevice(null, Vortice.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                    new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 },
                    out ID3D11Device? device, out ID3D11DeviceContext? ctx);
                if (hr.Failure || device == null)
                {
                    App.Logger?.Warning("[DynamicWallpaper] No Direct3D 11 device (hr {Hr})", hr.Code);
                    return false;
                }
                _device = device;
                _d3dCtx = ctx;
                _dxgiFactory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
                using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
                try
                {
                    using var adapter = dxgiDevice.GetAdapter();
                    App.Logger?.Information("[DynamicWallpaper] GPU: {Gpu}", adapter.Description.Description.Trim());
                }
                catch (Exception ex) { Diag.Swallowed(ex, "dynamic adapter name"); }

                // Multithreaded: the builder uploads frames from its own thread through its own context.
                _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.MultiThreaded, DebugLevel.None);
                _d2dLock = _d2dFactory.QueryInterface<ID2D1Multithread>();
                _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
                _drawCtx = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
                _loadCtx = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
                _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[DynamicWallpaper] GPU init failed");
                return false;
            }
        }

        /// <summary>One window per monitor: shown off-screen, bound to DComp, then put under the icons.
        /// Called on the first collage. False when not a single monitor could be bound.</summary>
        private bool CreateSurfaces()
        {
            var screens = WinForms.Screen.AllScreens;
            if (screens == null || screens.Length == 0)
            {
                App.Logger?.Warning("[DynamicWallpaper] No screens enumerated");
                return false;
            }
            var virt = WinForms.SystemInformation.VirtualScreen;
            int bound = 0;
            foreach (var screen in screens)
            {
                var b = screen.Bounds;
                var s = new Surface { X = b.X, Y = b.Y, Width = Math.Max(16, b.Width), Height = Math.Max(16, b.Height) };
                s.Form = new HostForm
                {
                    FormBorderStyle = WinForms.FormBorderStyle.None,
                    ShowInTaskbar = false,
                    StartPosition = WinForms.FormStartPosition.Manual,
                    Bounds = new System.Drawing.Rectangle(-32000, -32000, s.Width, s.Height),
                };
                _surfaces.Add(s);   // tracked first so Teardown always reaches it
                s.Form.Show();
                if (!BindComposition(s)) continue;
                AttachToDesktop(s.Form.Handle, s, virt.X, virt.Y);
                bound++;
            }
            App.Logger?.Information("[DynamicWallpaper] Behind the icons on {Count} of {Total} monitor(s) (raised={Raised})",
                bound, _surfaces.Count, _isRaised);
            return bound > 0;
        }

        /// <summary>Composition swapchain, DComp visual, target on the window. A plain HWND swapchain is
        /// not composited once the window sits under the icons; a DComp visual is.</summary>
        private bool BindComposition(Surface s)
        {
            try
            {
                var desc = new SwapChainDescription1
                {
                    Width = (uint)s.Width,
                    Height = (uint)s.Height,
                    Format = Format.B8G8R8A8_UNorm,
                    Stereo = false,
                    SampleDescription = new SampleDescription(1, 0),
                    BufferUsage = Usage.RenderTargetOutput,
                    BufferCount = 2,
                    Scaling = Scaling.Stretch,
                    SwapEffect = Vortice.DXGI.SwapEffect.FlipSequential,
                    AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
                };
                s.Swap = _dxgiFactory!.CreateSwapChainForComposition(_device!, desc);
                using (var buffer = s.Swap.GetBuffer<IDXGISurface>(0))
                {
                    var targetProps = new BitmapProperties1(
                        new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                        96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw);
                    s.Target = _drawCtx!.CreateBitmapFromDxgiSurface(buffer, targetProps);
                }
                _dcomp!.CreateTargetForHwnd(s.Form!.Handle, true, out IDCompositionTarget dcTarget);
                s.DcTarget = dcTarget;
                s.Visual = _dcomp.CreateVisual();
                s.Visual.SetContent(s.Swap);
                s.DcTarget.SetRoot(s.Visual);
                _dcomp.Commit();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[DynamicWallpaper] Composition setup failed for a monitor");
                // Leave nothing half-bound for the render loop to present into.
                s.Visual?.Dispose(); s.Visual = null;
                s.DcTarget?.Dispose(); s.DcTarget = null;
                s.Target?.Dispose(); s.Target = null;
                s.Swap?.Dispose(); s.Swap = null;
                return false;
            }
        }

        // ================================================================ render loop (wallpaper thread)

        private void RenderTick()
        {
            long now = Environment.TickCount64;
            var next = Interlocked.Exchange(ref _pending, null);
            if (next != null)
            {
                // The old one stays on screen under the new one until the fade ends.
                _outgoing?.Dispose();
                _outgoing = _current;
                _current = next;
                _fadeStart = now;
                _fadeMs = GentleVisuals.FadeMs(CrossfadeMs);
                _lastTick = now;   // build time is not animation time
                foreach (var s in _surfaces) s.NeedsDraw = true;

                // First collage of the run: only now do the windows go under the icons.
                if (_surfaces.Count == 0)
                {
                    if (!CreateSurfaces())
                    {
                        _gpuFailed = true;   // no monitor could show it: Static takes over
                        WinForms.Application.ExitThread();
                        return;
                    }
                    foreach (var s in _surfaces) s.NeedsDraw = true;
                    UpdateDesktopState();
                }
            }

            var c = _current;
            var ctx = _drawCtx;
            if (c == null || ctx == null || _deviceLost) return;

            long dt = Math.Clamp(now - _lastTick, 0, 250);   // a sleep/stall must not leave a backlog
            _lastTick = now;

            // Advance every tile ONCE per tick, whichever monitor shows it.
            bool changed = false;
            if (!_holdStill)
            {
                double speed = GentleVisuals.Speed;
                foreach (var t in c.Screens.SelectMany(m => m.Tiles))
                    changed |= DynamicFrameBudget.Advance(t, dt, speed);
            }

            // First collage: the tiles fade in over its own backdrop. After that: new collage over the old.
            float fade = _fadeMs <= 0 ? 1f : Math.Clamp((now - _fadeStart) / (float)_fadeMs, 0f, 1f);
            bool fading = fade < 1f;
            if (!fading && _fadeMs > 0)   // done: one last draw at full strength, then the old one goes
            {
                _outgoing?.Dispose();
                _outgoing = null;
                _fadeMs = 0;
                changed = true;
            }

            foreach (var s in _surfaces)
            {
                if (s.Swap == null || s.Target == null) continue;
                if (changed || fading) s.NeedsDraw = true;
                if (!s.NeedsDraw || s.Covered) continue;   // nothing new, or nobody can see it
                try
                {
                    // Each monitor has its own collage at its own size, so this is 1:1. Only between a display
                    // change and its rebuild can a collage meet another size: cover, never stretch (#1544).
                    var mine = c.For(s);
                    if (mine == null) continue;
                    ctx.Target = s.Target;
                    ctx.BeginDraw();
                    ctx.Clear(new Color4(0f, 0f, 0f, 1f));
                    var old = _outgoing?.For(s);
                    if (old != null) DrawCollage(ctx, old, FitTo(s, old), 1f, 1f);
                    DrawCollage(ctx, mine, FitTo(s, mine), old != null ? fade : 1f, fade);
                    ctx.Transform = Matrix3x2.Identity;
                    var drawn = ctx.EndDraw();

                    // Present uses the D3D device the builder's uploads also use: take Direct2D's lock.
                    _d2dLock?.Enter();
                    SharpGen.Runtime.Result shown;
                    try { shown = s.Swap.Present(1, PresentFlags.None); }
                    finally { _d2dLock?.Leave(); }

                    if (drawn.Failure || shown.Failure)
                    {
                        _deviceLost = true;
                        App.Logger?.Warning("[DynamicWallpaper] GPU device lost (draw {Draw}, present {Present})", drawn.Code, shown.Code);
                        RequestRestart("lost the GPU", countsAsFailure: true);
                        return;
                    }
                    s.NeedsDraw = false;
                }
                catch (Exception ex) { Diag.Swallowed(ex, "dynamic render frame"); }
            }
        }

        private static Matrix3x2 FitTo(Surface s, Collage c)
        {
            var (k, ox, oy) = DynamicFrameBudget.CoverFit(s.Width, s.Height, c.RefW, c.RefH);
            return Matrix3x2.CreateScale(k) * Matrix3x2.CreateTranslation(ox, oy);
        }

        private static void DrawCollage(ID2D1DeviceContext ctx, Collage c, Matrix3x2 monitor, float baseOpacity, float layerOpacity)
        {
            ctx.Transform = monitor;
            if (c.Base != null) ctx.DrawBitmap(c.Base, baseOpacity, InterpolationMode.Linear);
            foreach (var t in c.Tiles)
            {
                if (t.Frames.Count == 0) continue;
                ctx.Transform =
                    Matrix3x2.CreateTranslation(-t.BakeW / 2f, -t.BakeH / 2f) *
                    Matrix3x2.CreateScale(t.ScaleX, t.ScaleY) *
                    Matrix3x2.CreateRotation(t.AngleRad) *
                    Matrix3x2.CreateTranslation(t.Cx, t.Cy) *
                    monitor;
                ctx.DrawBitmap(t.Frames[t.Cur], layerOpacity, InterpolationMode.Linear);
            }
            ctx.Transform = monitor;
            if (c.Overlay != null) ctx.DrawBitmap(c.Overlay, layerOpacity, InterpolationMode.Linear);
        }

        /// <summary>Explorer reshuffles its children now and then; keep each window directly under the
        /// icons. A dead Progman means Explorer restarted and took our windows with it.</summary>
        private void KeepBelowIcons()
        {
            if (_surfaces.Count > 0 && _progman != IntPtr.Zero && !IsWindow(_progman))
            {
                RequestRestart("Explorer restarted", countsAsFailure: false);
                return;
            }
            if (!_isRaised || _shellDefView == IntPtr.Zero) return;
            foreach (var s in _surfaces)
            {
                try
                {
                    if (s.Swap != null && s.Form != null && s.Form.IsHandleCreated)   // attached monitors only
                        SetWindowPos(s.Form.Handle, _shellDefView, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                catch (Exception ex) { Diag.Swallowed(ex, "dynamic keep below icons"); }
            }
        }

        private void Teardown()
        {
            lock (_gpuLock) { _gpuGone = true; }   // a late builder now stops before touching the device

            foreach (var s in _surfaces)
            {
                try { if (s.Form != null && s.Form.IsHandleCreated) SetParent(s.Form.Handle, IntPtr.Zero); }
                catch (Exception ex) { Diag.Swallowed(ex, "dynamic detach"); }
                s.Visual?.Dispose();
                s.DcTarget?.Dispose();
                s.Target?.Dispose();
                s.Swap?.Dispose();
                try { s.Form?.Close(); s.Form?.Dispose(); }
                catch (Exception ex) { Diag.Swallowed(ex, "dynamic window close"); }
            }
            _surfaces.Clear();

            _outgoing?.Dispose();
            _outgoing = null;
            _current?.Dispose();
            _current = null;
            Interlocked.Exchange(ref _pending, null)?.Dispose();

            _loadCtx?.Dispose(); _loadCtx = null;
            _drawCtx?.Dispose(); _drawCtx = null;
            _d2dDevice?.Dispose(); _d2dDevice = null;
            _d2dLock?.Dispose(); _d2dLock = null;
            _d2dFactory?.Dispose(); _d2dFactory = null;
            _dcomp?.Dispose(); _dcomp = null;
            _dxgiFactory?.Dispose(); _dxgiFactory = null;
            _d3dCtx?.Dispose(); _d3dCtx = null;
            _device?.Dispose(); _device = null;

            _deviceLost = false;
            lock (_gpuLock) { _gpuGone = false; }
        }

        /// <summary>One build: a collage for every monitor, sharing one frame budget and one rotation.</summary>
        private sealed class CollageSet : IDisposable
        {
            public readonly List<Collage> Screens = new();
            public int MovingGif, MovingWebp, FrameTotal;
            public long BudgetLeft = DynamicFrameBudget.Bytes;   // shared by every monitor
            private List<(int X, int Y, int W, int H)>? _rects;

            /// <summary>The collage planned for this monitor (<see cref="DynamicFrameBudget.MatchMonitor"/>).</summary>
            public Collage? For(Surface s)
            {
                _rects ??= Screens.Select(c => (c.MonX, c.MonY, c.MonW, c.MonH)).ToList();
                int i = DynamicFrameBudget.MatchMonitor(_rects,
                    s.X, s.Y, s.Width, s.Height);
                return i < 0 ? null : Screens[i];
            }

            public void Dispose()
            {
                foreach (var c in Screens) c.Dispose();
                Screens.Clear();
            }
        }

        /// <summary>One monitor's collage, planned at that monitor's physical size.</summary>
        private sealed class Collage : IDisposable
        {
            public int MonX, MonY, MonW, MonH;   // the monitor it was planned for
            public float RefW, RefH;              // its canvas (= the monitor's size)
            public ID2D1Bitmap? Base, Overlay;
            public readonly List<Tile> Tiles = new();

            public void Dispose()
            {
                Base?.Dispose();
                Overlay?.Dispose();
                foreach (var t in Tiles) t.Dispose();
                Tiles.Clear();
            }
        }

    }
}
