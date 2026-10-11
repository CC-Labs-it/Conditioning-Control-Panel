using System;
using System.Windows;

namespace ConditioningControlPanel.Services.Video.Floating
{
    /// <summary>
    /// One floating video window as the video service sees it: its frame, its motion and its sound.
    /// The service decides HOW MANY there are and WHICH ONE carries the sound; the frame decides how
    /// a window looks and moves.
    /// </summary>
    internal interface IFloatingVideoSurface
    {
        /// <summary>The player pressed or flung this window. Raised on the UI thread.</summary>
        event Action<IFloatingVideoSurface>? Touched;

        /// <summary>True lights the border: this window carries the sound.</summary>
        bool AudioLit { get; set; }

        /// <summary>Silences or restores this window's own sound. Never touches another window.</summary>
        void SetMuted(bool muted);

        /// <summary>Fling and bounce inside these bounds (device pixels, one monitor's work area).</summary>
        void StartMotion(Rect boundsPx);

        /// <summary>Stops moving and lets go of every hook. Safe to call twice, and on a closed window.</summary>
        void Detach();
    }

    /// <summary>
    /// Gives a floating video window its frame (border, rounded corners), its fling and bounce, and
    /// its audio light. Works on any top-level WPF window, so both video engines share it.
    /// </summary>
    internal static class FloatingVideoFrame
    {
        /// <param name="window">The floating video window, already created (shown or not).</param>
        /// <param name="mute">
        /// How to silence a window the frame cannot silence itself (the LibVLC window). Null when
        /// the window knows how (BrowserVideoWindow).
        /// </param>
        public static IFloatingVideoSurface Attach(Window window, Action<bool>? mute = null)
            => new Inert(mute);

        // Placeholder until the frame lane lands: no frame, no motion, mute passed through.
        private sealed class Inert : IFloatingVideoSurface
        {
            private readonly Action<bool>? _mute;
            public Inert(Action<bool>? mute) { _mute = mute; }
#pragma warning disable CS0067
            public event Action<IFloatingVideoSurface>? Touched;
#pragma warning restore CS0067
            public bool AudioLit { get; set; }
            public void SetMuted(bool muted) => _mute?.Invoke(muted);
            public void StartMotion(Rect boundsPx) { }
            public void Detach() { }
        }
    }
}
