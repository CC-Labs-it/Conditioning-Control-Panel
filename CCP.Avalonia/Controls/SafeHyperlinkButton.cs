using Avalonia.Input.Platform;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Opt-in WPF Helpers/BrowserLauncher.cs:63-77 fallback for links whose failure needs an explanation.</summary>
        public bool ExplainFailure { get; set; }
        public string? FailurePurpose { get; set; }
        private static int _browserGeneration;
        // Instance seams keep delayed launcher/clipboard tests off the real desktop.
        internal Func<TopLevel?, string, Task<bool>> LaunchLink = ExternalOpener.OpenAsync;
        internal Func<Window, string, Task> CopyLink = async (owner, url) =>
        {
            if (owner.Clipboard is { } clipboard) await clipboard.SetTextAsync(url);
        };
        private static readonly HashSet<Window> Prompts = new();
        /// <summary>True when the sandbox (not the browser) refused the link: no clipboard write, no prompt.
        /// Test seam: a test sets it false to reach the WPF no-browser fallback.</summary>
        internal static Func<string, bool> Refused = url => !ExternalOpener.Allowed(url);
        internal static void CloseBrowserPrompts()
        {
            // Invalidate requests suspended before they have a registered prompt, too.
            _browserGeneration++;
            foreach (var prompt in Prompts.ToArray()) prompt.Close();
        }

        private async Task OpenAsync(Uri uri)
        {
            var top = TopLevel.GetTopLevel(this);
            var owner = top as Window;
            var url = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;
            var generation = _browserGeneration;
            var ownerClosed = false;
            void OnOwnerClosed(object? sender, EventArgs e) => ownerClosed = true;
            bool Valid() => !ownerClosed && generation == _browserGeneration;
            if (owner is not null) owner.Closed += OnOwnerClosed;
            try
            {
                var opened = await LaunchLink(top, url);
                if (!Valid()) return;
                if (opened)
                {
                    SetCurrentValue(IsVisitedProperty, true);
                    return;
                }
                if (!ExplainFailure || owner is null || Refused(url)) return;
                // WPF BrowserLauncher.cs:68-80 is synchronous. Recheck after each async boundary
                // so panic or owner closure cannot be followed by a late clipboard prompt.
                try { await CopyLink(owner, url); } catch { }
                if (!Valid()) return;
                var prompt = new Views.Dialogs.MessageDialog(
                    ConditioningControlPanel.Localization.Loc.Get("title_open_link_in_browser"),
                    (string.IsNullOrEmpty(FailurePurpose)
                        ? ConditioningControlPanel.Localization.Loc.Get("msg_browser_no_default")
                        : ConditioningControlPanel.Localization.Loc.GetF("msg_browser_no_default_for", FailurePurpose))
                    + ConditioningControlPanel.Localization.Loc.GetF("msg_browser_link_copied", url), showCancel: false);
                Prompts.Add(prompt);
                try { await prompt.ShowDialogSafe<bool?>(owner); }
                finally { Prompts.Remove(prompt); }
            }
            finally { if (owner is not null) owner.Closed -= OnOwnerClosed; }
        }
    }
}
