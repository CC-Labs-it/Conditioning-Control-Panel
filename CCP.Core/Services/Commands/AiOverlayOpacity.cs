using System;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// What an AI <c>spiral</c> / <c>pink</c> command may write into the user's SAVED opacity.
    /// "Off" carries no opacity: the model sends <c>{ "On": false }</c> (or Intensity 0, the way the
    /// bubbles example does), the missing field deserialises to 0, and writing that left the user's
    /// spiral or pink filter at 0 % for every later run, invisible when switched back on. An "on"
    /// with no intensity keeps the user's own opacity instead of painting an invisible overlay.
    /// Pure.
    /// </summary>
    public static class AiOverlayOpacity
    {
        /// <returns>The opacity to store, or null to leave the user's value alone.</returns>
        public static int? ToWrite(bool on, int intensity, int max)
            => on && intensity > 0 ? Math.Clamp(intensity, 1, Math.Max(1, max)) : null;
    }
}
