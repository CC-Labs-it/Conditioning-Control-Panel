using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private bool _browserCrashed;

        // WPF MainWindow.Browser.cs:177-195. Keep the native view hidden behind the prompt.
        private void OnBrowserCrash()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(OnBrowserCrash);
                return;
            }
            _browserCrashed = true;
            SetBrowserFailure("label_disconnected");
            SyncBrowserPrompt();
        }

        internal void ClearBrowserCrash()
        {
            if (!_browserCrashed) return;
            _browserCrashed = false;
            SyncBrowserPrompt();
        }
    }
}
