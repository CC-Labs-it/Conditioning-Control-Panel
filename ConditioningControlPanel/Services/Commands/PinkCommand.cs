using System;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class PinkCommand : ICommand
    {
        public const int MaxIntensity = 30;

        private readonly SpiralPinkFiler _data;
        public PinkCommand(SpiralPinkFiler data) { _data = data; }

        public Task<bool> ExecuteAsync()
        {
            // Null for "off" (and for an "on" with no intensity): the saved opacity is the user's.
            var opacity = AiOverlayOpacity.ToWrite(_data.On, _data.Intensity, MaxIntensity);

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var settings = App.Settings?.Current;
                    if (settings == null || App.Overlay == null) return;

                    if (opacity is int o) settings.PinkFilterOpacity = o;
                    settings.PinkFilterEnabled = _data.On;

                    if (!App.Overlay.IsRunning)
                    {
                        App.Overlay.BypassLevelCheck = true;
                        App.Overlay.Start();
                    }
                    else if (!App.Overlay.BypassLevelCheck)
                    {
                        App.Overlay.BypassLevelCheck = true;
                    }

                    App.Overlay.RefreshOverlays();
                    App.Settings?.Save();
                });
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "PinkCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
