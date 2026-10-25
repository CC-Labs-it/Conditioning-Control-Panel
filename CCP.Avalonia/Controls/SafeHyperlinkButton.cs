using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
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

        /// <summary>Opt-in WPF BrowserLauncher fallback for links whose failure needs an explanation.</summary>
        public string? FailurePurpose { get; set; }
        private static readonly HashSet<Window> Prompts = new();
        internal static void CloseBrowserPrompts()
        {
            foreach (var prompt in Prompts.ToArray()) prompt.Close();
        }

        private async Task OpenAsync(Uri uri)
        {
            // IsVisited as the base sets it: only once the link actually opened.
            if (await ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString))
                SetCurrentValue(IsVisitedProperty, true);
            else if (FailurePurpose is { } purpose && TopLevel.GetTopLevel(this) is Window owner)
            {
                var url = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;
                try { if (owner.Clipboard is { } clipboard) await clipboard.SetTextAsync(url); } catch { }
                var prompt = new Views.Dialogs.MessageDialog(
                    ConditioningControlPanel.Localization.Loc.Get("title_open_link_in_browser"),
                    ConditioningControlPanel.Localization.Loc.GetF("msg_browser_no_default_for", purpose)
                    + ConditioningControlPanel.Localization.Loc.GetF("msg_browser_link_copied", url), showCancel: false);
                Prompts.Add(prompt);
                try { await prompt.ShowDialogSafe<bool?>(owner); }
                finally { Prompts.Remove(prompt); }
            }
        }
    }
}
