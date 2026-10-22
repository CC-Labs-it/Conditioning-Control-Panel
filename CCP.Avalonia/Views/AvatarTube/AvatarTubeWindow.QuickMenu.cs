// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.ChatInput.cs: the right-click menu's
// handlers and state - MenuItemEngine_Click :1053 (#479 guards), MenuItemTriggerMode_Click :1095,
// MenuItemBambiTakeover_Click :1113, MenuItemTalkToBambi_Click :1178, MenuItemMute_Click :1324,
// MenuItemMuteWhispers_Click :1343, MenuItemDismiss_Click :400, UpdateQuickMenuState :1386 - and the
// input-panel close rules Window_PreviewMouseDown :358 / ImgAvatar_MouseRightButtonDown :180.
// Detach/Attach/Shrink/Grow sit in Windowing.cs, the personality submenu in ContentGates.cs.
//
// Every action goes through the shell's own entry (StartEngine/StopEngine, SetAutonomyEnabled), so
// whatever the tube starts is what panic already stops (TubeQuickMenuTests proves it).
//
// ponytail: not ported - Pause browser (WPF mutes CoreWebView2 + pauses its media; this head's
// browser has no mute/script channel, so the item is hidden rather than promising a pause), the
// remote-controller emote swap (RefreshEmoteMenuItemsForRemoteState :878; the five items are greyed
// as UpdateQuickMenuState does, the emote items stay hidden), and the tube's trigger-phrase timer
// (Speech.cs:1594 StartTriggerTimer): Trigger mode flips and saves the setting WPF's timer reads.

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private static readonly IBrush MenuRed = new SolidColorBrush(Color.FromRgb(255, 99, 71));
        private static readonly IBrush MenuGreen = new SolidColorBrush(Color.FromRgb(144, 238, 144));
        private static readonly IBrush MenuPatreon = new SolidColorBrush(Color.FromRgb(155, 89, 182));
        private static readonly IBrush MenuLocked = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x70));

        private MenuItem? QuickItem(string name) => this.FindControl<MenuItem>(name);

        /// <summary>WPF's `_parentWindow is MainWindow`.</summary>
        private MainShellWindow? QuickShell => _parentWindow as MainShellWindow;

        private static bool RemoteControllerConnected =>
            RemoteControlTabView.Relay.IsValueCreated && RemoteControlTabView.Relay.Value.ControllerConnected;

        private void WireQuickMenu()
        {
            Wire("MenuItemTalkToBambi", OpenChatInput);
            Wire("MenuItemEngine", OnMenuEngine);
            Wire("MenuItemTriggerMode", OnMenuTriggerMode);
            Wire("MenuItemBambiTakeover", OnMenuTakeover);
            Wire("MenuItemMute", OnMenuMute);
            Wire("MenuItemMuteWhispers", OnMenuMuteWhispers);
            Wire("MenuItemShrink", () => StepScale(-ScaleStep));
            Wire("MenuItemGrow", () => StepScale(+ScaleStep));
            Wire("MenuItemDismiss", OnMenuDismiss);
            if (QuickItem("MenuItemPauseBrowser") is { } pause) pause.IsVisible = false;
            // WPF Window_PreviewMouseDown: tunnel, so a child that handles the press still closes the box.
            AddHandler(PointerPressedEvent, OnTubePreviewPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        private void Wire(string name, Action handler)
        {
            if (QuickItem(name) is not { } item) { Log.Warning("AvatarTubeWindow: {Item} not found", name); return; }
            item.Click += (_, _) =>
            {
                try { handler(); }
                catch (Exception ex) { Log.Warning(ex, "AvatarTubeWindow: {Item} click failed", name); }
            };
        }

        /// <summary>WPF Window_PreviewMouseDown + ImgAvatar_MouseRightButtonDown: any press outside the
        /// input panel (a right-click on her included) closes it, and counts as user activity.</summary>
        private void OnTubePreviewPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            QuickShell?.Autonomy.ReportUserActivity();
            if (!_inputPanel.IsVisible) return;
            if (e.Source is global::Avalonia.Visual v && (ReferenceEquals(v, _inputPanel) || _inputPanel.IsVisualAncestorOf(v))) return;
            _inputPanel.IsVisible = false;   // WPF HideInputPanel
        }

        /// <summary>WPF MenuItemEngine_Click: no-op under a remote controller; a Stop while locked
        /// (#479: Lockdown, strict video, strict bubble count) is refused and counted as an escape.</summary>
        internal void OnMenuEngine()
        {
            if (QuickShell is not { } shell || RemoteControllerConnected) return;
            if (CoreEngine.IsRunning)
            {
                if (MainShellWindow.VoiceStopLocked())
                {
                    try { LockdownService.Current?.NotifyEscapeAttempt(EscapeKinds.Stop); } catch { }
                    Giggle("nuh-uh~ no stopping now, you're locked in~");
                    return;
                }
                MainShellWindow.StopEngine();
                Giggle("Engine stopped~");
            }
            else
            {
                shell.StartEngine();
                Giggle("Engine started! *giggles*");
            }
            UpdateQuickMenuState();
        }

        /// <summary>WPF MenuItemTriggerMode_Click (SyncTriggerModeUI = the shell's hero sync).</summary>
        internal void OnMenuTriggerMode()
        {
            var on = !CoreSettings.Current.TriggerModeEnabled;
            CoreSettings.Current.TriggerModeEnabled = on;
            CoreSettings.Save();
            UpdateQuickMenuState();
            QuickShell?.SyncHero();
            Giggle(on ? "Trigger mode ON~" : "Trigger mode off~");
        }

        /// <summary>WPF MenuItemBambiTakeover_Click: premium or the free day (#978); a RUNNING
        /// Takeover is not switched off under Lockdown (#514); choosing it here is the consent.</summary>
        internal void OnMenuTakeover()
        {
            var s = CoreSettings.Current;
            if (!AutonomyScheduler.HasEntitlement) { Giggle("This is Patreon only~"); return; }
            if (QuickShell is not { } shell) return;
            if (s.AutonomyModeEnabled && shell.Autonomy.IsEnabled && MainShellWindow.LockdownActive)
            {
                Giggle("Lockdown is active~ you can't turn me off~");
                return;
            }
            if (!s.AutonomyConsentGiven) s.AutonomyConsentGiven = true;
            var on = !s.AutonomyModeEnabled;
            shell.SetAutonomyEnabled(on);   // saves, starts/stops her, syncs the shell's switch
            Giggle(on ? CoreMods.Service?.GetAutonomyOnPhrase() ?? "Bambi takes over~ *giggles*" : "Takeover mode off~");
            UpdateQuickMenuState();
        }

        /// <summary>WPF MenuItemMute_Click: her voice setting (read live by <see cref="IsMuted"/>).</summary>
        internal void OnMenuMute()
        {
            CoreSettings.Current.AvatarMuted = !CoreSettings.Current.AvatarMuted;
            CoreSettings.Save();
            UpdateQuickMenuState();
            QuickShell?.SyncHero();   // WPF SyncQuickControlsUI(muteAvatar)
        }

        /// <summary>WPF MenuItemMuteWhispers_Click: the dedicated mute, never SubAudioEnabled.</summary>
        internal void OnMenuMuteWhispers()
        {
            CoreSettings.Current.SubAudioMuted = !CoreSettings.Current.SubAudioMuted;
            CoreSettings.Save();
            UpdateQuickMenuState();
        }

        /// <summary>WPF MenuItemDismiss_Click: the decision is saved first (#888), she re-attaches
        /// and hides, and the Companion room re-reads the switch.</summary>
        internal void OnMenuDismiss()
        {
            Log.Information("User dismissed avatar - hiding and reattaching");
            CoreSettings.Current.AvatarEnabled = false;
            CoreSettings.Save();
            if (!_isAttached) Attach();
            if (QuickShell is { } shell) { shell.HideAvatarTube(); shell.SyncHero(); }
            else RunOnAvatar(Hide);
        }

        private static IconLabel MenuLabel(string key, params object[] args) => IconLabel.ForKey(key, args);

        /// <summary>WPF UpdateQuickMenuState: headers, colours and enabled state; on Opened and after each click.</summary>
        internal void UpdateQuickMenuState()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(UpdateQuickMenuState); return; }
            var s = CoreSettings.Current;
            var mods = CoreMods.Service;

            if (QuickItem("MenuItemTalkToBambi") is { } talk)
            {
                var label = mods?.GetTalkToLabel() ?? Loc.Get("menu_talk_to_companion");
                talk.IsEnabled = CoreAi.IsAvailable;
                talk.Header = MenuLabel(talk.IsEnabled ? "menu_talk_to_format" : "menu_talk_to_locked_format", label);
                talk.Foreground = talk.IsEnabled ? AccentBrush : MenuPatreon;
            }

            var running = CoreEngine.IsRunning;
            var engine = QuickItem("MenuItemEngine");
            if (engine != null)
            {
                engine.Header = MenuLabel(running ? "menu_stop_engine" : "menu_start_engine");
                engine.Foreground = running ? MenuRed : MenuGreen;
            }
            var trigger = QuickItem("MenuItemTriggerMode");
            if (trigger != null)
            {
                trigger.Header = MenuLabel(s.TriggerModeEnabled ? "menu_trigger_mode_on" : "menu_trigger_mode_off");
                trigger.Foreground = s.TriggerModeEnabled ? MenuGreen : Brushes.White;
            }
            var takeoverAvailable = AutonomyScheduler.HasEntitlement;
            var takeover = QuickItem("MenuItemBambiTakeover");
            if (takeover != null)
            {
                var name = mods?.GetTakeoverLabel() ?? Loc.Get("menu_takeover");
                takeover.Header = MenuLabel(!takeoverAvailable ? "menu_takeover_locked_format"
                    : s.AutonomyModeEnabled ? "menu_takeover_on_format" : "menu_takeover_off_format", name);
                takeover.Foreground = !takeoverAvailable ? MenuPatreon : s.AutonomyModeEnabled ? AccentBrush : Brushes.White;
            }
            var mute = QuickItem("MenuItemMute");
            if (mute != null)
            {
                mute.Header = MenuLabel(s.AvatarMuted ? "menu_mute_avatar_on" : "menu_mute_avatar_off");
                mute.Foreground = s.AvatarMuted ? MenuRed : Brushes.White;
            }
            var whispers = QuickItem("MenuItemMuteWhispers");
            if (whispers != null)
            {
                whispers.Header = MenuLabel(s.SubAudioMuted ? "menu_mute_whispers_on" : "menu_mute_whispers_off");
                whispers.Foreground = s.SubAudioMuted ? MenuRed : Brushes.White;
            }

            if (RemoteControllerConnected)
            {
                if (engine != null) engine.Header = MenuLabel("label_start_engine");
                foreach (var item in new[] { engine, trigger, takeover, mute, whispers })
                    if (item != null) { item.IsEnabled = false; item.Foreground = MenuLocked; }
                return;   // the personality root is locked by PopulatePersonalityMenu
            }
            if (engine != null) engine.IsEnabled = !(running && MainShellWindow.VoiceStopLocked());
            if (trigger != null) trigger.IsEnabled = true;
            if (takeover != null) takeover.IsEnabled = takeoverAvailable;
            if (mute != null) mute.IsEnabled = true;
            if (whispers != null) whispers.IsEnabled = true;
        }
    }
}
