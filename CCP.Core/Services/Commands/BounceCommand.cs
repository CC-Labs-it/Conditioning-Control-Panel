using System;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class BounceCommand : ICommand
    {
        private readonly Bounce _data;
        public BounceCommand(Bounce data) { _data = data; }

        /// <summary>Head surface: (on, words) on the UI thread (WPF App.BouncingText Start/Stop).
        /// Returns false when nothing could show. Unseeded: refused, nothing faked.</summary>
        public static volatile Func<bool, System.Collections.Generic.List<string>?, bool>? Surface;

        public Task<bool> ExecuteAsync()
        {
            try
            {
                // Trimmed and capped (main 61eeb4d0d); null leaves the user's own words.
                return Task.FromResult(Surface?.Invoke(_data.On, _data.On ? AiCommandAdmission.BounceWords(_data.Words) : null) == true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BounceCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
