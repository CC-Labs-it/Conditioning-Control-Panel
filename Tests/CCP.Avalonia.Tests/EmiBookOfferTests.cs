using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class EmiBookOfferTests
{
    [Theory]
    [InlineData(true, "tourFinished")]
    [InlineData(false, "tourSkipped")]
    public Task EndingOffersOnce(bool completed, string why) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var events = new List<EmiMoment>();
        void OnMoment(object? sender, EmiMoment e) { if (e.Id == "bookOffer") events.Add(e); }
        var desk = EmiDeskService.Instance;
        var content = EmiBookOffer.HasContent; var open = EmiBookOffer.IsOpen; var opens = EmiBookOffer.Opens;
        var delay = EmiBookOffer.DelayMs;
        try
        {
            EmiBookOffer.HasContent = () => true; EmiBookOffer.IsOpen = () => false; EmiBookOffer.Opens = () => 0;
            EmiBookOffer.DelayMs = 30; desk.MomentFired += OnMoment;
            TutorialHead.Seed(); TutorialHead.Service.Start(TutorialType.ShortWalk);
            if (completed) while (TutorialHead.Service.IsActive) TutorialHead.Service.Next();
            else TutorialHead.Service.Skip();
            await Task.Delay(150);
            var moment = Assert.Single(events);
            Assert.Equal(why, moment.Context!.GetType().GetProperty("why")!.GetValue(moment.Context));
        }
        finally
        {
            desk.MomentFired -= OnMoment; TutorialHead.ResetForTests();
            EmiBookOffer.HasContent = content; EmiBookOffer.IsOpen = open; EmiBookOffer.Opens = opens; EmiBookOffer.DelayMs = delay;
        }
    });

    [Fact]
    public Task RearmBrakesAndPanic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var events = new List<EmiMoment>();
        void OnMoment(object? sender, EmiMoment e) { if (e.Id == "bookOffer") events.Add(e); }
        var desk = EmiDeskService.Instance;
        var content = EmiBookOffer.HasContent; var open = EmiBookOffer.IsOpen; var opens = EmiBookOffer.Opens;
        var delay = EmiBookOffer.DelayMs;
        try
        {
            EmiBookOffer.HasContent = () => true; EmiBookOffer.IsOpen = () => false; EmiBookOffer.Opens = () => 0;
            EmiBookOffer.DelayMs = 80; desk.MomentFired += OnMoment;
            EmiBookOffer.Schedule("tourFinished"); await Task.Delay(40); EmiBookOffer.Schedule("tourSkipped");
            await Task.Delay(160);
            var moment = Assert.Single(events);
            Assert.Equal("tourSkipped", moment.Context!.GetType().GetProperty("why")!.GetValue(moment.Context));
            events.Clear(); EmiBookOffer.HasContent = () => false; EmiBookOffer.Schedule("tourFinished"); await Task.Delay(120);
            EmiBookOffer.HasContent = () => true; EmiBookOffer.Opens = () => 1; EmiBookOffer.Schedule("tourFinished"); await Task.Delay(120);
            EmiBookOffer.Opens = () => 0; EmiBookOffer.IsOpen = () => true; EmiBookOffer.Schedule("tourFinished"); await Task.Delay(120);
            Assert.Empty(events);
            EmiBookOffer.IsOpen = () => false; EmiBookOffer.Schedule("tourFinished"); TutorialHead.OnPanic(); await Task.Delay(120);
            Assert.Empty(events);
        }
        finally
        {
            desk.MomentFired -= OnMoment; TutorialHead.ResetForTests();
            EmiBookOffer.HasContent = content; EmiBookOffer.IsOpen = open; EmiBookOffer.Opens = opens; EmiBookOffer.DelayMs = delay;
        }
    });
}
