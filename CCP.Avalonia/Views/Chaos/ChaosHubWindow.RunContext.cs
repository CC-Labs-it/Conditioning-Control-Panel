using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    public partial class ChaosHubWindow
    {
        // WPF ChaosHubWindow.xaml.cs:43-54, 1716-1763, 1786-1793, 2934-2939.
        internal static Action<bool> SetCompanionActive = active => AvatarTubeWindow.Live?.SetChaosRunActive(active);
        internal static bool IsStoryModeEnabled => StoryModeEnabled;
        private bool _runContext, _fallingIn, _companionHooksReady;

        private void EnsureCompanionHooks()
        {
            if (_companionHooksReady) return;
            _companionHooksReady = true;
            Closed += (_, _) => { if (!_fallingIn) SetCompanionActive(false); };
            Part<Button>("BtnBackToMenu").Click += (_, _) => LeaveRunContext();
        }

        private void EnterRunContext()
        {
            EnsureCompanionHooks();
            if (_runContext) return;
            _runContext = true;
            SetCompanionActive(true);
        }

        private void LeaveRunContext()
        {
            if (!_runContext) return;
            _runContext = false;
            if (!_fallingIn) SetCompanionActive(false);
        }

        private void BeginCompanionHandoff()
        {
            EnsureCompanionHooks();
            SetCompanionActive(true);
        }

        private void UpdateCompanionForHubState(WindowState state)
        {
            EnsureCompanionHooks();
            if (state == WindowState.Maximized) SetCompanionActive(true);
            else if (!_runContext && !_fallingIn) SetCompanionActive(false);
        }
    }
}
