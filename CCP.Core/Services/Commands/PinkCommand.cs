using System;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class PinkCommand : ICommand
    {
        public const int MaxIntensity = 30;

        private readonly SpiralPinkFiler _data;
        public PinkCommand(SpiralPinkFiler data) { _data = data; }

        /// <summary>Head surface: (on, intensity) on the UI thread - writes the Pink filter settings and
        /// brings the overlay up past the level check (WPF App.Overlay). False when nothing could
        /// show. Unseeded: refused, nothing faked.</summary>
        /// The opacity is null for "off" and for an "on" with no intensity: the saved opacity is the
        /// user's (AiOverlayOpacity, main bafe52cfc).
        public static volatile Func<bool, int?, bool>? Surface;

        public Task<bool> ExecuteAsync()
        {
            // Null for "off" (and for an "on" with no intensity): the saved opacity is the user's.
            var opacity = AiOverlayOpacity.ToWrite(_data.On, _data.Intensity, MaxIntensity);

            try
            {
                return Task.FromResult(Surface?.Invoke(_data.On, opacity) == true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PinkCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
