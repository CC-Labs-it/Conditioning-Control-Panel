using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// A HyperlinkButton whose NavigateUri opens through <see cref="ExternalOpener"/> (so a sandbox never opens a
    /// real site) instead of HyperlinkButton's own launcher call. Looks identical: it keeps HyperlinkButton's theme.
    /// </summary>
    public class SafeHyperlinkButton : HyperlinkButton
    {
        protected override Type StyleKeyOverride => typeof(HyperlinkButton);

        /// <summary>WPF BrowserLauncher.OpenUrlOrPrompt's purpose (Helpers/BrowserLauncher.cs:63-80). Set, a link the
        /// browser would not open is copied and explained through the shared MessageDialog.ShowLinkCopiedAsync.
        /// Unset, a failure stays silent as before.</summary>
        public string? FailurePurpose { get; set; }

        /// <summary>Test seam: the launcher (ExternalOpener.OpenAsync).</summary>
        internal Func<TopLevel?, string, Task<bool>> LaunchLink = ExternalOpener.OpenAsync;

        /// <summary>True when the sandbox (not the browser) refused the link: no clipboard write, no prompt.
        /// Test seam: a test sets it false to reach the WPF no-browser fallback.</summary>
        internal static Func<string, bool> Refused = url => !ExternalOpener.Allowed(url);

        protected override void OnClick()
        {
            // Hide the Uri from the base class for the duration of its OnClick: Click/Command still fire, but its
            // own launch sees nothing to open whatever order it runs in.
            var uri = NavigateUri;
            NavigateUri = null;
            try { base.OnClick(); }
            finally { NavigateUri = uri; }
            if (uri is not null) _ = OpenAsync(uri);
        }

        private async Task OpenAsync(Uri uri)
        {
            var top = TopLevel.GetTopLevel(this);
            var url = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;
            var owner = top as Window;
            var ownerClosed = false;
            void OnOwnerClosed(object? sender, EventArgs e) => ownerClosed = true;
            if (owner is not null) owner.Closed += OnOwnerClosed;
            try
            {
                // IsVisited as the base sets it: only once the link actually opened.
                if (await LaunchLink(top, url))
                {
                    SetCurrentValue(IsVisitedProperty, true);
                    return;
                }
                if (string.IsNullOrEmpty(FailurePurpose) || owner is null || ownerClosed || Refused(url)) return;
                await Views.Dialogs.MessageDialog.ShowLinkCopiedAsync(owner, url, FailurePurpose);
            }
            catch (Exception ex) { Serilog.Log.Debug("SafeHyperlinkButton: {Error}", ex.Message); }
            finally { if (owner is not null) owner.Closed -= OnOwnerClosed; }
        }
    }
}
