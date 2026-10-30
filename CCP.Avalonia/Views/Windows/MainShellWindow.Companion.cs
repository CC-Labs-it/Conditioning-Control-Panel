// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Companion.cs (312 lines) - the
// avatar tube's window management plus the XP-drain flash, sorted member by member.
//
// WHAT IS REAL HERE. The tube itself is on this head
// (CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow), so the four members that only ever open,
// close, pose and wake it are real ports; so is FlashOverlay, whose WPF DoubleAnimation with
// AutoReverse becomes one Avalonia keyframe Animation of the same total 500ms.
//
// THE OWN-THREAD BRANCH IS GONE, not stubbed. WPF ran the tube on its own STA thread behind
// AppSettings.AvatarOwnThread and pumped a second Dispatcher; Avalonia has ONE UI thread per
// process, and the ported tube says so in its own header - RunOnAvatar marshals to
// Dispatcher.UIThread and the setting has no meaning on this head. There is nothing to port,
// so InitializeAvatarTube is the else-branch alone.
//
// XP events now originate in CompanionCore and are released when the shell closes. The existing
// live mute setting, speech pipeline and quick menu are reused; no second mute flag is introduced.

using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The companion's tube, or null before it has ever been opened. See the header:
        /// this is the one declaration of the field for the whole partial class.</summary>
        private AvatarTubeWindow? _avatarTubeWindow;

        /// <summary>WPF MainWindow.BuildingHiddenForBoot: set while a launcher boot shows the panel only so
        /// its Opened work runs (App.axaml.cs). Nobody sees the panel then, so the tube stays put.</summary>
        internal bool BuildingHiddenForBoot { get; set; }

        private bool _companionEventsClosed;
        private Action? _stopCompanionLevelSound;

        /// <summary>WPF MainWindow.Companion.cs:35-140 event handlers; a closed shell must never receive another reward.</summary>
        private void HookCompanionEvents()
        {
            CompanionCore.XPAwarded += OnCompanionXPAwarded;
            CompanionCore.LevelUp += OnCompanionLevelUp;
            CompanionCore.XPDrained += OnCompanionXPDrained;
            CompanionCore.Switched += OnCompanionSwitched;
            Closed += (_, _) =>
            {
                _companionEventsClosed = true;
                CompanionCore.XPAwarded -= OnCompanionXPAwarded;
                CompanionCore.LevelUp -= OnCompanionLevelUp;
                CompanionCore.XPDrained -= OnCompanionXPDrained;
                CompanionCore.Switched -= OnCompanionSwitched;
                StopCompanionLevelSound();
            };
        }

        private void DispatchCompanionEvent(Action action)
        {
            void Apply() { if (!_companionEventsClosed) action(); }
            if (Dispatcher.UIThread.CheckAccess()) Apply();
            else Dispatcher.UIThread.Post(Apply);
        }

        private bool CompanionRoomVisible => Named<Control>("CompanionTab")?.IsVisible == true;

        private void RefreshCompanionCards()
        {
            SyncHero();
            RoomView()?.RefreshRoster();
        }

        // WPF MainWindow.Companion.cs:35-45: only ordinary awards require the room to be visible.
        private void OnCompanionXPAwarded(CompanionId id, double amount, double modifier) =>
            DispatchCompanionEvent(() => { if (CompanionRoomVisible) RefreshCompanionCards(); });

        private void OnCompanionLevelUp(CompanionId id, int level) => DispatchCompanionEvent(() =>
        {
            RefreshCompanionCards();
            var name = CoreMods.MakeModAware(CompanionDefinition.GetById(id).Name);
            if (CompanionCore.MilestoneNotification(name, level) is not { } notice) return;
            Platform.OsNotifications.Show(notice.Title, notice.Body);
            PlayCompanionLevelSound();
        });

        private void OnCompanionXPDrained(double amount) => DispatchCompanionEvent(() =>
        {
            UpdateLevelDisplay();
            FlashXpBarOnDrain();
            if (CompanionRoomVisible) RefreshCompanionCards();
        });

        private void OnCompanionSwitched(CompanionId id) => DispatchCompanionEvent(RefreshCompanionCards);

        /// <summary>WPF MainWindow/MainWindow.xaml.cs:784-831 PlayLevelUpSound: same fallbacks, curve and gain.</summary>
        private void PlayCompanionLevelSound()
        {
            try
            {
                var root = AppDomain.CurrentDomain.BaseDirectory;
                var path = new[]
                {
                    Path.Combine(root, "Resources", "sounds", "lvup.mp3"),
                    Path.Combine(root, "Resources", "lvlup.mp3"),
                    Path.Combine(root, "assets", "sounds", "lvlup.mp3"),
                }.FirstOrDefault(File.Exists);
                if (path == null) return;
                StopCompanionLevelSound();
                var volume = (float)Math.Pow(CoreSettings.Current.MasterVolume / 100f, 1.5) * 0.2625f;
                _stopCompanionLevelSound = CoreAudio.PlayStoppable(path, Math.Max(0.01f, volume), "level-up");
            }
            catch (Exception ex) { Log.Warning(ex, "Companion level-up sound failed"); }
        }

        internal void StopCompanionLevelSound()
        {
            var stop = _stopCompanionLevelSound;
            _stopCompanionLevelSound = null;
            try { stop?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "Companion level-up sound stop failed"); }
        }

        // ============================== the XP-drain flash ==============================

        /// <summary>
        /// Pulses the pink overlay on the header XP bar each time Brain Parasite drains. Fired
        /// whatever tab is visible - animating an off-screen Border is harmless.
        /// </summary>
        internal void FlashXpBarOnDrain()
        {
            Log.Information("XP drain flash fired");
            FlashOverlay(Named<Border>("XPBarFlashOverlay"));
            foreach (var hero in this.GetLogicalDescendants().OfType<CompanionHeroCard>())
                FlashOverlay(hero.FindControl<Border>("PrgCompanion0FlashOverlay"));
        }

        /// <summary>
        /// One 0 -> 1 -> 0 opacity pulse. WPF ran a 250ms DoubleAnimation with AutoReverse; the
        /// Avalonia twin is one 500ms keyframe animation with the peak at the halfway cue, which
        /// is the same curve and leaves the overlay back at its XAML opacity afterwards (FillMode
        /// None) rather than pinned by a local value.
        /// </summary>
        private static void FlashOverlay(Border? overlay)
        {
            if (overlay == null) return;
            var anim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(500),
                Easing = new QuadraticEaseOut(),
                FillMode = FillMode.None,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d),   Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(0.5d), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                    new KeyFrame { Cue = new Cue(1d),   Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                },
            };
            _ = anim.RunAsync(overlay);
        }

        // ============================== the avatar tube ==============================

        /// <summary>
        /// Builds the tube once. Never auto-shows a companion the user has dismissed (#888):
        /// callers that mean to show it anyway flip AvatarEnabled back on first, which is exactly
        /// what <see cref="WakeBambiUp"/> and the room's toggle do.
        /// </summary>
        private void InitializeAvatarTube()
        {
            if (_avatarTubeWindow != null)
            {
                Log.Warning("InitializeAvatarTube called but window already exists");
                return;
            }

            try
            {
                bool showNow = IsVisible && !BuildingHiddenForBoot
                               && WindowState != WindowState.Minimized
                               && CoreSettings.Current.AvatarEnabled;

                _avatarTubeWindow = new AvatarTubeWindow(this);
                PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) OnShellStateForTube(); };

                _avatarTubeWindow.UpdateQuickMenuState(); // IsMuted already reads the saved setting live.

                if (showNow)
                {
                    _avatarTubeWindow.Show();
                    _avatarTubeWindow.StartPoseAnimation();
                }

                Log.Information("Avatar Tube Window initialized");
            }
            catch (Exception ex)
            {
                Log.Error("Failed to initialize Avatar Tube Window: {Error}", ex.Message);
            }
        }

        /// <summary>Show her, unless she has been dismissed. Recreates the tube if it was closed.</summary>
        public void ShowAvatarTube()
        {
            if (!CoreSettings.Current.AvatarEnabled) return;

            if (_avatarTubeWindow == null)
            {
                InitializeAvatarTube();
                return;
            }

            // ShowSafe is the ported twin of WPF's ShowTube: it marshals to the UI thread and
            // shows. Z-order pairing is the tube's own business (see its OnOpened).
            _avatarTubeWindow.ShowSafe();
            _avatarTubeWindow.StartPoseAnimation();
        }

        private bool _avatarWasAttachedBeforeMaximize;
        private readonly Services.WindowMinimizeRecovery _windowMinimizeRecovery = new();

        /// <summary>WPF MainWindow.WindowChrome.cs:68-92,414-455: minimise hides an attached tube,
        /// maximise detaches it, restore shows it and re-attaches what maximise detached.</summary>
        private void OnShellStateForTube()
        {
            var tube = _avatarTubeWindow;
            if (tube == null) return;
            if (WindowState == WindowState.Minimized)
            {
                var recovery = _windowMinimizeRecovery.Minimized(!tube.IsDetached, Autonomy.IsEnabled,
                    LockdownActive, tube.IsMuted);
                if (recovery.PauseAutonomy) Autonomy.Stop();
                if (recovery.MuteAvatar) tube.SetWindowMute(true);
                HideAvatarTube();
                return;
            }
            if (WindowState == WindowState.Maximized && !tube.IsDetached)
            {
                _avatarWasAttachedBeforeMaximize = true;
                tube.Detach();
            }
            ShowAvatarTube();
            if (WindowState == WindowState.Normal && _avatarWasAttachedBeforeMaximize && tube.IsDetached)
            {
                tube.Attach();
                _avatarWasAttachedBeforeMaximize = false;
            }
            if (WindowState == WindowState.Normal)
            {
                var recovery = _windowMinimizeRecovery.Restored();
                if (recovery.ResumeAutonomy) Autonomy.Start();
                if (recovery.UnmuteAvatar) tube.SetWindowMute(false);
            }
        }

        /// <summary>Park her. Skipped for a DETACHED tube so a floating companion survives the
        /// shell minimising (WPF HideAvatarTube).</summary>
        public void HideAvatarTube()
        {
            if (_avatarTubeWindow == null) return;
            if (_avatarTubeWindow.IsDetached && CoreSettings.Current.AvatarEnabled) return;   // dismiss still hides
            _avatarTubeWindow.StopPoseAnimation();
            _avatarTubeWindow.RunOnAvatar(() => _avatarTubeWindow?.Hide());
        }

        /// <summary>
        /// The tray's "Wake Bambi Up!". Waking her is the opposite decision to Dismiss and has to
        /// survive a restart the same way (#888), so AvatarEnabled is written BEFORE the tube is
        /// built - creation and the speech gate both read it.
        /// </summary>
        public void WakeBambiUp()
        {
            try
            {
                if (!CoreSettings.Current.AvatarEnabled)
                {
                    CoreSettings.Current.AvatarEnabled = true;
                    CoreSettings.Save();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to persist avatar wake: {Error}", ex.Message);
            }

            if (_avatarTubeWindow == null) InitializeAvatarTube();
            if (_avatarTubeWindow != null)
            {
                _avatarTubeWindow.ShowSafe();
                _avatarTubeWindow.StartPoseAnimation();
                _avatarTubeWindow.Detach();
                _avatarTubeWindow.Giggle(ConditioningControlPanel.Localization.Loc.Get("companion_wake_greeting"));
            }
            SyncHero();   // WPF CompanionRoom?.SyncHero(): every on/off label follows the switch
        }

        /// <summary>Force a pose. Poses themselves load in the tube's Avatar.cs partial, which has
        /// not crossed, so on this head this cycles the placeholder set.</summary>
        public void SetAvatarPose(int poseNumber) => _avatarTubeWindow?.SetPose(poseNumber);
    }
}
