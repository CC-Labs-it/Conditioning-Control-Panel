// PORTED from WPF MainWindow/MainWindow.RemoteControl.cs:685-738 (UpdateDirectoryListingStatus), after
// parity/trunk d7a46b6e4: the title-bar pill that says whether a running remote session is listed.
using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal readonly record struct DirectoryPill(bool Visible, string TextKey, string DotHex, string TipKey)
        {
            internal string Text => Loc.Get(TextKey);
            internal string Tip => Loc.Get(TipKey);
        }

        /// <summary>WPF's four states: hidden, private, listed and claimed.</summary>
        internal static DirectoryPill DirectoryPillFor(bool active, bool optedIn, bool claimed) =>
            !active ? new DirectoryPill(false, "", "#8A8AA0", "")
            : !optedIn ? new DirectoryPill(true, "remote_directory_private", "#8A8AA0", "remote_directory_private_tip")
            : claimed ? new DirectoryPill(true, "remote_directory_claimed", "#00FF88", "remote_directory_claimed_tip")
            : new DirectoryPill(true, "remote_directory_listed", "#B47BFF", "remote_directory_listed_tip");

        // Bind each state to the current language. Dispose the old bindings when the state changes.
        internal sealed class DirectoryPillView : IDisposable
        {
            private IDisposable? _textBinding, _tipBinding;

            internal void Update(Border pill, TextBlock? text, Ellipse? dot, bool active, bool optedIn, bool claimed)
            {
                var state = DirectoryPillFor(active, optedIn, claimed);
                Dispose();
                pill.IsVisible = state.Visible;
                if (!state.Visible) return;
                if (text != null) _textBinding = text.Bind(TextBlock.TextProperty, Localized(state.TextKey));
                if (dot != null) dot.Fill = new SolidColorBrush(Color.Parse(state.DotHex));
                _tipBinding = pill.Bind(ToolTip.TipProperty, Localized(state.TipKey));
            }

            private static Binding Localized(string key) => new($"[{key}]") { Source = LocalizationManager.Instance };

            public void Dispose()
            {
                _textBinding?.Dispose();
                _tipBinding?.Dispose();
                _textBinding = _tipBinding = null;
            }
        }

        private readonly DirectoryPillView _directoryPillView = new();

        /// <summary>The Remote tab calls this whenever its session, its listing or its controller changes.</summary>
        internal void UpdateDirectoryListingStatus(bool active, bool optedIn, bool claimed)
        {
            if (Named<Border>("DirectoryStatusPill") is not { } pill) return;
            _directoryPillView.Update(pill, Named<TextBlock>("TxtDirectoryStatus"),
                Named<Ellipse>("DirectoryStatusDot"), active, optedIn, claimed);
        }
    }
}
