// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Marquee.cs: InitializeMarqueeBanner's migration and
// server timers (:649-735), RefreshMarqueeFromSettings (:740-774), CheckServerUpdateBanner (:799-826); and the
// URL-first half of BtnUpdateAvailable_Click (MainWindow.AccountShell.cs:482-505), wired in MainShellWindow.AccountShell.cs.
// Timings are WPF's: the marquee 3 s after the window opens and then every 5 minutes, the update banner at 5 s.
// The feed is the Core V2AuthService the announcement already rides (AnnouncementClient, Announcement.cs), so a
// test shell that does not seed it makes no request. Every failure is silent (Core logs at Debug).
// IMPORTED-AND-ADAPTED from trunk cab1eb98f / d30fcf576 (the fetches moved into Core instead of a head HttpClient).

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The server banner's download page, when it sent one (WPF _serverUpdateUrl).</summary>
        internal string? ServerUpdateUrl { get; private set; }

        private bool _serverBannersArmed;
        private DispatcherTimer? _marqueeRefreshTimer;

        private void InitializeServerBanners()
        {
            MigrateMarqueeMessage();
            Opened += (_, _) =>
            {
                if (_serverBannersArmed || AnnouncementClient == null) return;
                _serverBannersArmed = true;
                DispatcherTimer.RunOnce(() => _ = RefreshMarqueeFromServerAsync(), TimeSpan.FromSeconds(3));
                DispatcherTimer.RunOnce(() => _ = CheckServerUpdateBannerAsync(), TimeSpan.FromSeconds(5));
                var refresh = _marqueeRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
                refresh.Tick += (_, _) => _ = RefreshMarqueeFromServerAsync();
                refresh.Start();
                Closed += (_, _) => refresh.Stop();
            };
        }

        /// <summary>WPF InitializeMarqueeBanner :656-667: the one-shot neutral-default migration, then a banner nobody
        /// wrote (blank, or one of the two retired defaults) takes the active mod's line.</summary>
        private static void MigrateMarqueeMessage()
        {
            try
            {
                var s = CoreSettings.Current;
                s.MigrateMarqueeMessage(App.Mods?.ActiveModId);
                var saved = s.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(saved) || saved.Contains("WELCOME TO YOUR CONDITIONING") || saved.Contains("RELAX AND SUBMIT"))
                    s.MarqueeMessage = Tabs.MarqueeScroller.ResolveDefaultMarqueeMessage();
            }
            catch (Exception ex) { Log.Warning("Failed to initialize marquee banner: {Error}", ex.Message); }
        }

        /// <summary>WPF RefreshMarqueeFromSettings: a new, non-blank server line replaces the saved one and the
        /// Home strip restarts. Compared against the RAW saved value (WPF :757-763: an upper-cased copy never
        /// matched a mixed-case message, so the strip restarted on every poll).</summary>
        internal async Task RefreshMarqueeFromServerAsync()
        {
            var make = AnnouncementClient;
            if (make == null) return;
            var message = await make().GetMarqueeMessageAsync();
            if (message == null || message == CoreSettings.Current.MarqueeMessage) return;
            Log.Information("Marquee message updated from server");
            CoreSettings.Current.MarqueeMessage = message;
            await Dispatcher.UIThread.InvokeAsync(() => Named<Tabs.SettingsTabView>("SettingsTab")?.StartMarqueeAnimation());
        }

        internal static Version CurrentAppVersion() =>
            Version.TryParse(CoreReleaseContent.AppVersion, out var v) ? v : new Version(1, 0, 0);

        /// <summary>WPF CheckServerUpdateBanner: when the server names a newer build, the pill turns urgent.</summary>
        internal async Task CheckServerUpdateBannerAsync()
        {
            var make = AnnouncementClient;
            if (make == null) return;
            var banner = await make().GetUpdateBannerAsync(CurrentAppVersion());
            if (banner == null) return;
            Log.Information("Server update banner enabled: version={Version}", banner.Version);
            await Dispatcher.UIThread.InvokeAsync(() => ApplyServerUpdateBanner(banner.Version!, banner.Url));
        }

        /// <summary>WPF Marquee.cs:816-826: Tag "UrgentUpdate", "Update to v{0}", and the download tooltip when the
        /// banner carries a URL (the pill then opens it), else the install one.</summary>
        internal void ApplyServerUpdateBanner(string version, string? url)
        {
            ServerUpdateUrl = url;
            if (Named<Button>("BtnUpdateAvailable") is not { } b) return;
            b.Tag = "UrgentUpdate";
            b.Bind(ContentControl.ContentProperty, LocF("btn_update_to_version", version));
            b.Bind(ToolTip.TipProperty, LocF(!string.IsNullOrEmpty(url)
                ? "tooltip_update_to_version_download" : "tooltip_update_to_version_install", version));
        }
    }
}
