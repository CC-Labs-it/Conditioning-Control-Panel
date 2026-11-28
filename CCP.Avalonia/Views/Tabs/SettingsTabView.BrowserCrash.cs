using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private bool _browserCrashed;
        private bool _browserStartupFailed;

        private void OnBrowserStartupFailed()
        {
            _browserCrashed = true;
            _browserStartupFailed = true;
            SetBrowserFailure("label_error_2");
            SyncBrowserPrompt();
        }

        // WPF MainWindow.Browser.cs:177-195. Keep the native view hidden behind the prompt.
        private void OnBrowserCrash()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(OnBrowserCrash);
                return;
            }
            _browserCrashed = true;
            _browserStartupFailed = false;
            SetBrowserFailure("label_disconnected");
            SyncBrowserPrompt();
        }

        internal bool PrepareBrowserNavigation()
        {
            if (!_browserCrashed) return true;
            if (!BrowserWebHost.RecreateNativeView()) return false;
            _browserCrashed = false;
            _browserStartupFailed = false;
            SyncBrowserPrompt();
            return true;
        }
    }
}
