// B2 section 4: Linux first race card and the owned shell launch (LinuxRaceHost). The fetch arrives in a separate lane.
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
                RaceDownload.IsEnabled = _raceFetch == null && RaceEngineFetch != null;
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
            if (RaceEngineFetch == null)
            {
                // No engine fetch in this build (the owned Electron host is not in it yet): say so before
                // anyone presses Download, and leave the two labelled fallbacks.
                RaceDownload.IsEnabled = false;
                RaceStatus.Text = Loc.Get("rt_engine_unavailable");
                return;
            }
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

        /// <summary>B2 (#3702) step 3: the race runs in the owned Electron shell (LinuxRaceHost). This window is never
        /// shown and keeps the whole Glassroad session; Post and the page frames travel by the shell's pipe.</summary>
        internal LinuxRaceHost? RaceShell { get; private set; }
        internal static Func<GameWindow, Uri, LinuxRaceHost?> LaunchRaceShell = LinuxRaceHost.TryLaunch;
        private string? _shellRoom;
        /// <summary>True = the shell runs; null = no engine or shell installed; false = installed and did not start.</summary>
        private bool? StartRaceShell(string? room)
        {
            var url = new Uri(Server().Url(Spec.Page, RaceQuery(Spec, room)));
            LinuxRaceHost? shell;
            try { shell = LaunchRaceShell(this, url); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Glassroad: race shell did not start"); return false; }
            if (shell == null) return null;
            (RaceShell, PageUrl, _shellRoom) = (shell, url, room);
            Closed += (_, _) => shell.Dispose();
            lock (Open) Open.Add(this);   // what Opened does for a shown window
            OpenGlassroad();
            return true;
        }
        /// <summary>The shell died before loading a page: close this session and show the card saying so.</summary>
        internal void RaceShellFailed()
        {
            Close();
            var card = new GameWindow(Spec);
            card.ShowRaceCard(_shellRoom, failed: true);
            card.Show();
        }
        private void ShowRaceCard(string? room, bool failed)
        {
            ShowRaceCard();
            if (failed) RaceStatus.Text = Loc.Get("rt_engine_start_failed");
            HandoffRaceRoom(room);   // joins once the in-app page is ready (Play race slower)
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
