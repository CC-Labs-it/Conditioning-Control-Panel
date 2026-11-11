using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Commands;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Launcher;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// CompanionBrain's CommandExecutor and ActivitiesProvider on this head (WPF App.xaml.cs:2767-2781).
    /// The gate is Core's AiCommandService - the same master switch + Lab entitlement, per-effect
    /// toggles, video-feature gate and 3-per-reply cap WPF runs. Only the drawing is here.
    /// An overlay surface also refuses where
    /// click-through windows are impossible (X11Overlay unavailable), as the overlays themselves do.
    /// </summary>
    internal static class CompanionEffects
    {
        internal static AiCommandService Commands { get; } = new();

        private static Window? Host =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        private static bool OnUi(Func<bool> f) => Dispatcher.UIThread.CheckAccess() ? f() : Dispatcher.UIThread.Invoke(f);

        /// <summary>A desktop-effect start, through MainShellWindow.StartEffect so the portal panic
        /// shortcut is bound first on native Wayland. A start deferred behind that bind is dropped if a
        /// panic or switch-off came meanwhile, and reported as requested; inline, it reports what showed.</summary>
        /// <summary>The effect-start door (MainShellWindow.StartEffect); a test holds starts here as the portal bind would.</summary>
        internal static Action<Action> StartEffect = MainShellWindow.StartEffect;

        internal static bool Start(Func<bool> show) => OnUi(() =>
        {
            var generation = AiCommandService.CancelGeneration;
            bool? shown = null;
            StartEffect(() =>
            {
                if (generation == AiCommandService.CancelGeneration && AiEffectControlGate.IsOnNow) shown = show();
                else shown = false;
            });
            return shown ?? true;
        });

        /// <summary>An overlay surface: refused without a host or click-through support.</summary>
        private static bool Overlay(Action<Window> show) => OnUi(() =>
            X11Overlay.IsAvailable && Host is not null && Start(() =>
            {
                if (Host is not { } host) return false;
                show(host);
                return true;
            }));

        internal static void Seed()
        {
            AiCommandService.LiveActionSink = App.AppendAiLiveAction;
            CompanionBrain.CommandExecutor = commands =>
            {
                Commands.BeginBatch();
                foreach (var command in commands) Commands.ExecuteCommand(command);
            };
            CompanionBrain.EffectScheduler = execute =>
            {
                if (Dispatcher.UIThread.CheckAccess()) execute();
                else Dispatcher.UIThread.Post(execute);
            };
            CompanionBrain.ActivitiesProvider = Activities;

            FlashImageCommand.Surface = (amount, durationMs, size) =>
                Overlay(host => FlashOverlay.TriggerOnce(host, amount, durationMs, size));
            BubbleCommand.Surface = (start, frequency) => start
                ? Overlay(host => BubbleOverlay.Start(host, frequency))
                : OnUi(() => { BubbleOverlay.Stop(); return true; });
            SubliminalCommand.Surface = (text, opacity) => Overlay(host =>
            {
                SubliminalOverlay.Show(host, text, opacity);
                CoreProgression.AddXP(10, "Subliminal");   // WPF FlashSubliminalCustom
            });
            BounceCommand.Surface = (on, words) => on
                ? Overlay(host => BouncingTextOverlay.Start(host, words))
                : OnUi(() => { BouncingTextOverlay.Stop(); return true; });
            MantraLockScreenCommand.Surface = (phrase, repeats) => Start(() =>
            {
                if (LockCardWindow.IsAnyOpen()) return false;   // never stacks (WPF), so nothing showed
                // Strict follows the user's own Lock Card switch, never forced (owner 2026-10-22).
                LockCardWindow.ShowOnAllMonitors(phrase, repeats, strictMode: CoreSettings.Current.LockCardStrict, isTest: false,
                    voiceMode: CoreSettings.Current.LockCardVoiceMode);
                return LockCardWindow.IsAnyOpen();
            });
            SpiralCommand.Surface = (on, opacity) => ApplySpiral(Host, on, opacity);
            PinkCommand.Surface = (on, opacity) => ApplyPink(Host, on, opacity);
            MediaCommand.VideoSurface = path => Start(() =>
                CoreEngine.Video?.Trigger(path == null ? null : false, path) == true);
            MediaCommand.AudioSurface = PlayAudio;
            GetBackToMeCommand.AiProvider = () => App.Ai;
            GetBackToMeCommand.SaySurface = (text, aiGenerated) =>
                Views.AvatarTube.AvatarTubeWindow.Live?.RunOnAvatar(() =>
                    Views.AvatarTube.AvatarTubeWindow.Live?.GigglePriority(text, playSound: true, aiGenerated: aiGenerated));
        }

        internal static bool ApplyPink(Window? host, bool on, int? opacity) => Start(() =>
        {
            if (host is null) return false;
            var s = CoreSettings.Current;
            var (oldOpacity, oldEnabled) = (s.PinkFilterOpacity, s.PinkFilterEnabled);
            if (opacity is int value) s.PinkFilterOpacity = value;
            s.PinkFilterEnabled = on;
            if (!PinkFilterOverlay.StartForAi(host))
            {
                (s.PinkFilterOpacity, s.PinkFilterEnabled) = (oldOpacity, oldEnabled);
                PinkFilterOverlay.Refresh(host);
                return false;
            }
            SpiralOverlay.StartForAi(host);
            CoreSettings.Save();
            return true;
        });

        /// <summary>WPF App.AiEffects.cs:63,128-150: set, start the overlay even with the engine off,
        /// refresh and save. Refuse an unsafe or absent host before changing the user's settings.</summary>
        internal static bool ApplySpiral(Window? host, bool on, int? opacity) => OnUi(() =>
        {
            var generation = MainShellWindow.OverlayGeneration;
            return Start(() =>
            {
                if (generation != MainShellWindow.OverlayGeneration || host is null
                    || (on && (!SpiralOverlay.CanStartForAi(host) || !SpiralOverlay.Preflight(host)))) return false;
                var s = CoreSettings.Current;
                var (oldEnabled, oldOpacity) = (s.SpiralEnabled, s.SpiralOpacity);
                if (opacity is int value) s.SpiralOpacity = value;
                s.SpiralEnabled = on;
                if (!SpiralOverlay.StartForAi(host))
                {
                    (s.SpiralEnabled, s.SpiralOpacity) = (oldEnabled, oldOpacity);
                    SpiralOverlay.Refresh(host);
                    return false;
                }
                PinkFilterOverlay.Refresh(host);   // WPF OverlayService.Start/RefreshOverlays
                CoreSettings.Save();
                return true;   // GIF decode and portal binding may still be in flight
            });
        });

        /// <summary>True once the clip is really playing, false when refused or it never starts.</summary>
        internal static async Task<bool> PlayAudio(string path)
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CoreAudio.PlayOneShot(path, 1f, "ai", _ => started.TrySetResult(true), () => started.TrySetResult(false));
            return await Task.WhenAny(started.Task, Task.Delay(10_000)) == started.Task && started.Task.Result;
        }

        /// <summary>WPF CompanionActivities.Current: the launcher games this head can open, then the pages.</summary>
        internal static IReadOnlyList<CompanionActivity> Activities()
        {
            var result = new List<CompanionActivity>();
            foreach (var card in LauncherWindow.VisibleCards)
            {
                var dest = LauncherWindow.Destinations[card.Id];
                result.Add(new("game." + card.Id, Loc.Get(card.TitleKey), Loc.Get(card.BlurbKey),
                    () => CompanionPages.CanOfferGame(true, dest.Locked(), true,
                        card.RequiresAccount && !CoreAccount.IsLoggedIn, CoreSettings.Current.AudioOnlySession),
                    () => OnUi(() =>
                    {
                        // A game opens its host through the launcher's Play (leash gate first), not the panel.
                        if (Views.Games.GameWindow.Games.ContainsKey(card.Id))
                            return MainShellWindow.Current is { } shell && LauncherWindow.LaunchGame(shell, card.Id);
                        if (LauncherWindow.Instance is { } launcher) { launcher.OpenPanel(dest.Open); return true; }
                        if (MainShellWindow.Current is not { } panel) return false;
                        panel.ShowFromTray();
                        dest.Open(panel);
                        return true;
                    })));
            }
            result.AddRange(CompanionPages.Current(() => MainShellWindow.Current != null,
                entry => OnUi(() =>
                {
                    if (MainShellWindow.Current is not { } shell) return false;
                    SettingsPaletteWindow.Navigate(shell, entry);
                    return true;
                })));
            return result;
        }
    }
}
