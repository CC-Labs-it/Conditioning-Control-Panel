// WPF MainWindow.Marquee.cs:523-636: authored lines join the existing header rotation.
using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Banner;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private readonly BannerPoolService _bannerPool = new();
        private DispatcherTimer? _bannerPoolTimer;
        private bool _bannerPoolActive;
        private string? _pendingBannerPoolText;

        private void InitializeBannerPool()
        {
            _bannerPoolTimer?.Stop();
            _bannerPoolTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            _bannerPoolTimer.Tick += BannerPoolTimer_Tick;
            _bannerPoolTimer.Start();
            Closed += (_, _) => _bannerPoolTimer.Stop();
        }

        private void BannerPoolTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_bannerPoolTimer is not null)
                    _bannerPoolTimer.Interval = TimeSpan.FromMinutes(8 + Random.Shared.NextDouble() * 4);
                if (!IsLoaded || BannerPool is not { } pool) return;

                var line = _bannerPool.NextLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    _pendingBannerPoolText = null;
                    if (!_bannerPoolActive) return;
                    // Keep text through the outgoing fade; an empty line would blink mid-read.
                    _bannerPoolActive = false;
                    RebuildBannerBeats();
                }
                else if (_bannerPoolActive)
                    _pendingBannerPoolText = line;
                else
                {
                    pool.Text = line;
                    _bannerPoolActive = true;
                    RebuildBannerBeats();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Banner pool tick failed; the beat is left as it was");
            }
        }

        private void ApplyPendingBannerPoolText(TextBlock outgoing, TextBlock incoming)
        {
            if (_pendingBannerPoolText is null || BannerPool is not { } pool) return;
            // A text swap is invisible only when the pool is at neither end of the crossfade.
            if (ReferenceEquals(outgoing, pool) || ReferenceEquals(incoming, pool)) return;
            pool.Text = _pendingBannerPoolText;
            _pendingBannerPoolText = null;
        }
    }
}
