// B2 section 4: Linux first race card. The fetch and owned Chromium host arrive in separate lanes.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Race;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        internal static Func<bool> UseLinuxRaceCard = OperatingSystem.IsLinux;
        internal static IRaceEngineFetch? RaceEngineFetch;
        internal static Func<string, bool> OpenRaceBrowser = ExternalOpener.Open;
        internal const string RaceBrowserUrl = "https://app.cclabs.app/race3d";
        private bool _raceCard;
        private CancellationTokenSource? _raceFetch;
        internal Button RaceDownload = null!, RaceCancel = null!, RaceSlower = null!, RaceBrowser = null!;
        internal TextBlock RaceStatus = null!;
        internal Task RaceFetchPending { get; private set; } = Task.CompletedTask;

        private void ShowRaceCard()
        {
            _raceCard = true;
            var title = new TextBlock { Text = Loc.Get("rt_engine_intro"), FontSize = 20, TextWrapping = TextWrapping.Wrap };
            RaceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
            RaceDownload = new Button { Content = Loc.Get("rt_engine_download") };
            RaceCancel = new Button { Content = Loc.Get("rt_engine_cancel") };
            RaceSlower = new Button { Content = Loc.Get("rt_engine_slower") };
            RaceBrowser = new Button { Content = Loc.Get("rt_engine_browser") };
            RaceDownload.Click += (_, _) => RaceFetchPending = DownloadRaceEngine();
            RaceCancel.Click += (_, _) => { if (_raceFetch != null) { CancelRaceFetch(); RaceStatus.Text = ""; } else Close(); };
            RaceSlower.Click += (_, _) => PlayRaceSlower();
            RaceBrowser.Click += (_, _) =>
            {
                CancelRaceFetch();
                RaceDownload.IsEnabled = _raceFetch == null;
                bool opened;
                try { opened = OpenRaceBrowser(RaceBrowserUrl); } catch { opened = false; }
                if (opened) Close();
                else RaceStatus.Text = Loc.Get("rt_engine_browser_failed");
            };
            var buttons = new WrapPanel { Children = { RaceDownload, RaceCancel } };
            var card = new StackPanel { Spacing = 14, MaxWidth = 560, Children =
            {
                title, RaceStatus, buttons, RaceSlower, RaceBrowser,
                new TextBlock { Text = Loc.Get("rt_engine_browser_warning"), TextWrapping = TextWrapping.Wrap },
            } };
            Content = new Border { Padding = new Thickness(28), Child = card, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            try { if (RaceEngineFetch?.IsInstalled == true) RaceStatus.Text = Loc.Get("rt_engine_start_failed"); }
            catch { RaceStatus.Text = Loc.Get("rt_engine_unavailable"); }
        }

        private async Task DownloadRaceEngine()
        {
            if (_raceFetch != null || _closing) return;
            var fetch = RaceEngineFetch;
            if (fetch == null) { RaceStatus.Text = Loc.Get("rt_engine_unavailable"); return; }
            var cancel = new CancellationTokenSource();
            _raceFetch = cancel;
            RaceDownload.IsEnabled = false;
            RaceStatus.Text = Loc.GetF("rt_engine_progress", 0);
            var progress = new Progress<double>(value =>
            {
                if (_raceFetch == cancel && !cancel.IsCancellationRequested && !_closing)
                    RaceStatus.Text = Loc.GetF("rt_engine_progress", (int)Math.Clamp(value, 0, 100));
            });
            try
            {
                var result = await fetch.FetchAsync(progress, cancel.Token);
                if (_raceFetch != cancel || cancel.IsCancellationRequested || _closing) return;
                RaceStatus.Text = Loc.Get(result switch
                {
                    RaceEngineFetchResult.Installed => "rt_engine_start_failed", // owned host is a separate lane
                    RaceEngineFetchResult.Network => "rt_engine_network",
                    RaceEngineFetchResult.Checksum => "rt_engine_checksum",
                    RaceEngineFetchResult.Storage => "rt_engine_storage",
                    RaceEngineFetchResult.Unsupported => "rt_engine_unsupported",
                    _ => "rt_engine_unavailable",
                });
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch { if (_raceFetch == cancel && !_closing) RaceStatus.Text = Loc.Get("rt_engine_failed"); }
            finally
            {
                if (_raceFetch == cancel) { _raceFetch = null; if (!_closing) RaceDownload.IsEnabled = true; }
                cancel.Dispose();
            }
        }

        private void CancelRaceFetch()
        {
            var cancel = _raceFetch;
            try { cancel?.Cancel(); } catch (AggregateException) { }
        }

        private void PlayRaceSlower()
        {
            CancelRaceFetch();
            _raceCard = false;
            Content = Web;
            OpenGlassroad();
            Load(Server());
        }
    }
}
