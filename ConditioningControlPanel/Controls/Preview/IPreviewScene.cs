using System;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview
{
    /// <summary>
    /// One effect's live preview, drawn over the desktop slice of an <see cref="EffectPreview"/>
    /// (Live Preview pilot, owner 2026-11-24). Coordinates are DIPs inside the screen: (0,0) is the
    /// screen's top-left corner, the taskbar strip at the bottom is not part of the playing field.
    /// A scene never reads the user's media, fires a real effect, writes a setting or plays sound.
    /// </summary>
    public interface IPreviewScene : IDisposable
    {
        /// <summary>The AppSettings property names this scene shows; a change to one of them wakes the preview.</summary>
        string[] Settings { get; }

        /// <summary>The screen size and the band of it that is on screen right now (screen DIPs). Called
        /// before <see cref="Start"/> and again whenever the layout or the page scroll moves it.</summary>
        void Layout(SKRect screen, SKRect visible);

        /// <summary>The page was opened: the first burst comes soon.</summary>
        void Start();

        /// <summary>Advance the scene by <paramref name="dt"/> seconds.</summary>
        void Tick(double dt);

        /// <summary>Draw the scene. The canvas is in screen DIPs and already clipped to the screen.</summary>
        void Render(SKCanvas canvas, SKRect screen, SKRect visible);

        /// <summary>A watched setting changed (one of <see cref="Settings"/>).</summary>
        void SettingChanged(string name);

        /// <summary>A click at <paramref name="at"/> (screen DIPs).</summary>
        void Click(SKPoint at);
    }
}
