using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EmiTourOfferTests
{
    [Theory]
    [InlineData("shortwalk", "ShortWalk")]
    [InlineData("upgrade", "UpgradeTour")]
    public Task AcceptedTourRunsAndPanicAbandonsIt(string verb, string tour) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        AmbientBeatLoopsTests.Setup();
        using var state = new EmiTestScope();
        var oldShell = TutorialHeadHooks.ShellProvider;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var oldSettings = CoreSettings.ServiceProvider;
        var oldMoment = EmiTargets.Moment;
        var oldStore = TutorialService.CompletionStore;
        var moments = new List<(string, object?)>();
        var shell = new MainShellWindow();
        var settings = new SettingsService();
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreSettings.Current.EmiDeskEnabled = false;
            shell.Show();
            TutorialHead.Seed(); TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;
            CoreSession.IsSessionRunningProvider = () => false;
            EmiTargets.Moment = (id, context) => moments.Add((id, context));
            Assert.True(EmiOffers.EffectFeasible("tour:" + verb));
            EmiOffers.Run("tour:" + verb, true);
            await TutorialHead.ShellStartup;
            Assert.True(TutorialHead.Service.IsActive);
            Assert.Equal(tour, TutorialHead.Service.CurrentTutorialType.ToString());
            Assert.Multiple(
                () => Assert.Equal(EmiState.KnockSpent, EmiState.Current.KnockState),
                () =>
                {
                    var moment = Assert.Single(moments);
                    Assert.Equal("effectFired", moment.Item1);
                    Assert.Equal("tour", moment.Item2!.GetType().GetProperty("channel")!.GetValue(moment.Item2));
                    Assert.Equal(true, moment.Item2.GetType().GetProperty("fromAsk")!.GetValue(moment.Item2));
                });
            Assert.False(EmiOffers.EffectFeasible("tour:" + verb));
            PanicSurfaces.All.Single(x => x.Id == "tour").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(TutorialHead.Service.IsActive);
            Assert.False(EmiState.HasTourDone(tour));
        }
        finally
        {
            TutorialHead.ResetForTests(); TutorialService.CompletionStore = oldStore;
            TutorialHeadHooks.ShellProvider = oldShell; CoreSession.IsSessionRunningProvider = oldSession;
            EmiTargets.Moment = oldMoment; CoreSettings.ServiceProvider = oldSettings;
            shell.Close(); Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task TourOffersRespectAvailability() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        using var state = new EmiTestScope();
        var oldShell = TutorialHeadHooks.ShellProvider;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var shell = new MainShellWindow();
        try
        {
            TutorialHead.Seed(); TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => null;
            CoreSession.IsSessionRunningProvider = () => false;
            Assert.False(EmiOffers.EffectFeasible("tour:shortwalk"));
            TutorialHeadHooks.ShellProvider = () => shell;
            Assert.False(EmiOffers.EffectFeasible("tour:other"));
            CoreSession.IsSessionRunningProvider = () => true;
            Assert.False(EmiOffers.EffectFeasible("tour:shortwalk"));
            CoreSession.IsSessionRunningProvider = () => false;
            EmiState.NoteTourDone(EmiKnockMachine.ShortWalkTour);
            Assert.False(EmiOffers.EffectFeasible("tour:shortwalk"));
            Assert.True(EmiOffers.EffectFeasible("tour:upgrade"));
        }
        finally
        {
            TutorialHead.ResetForTests(); TutorialHeadHooks.ShellProvider = oldShell;
            CoreSession.IsSessionRunningProvider = oldSession; shell.Close();
        }
        return Task.CompletedTask;
    });
}
