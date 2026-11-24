using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The Avalonia <see cref="IPopQuizHost"/>, after WPF <c>PopQuizService</c>
    /// (ConditioningControlPanel/Services/Quiz/PopQuizService.cs). Every member goes through
    /// Dispatcher.UIThread.Invoke, which runs inline on the UI thread, so it is safe from any caller.
    ///
    /// </summary>
    public sealed class PopQuizHost : IPopQuizHost
    {
        public static PopQuizHost Instance { get; } = new();
        public PopQuizScheduler Scheduler { get; }
        private readonly Random _random = new();

        private PopQuizHost()
        {
            Scheduler = new PopQuizScheduler(this);
        }

        public bool IsQuizOpen => Dispatcher.UIThread.Invoke(PopQuizWindow.IsAnyOpen);
        public bool IsLockCardOpen => Dispatcher.UIThread.Invoke(LockCardWindow.IsAnyOpen);
        public bool IsInteractionBusy => Dispatcher.UIThread.Invoke(() => LockCardWindow.InteractionBusy);

        public bool Defer(Action replay) => LockCardWindow.DeferQuiz(replay);

        public void DropDeferred() => LockCardWindow.DropQuiz();

        public void Open(bool isTest) => Dispatcher.UIThread.Invoke(() =>
        {
            try
            {
                var mods = App.Mods;
                var pool = PopQuizScheduler.ResolveQuestionPool(
                    mods?.GetQuizPraiseOverride(),
                    mods?.GetQuizObedienceQuestionOverride(),
                    mods?.GetQuizPraiseHeardQuestionOverride());
                var question = pool[_random.Next(pool.Length)];
                new PopQuizWindow(question, isTest).Show();
                if (!isTest) SeasonFeatureTracking.Track(CoreSettings.Current, ConditioningControlPanel.Models.SeasonFeatureKeys.PopQuiz, ev => App.FeatureDayLog?.Note(ev), DateTime.UtcNow);
                Log.Information("Pop Quiz shown: {Question}", question.QuestionText);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to show pop quiz: {Error}", ex.Message);
            }
        });

        public void CloseAll() => Dispatcher.UIThread.Invoke(() =>
        {
            LockCardWindow.DropQuiz();
            LockCardWindow.DropHeld();   // first: the last quiz closing would replay a held card
            PopQuizWindow.ForceCloseAll();
        });
    }
}
