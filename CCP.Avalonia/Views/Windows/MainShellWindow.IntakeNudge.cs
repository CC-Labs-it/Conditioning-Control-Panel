using System;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private bool _serverAnnouncementShownThisLaunch;
    private bool _intakeNudgeArmed;
    private ITimer? _intakeNudgeTimer;

    private void InitializeIntakeNudge()
    {
        Opened += (_, _) =>
        {
            if (_intakeNudgeArmed || CoreSettings.Service == null) return;
            _intakeNudgeArmed = true;
            // WPF Marquee: deliberately last, fourteen seconds after opening (news checks at seven).
            _intakeNudgeTimer = Platform.StartupLadder.Time.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
            {
                if (!_windowClosed) CheckIntakePassNudge();
            }), null, TimeSpan.FromSeconds(14), Timeout.InfiniteTimeSpan);
        };
        Closed += (_, _) => _intakeNudgeTimer?.Dispose();
    }

    internal void CheckIntakePassNudge()
    {
        var s = CoreSettings.Current;
        if (!s.IntakeNudgeEnabled || !App.IntakePass.IsPassAvailable) return;
        var week = IntakePassService.CurrentWeekKey();
        if (s.IntakeNudgeDismissedWeek == week || _serverAnnouncementShownThisLaunch) return;
        var world = Platform.StartupLadder.World(DateTime.UtcNow);
        // WPF skips a session or any web host outright; first-launch/tour/modal quiet still files a row.
        if (world.SessionRunning || world.GameHostUp || Views.Games.GameWindow.IsAnyOpen()) return;
        _serverAnnouncementShownThisLaunch = true;
        var title = InboxFlyout.Str("intake_nudge_title", "Your weekly intake pass is ready");
        var action = InboxFlyout.Str("intake_nudge_action", "Start my intake");
        void RecordDismissed()
        {
            CoreSettings.Current.IntakeNudgeDismissedWeek = week;
            CoreSettings.Save();
        }
        Platform.StartupLadder.PresentOrInbox(new InboxItem
        {
            Key = "intake-pass:" + week, Glyph = "🎟", Title = title, Summary = action,
            Dismiss = RecordDismissed,
            Open = () => new AnnouncementPopup("intake-pass-" + week, title,
                InboxFlyout.Str("intake_nudge_pitch_nopunch",
                    "Your free Graded Intake just unlocked. It interviews you and drafts a session tuned to you."),
                imageUrl: null, onDismiss: RecordDismissed,
                // ponytail: IntakeNiche.PassCardImage is unported; WPF's missing-art text layout applies.
                actionText: action, onAction: () =>
                {
                    ShowTab("gradedintake");
                    Named<Tabs.GradedIntakeTabView>("GradedIntakeTab")?.BtnStartIntake_Click(this, new());
                }, dismissText: InboxFlyout.Str("intake_nudge_later", "Maybe later")).Show(this),
        });
    }
}
