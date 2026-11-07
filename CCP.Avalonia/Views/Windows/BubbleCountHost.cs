using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>The Avalonia <see cref="IBubbleCountHost"/>: the game and answer windows, and WPF
    /// BubbleCountService.ShowFullscreenMessage (:597) for the strict retry / mercy card.</summary>
    internal sealed class BubbleCountHost : IBubbleCountHost
    {
        public static BubbleCountHost Instance { get; } = new();
        public BubbleCountScheduler Scheduler { get; internal set; }   // tests swap in a fixed library

        private readonly List<Window> _messages = new();

        private BubbleCountHost() => Scheduler = new BubbleCountScheduler(this)
        {
            // WPF BubbleCountService _packVideos: active content-pack clips join the pick.
            PackVideos = new ConditioningControlPanel.Services.PackMediaPool(ConditioningControlPanel.Services.ContentPackStore.VideoType),
        };

        internal IReadOnlyList<Window> Messages => _messages;
        public double LastVideoDurationSeconds => BubbleCountWindow.LastVideoDurationSeconds;
        // WPF InteractionQueue: Video, LockCard and PopQuiz claim the screen before a game may start.
        public bool OtherInteractionActive =>
            CoreEngine.Video?.IsPlaying == true || LockCardWindow.IsAnyOpen() || PopQuizWindow.IsAnyOpen();

        public bool FeedOwnsTheScreen => Dispatcher.UIThread.Invoke(Games.GameWindow.IsForYouOnScreen);

        // ponytail: shared overlay parking is unported; reuse the existing video text hold.
        public void SetEffectsHold(bool hold) => Dispatcher.UIThread.Invoke(() =>
        {
            // WPF ReleaseFullscreenHold: a video that took over retains its own text pause.
            if (hold || CoreEngine.Video?.IsPlaying != true) Overlays.BouncingTextOverlay.PauseForVideo(hold);
        });

        public void ShowNoVideos() => Dispatcher.UIThread.Invoke(() =>
        {
            var packs = ContentPackStore.Current;
            var key = BubbleCountScheduler.NoVideosMessageKey(packs?.InstalledPackIds.Count ?? 0,
                packs?.GetActivePackIds().Count ?? 0);
            App.Notifications.Show(global::ConditioningControlPanel.Localization.Loc.Get(key), Helpers.NotificationType.Warning);
        });

        // The app seeds CoreDispatch (AvaloniaCoreDispatch), but headless tests do not: every door hops onto the UI thread.
        // WPF skips the game outright when the shared LibVLC is unusable (BubbleCountService.cs:373):
        // opening it would end as a failed count and, in strict mode, retry forever.
        public bool CanPlay => Platform.LibVlcAudio.Shared != null;
        public bool CanPlayPath(string path) => CanPlay || (!Platform.BrowserVideoSurface.StoodDown && !Platform.BrowserVideoSurface.SessionActive && Services.Video.Browser.BrowserVideoGate.ShouldUseBrowser(path));

        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) => Dispatcher.UIThread.Invoke(() =>
            BubbleCountWindow.ShowOnAllMonitors(path, (BubbleCountScheduler.Difficulty)difficulty, strict, onComplete));

        /// <summary>WPF: magenta 64 pt bold text on black, maximised on every screen, then <paramref name="then"/>.</summary>
        public void ShowMessage(string text, int ms, Action then) => Dispatcher.UIThread.Invoke(() =>
        {
            var host = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var screens = host is null ? Array.Empty<Screen>() : Features.ScreenList.Enumerate(host);
            var batch = new List<Window>();
            // WPF BubbleCountService.cs:588 App.GetGlobalScreens: the "Show content on" pick.
            var s = CoreSettings.Current;
            var primary = screens.ToList().FindIndex(x => x.IsPrimary);
            var picked = Overlays.PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal,
                s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary).Select(i => (Screen?)screens[i]).ToList();
            foreach (var screen in picked.Count > 0 ? picked : new List<Screen?> { null })
            {
                var w = new Window
                {
                    WindowDecorations = WindowDecorations.None,
                    Background = Brushes.Black,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Content = new TextBlock
                    {
                        Text = text,
                        Foreground = Brushes.Magenta,
                        FontSize = 64,
                        FontWeight = FontWeight.Bold,
                    FontFamily = new FontFamily("Impact, Arial Black, Segoe UI"),
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                if (screen != null)
                {
                    w.WindowStartupLocation = WindowStartupLocation.Manual;
                    w.Position = new PixelPoint(screen.Bounds.X + 100, screen.Bounds.Y + 100);
                }
                w.Show();
                w.WindowState = WindowState.Maximized;
                batch.Add(w);
            }
            _messages.AddRange(batch);
            DispatcherTimer.RunOnce(() =>
            {
                if (!_messages.Remove(batch[0])) return;   // panic closed it: no retry
                foreach (var w in batch) { _messages.Remove(w); w.Close(); }
                then();
            }, TimeSpan.FromMilliseconds(ms));
        });

        public void CloseAll() => Dispatcher.UIThread.Invoke(() =>
        {
            foreach (var w in _messages.ToArray()) try { w.Close(); } catch { }
            _messages.Clear();
            BubbleCountWindow.ForceCloseAll();
            BubbleCountResultWindow.ForceCloseAll();
        });
    }
}
