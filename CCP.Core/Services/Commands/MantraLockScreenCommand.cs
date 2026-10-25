using System;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public class MantraLockScreenCommand : ICommand
    {
        public const int MaxRepeats = 5;
        public const int MaxMantraChars = 200;

        private readonly MantraLockscreen _data;
        public MantraLockScreenCommand(MantraLockscreen data) { _data = data; }

        /// <summary>Head surface: (phrase, repeats) as a card on the UI thread whose strictness follows the
        /// user's own Lock Card switch (WPF App.LockCard.ShowLockCard customStrict: false). False when
        /// nothing could show. Unseeded: refused.</summary>
        public static volatile Func<string, int, bool>? Surface;

        /// <summary>
        /// Repeats for an AI lock card. A missing "amount" reads as 0, and a 0-repeat card showed
        /// "0 / 0" as if done but never closed (completion only runs on a typed match), leaving a
        /// strict card only the panic key could clear (ccp-bugs #1444). At least one repeat.
        /// </summary>
        internal static int Repeats(int amount) => Math.Clamp(amount, 1, MaxRepeats);

        public Task<bool> ExecuteAsync()
        {
            var amount = Repeats(_data.Amount);
            var phrase = (_data.Mantra ?? string.Empty).Trim();
            if (phrase.Length > MaxMantraChars) phrase = phrase.Substring(0, MaxMantraChars);
            if (string.IsNullOrEmpty(phrase)) return Task.FromResult(false);

            try
            {
                // Strict follows the user's own Lock Card switch (the head's surface passes
                // customStrict: false, WPF 61eeb4d0d). An AI card used to force strict on every player.
                return Task.FromResult(Surface?.Invoke(phrase, amount) == true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MantraLockScreenCommand failed");
                return Task.FromResult(false);
            }
        }
    }
}
