using System;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionRulesTests
{
    [Fact]
    public void ActivationOffersRulesAndBarks_OpeningSpendsOffer() => AvaloniaTestDispatcher.Run(() =>
    {
        AmbientBeatLoopsTests.Setup();
        StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.LockdownPossessionIntroSeen = false;
        s.SeenFeatureIntros.Remove("possession");
        s.AvatarEnabled = true;
        s.BarkChatSuppressionMs = 0;
        foreach (var key in FeatureIntros.All.Keys) if (key != "possession") s.SeenFeatureIntros.Add(key);
        s.WhatMovedCardShown = 1;
        var oldBark = BarkHead.Engine;
        var rule = new BarkRule { Id = "rules-test", Trigger = PossessionBarkTriggers.Rules, VariantPool = new() { new BarkVariant("rules") } };
        var bark = BarkHead.Engine = new BarkEngine(() => new BarkRuleSet(new[] { rule }));
        int spoken = 0;
        bark.Speak = _ => spoken++;
        bark.Start();
        var ld = LockdownService.Current = new LockdownService();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            s.LockdownPossessionEnabled = false;
            ld.Activate(TimeSpan.FromMinutes(30));
            Dispatcher.UIThread.RunJobs();
            Assert.False(StartupLadder.Inbox.Contains("intro:possession"));
            Assert.Equal(0, spoken);
            ld.Deactivate();
            s.LockdownPossessionEnabled = true;
            ld.Activate(TimeSpan.FromMinutes(30));
            Dispatcher.UIThread.RunJobs();
            var row = Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "intro:possession");
            Assert.Equal(1, spoken);
            Assert.False(s.LockdownPossessionIntroSeen);
            StartupLadder.Inbox.Dismiss(row);
            Assert.False(s.LockdownPossessionIntroSeen);
            ld.Deactivate();
            ld.Activate(TimeSpan.FromMinutes(30));
            Dispatcher.UIThread.RunJobs();
            row = Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "intro:possession");
            settings.SaveImmediate();
            Assert.False(settings.HasPendingSave);
            StartupLadder.Inbox.Open(row);
            Assert.True(s.LockdownPossessionIntroSeen);
            Assert.True(settings.HasPendingSave);
            ld.Deactivate();
            ld.Activate(TimeSpan.FromMinutes(30));
            Dispatcher.UIThread.RunJobs();
            Assert.False(StartupLadder.Inbox.Contains("intro:possession"));
            Assert.Equal(2, spoken);
        }
        finally
        {
            ld.Deactivate();
            shell.RequestExit(); shell.Close();
            ld.Dispose(); LockdownService.Current = null;
            BarkHead.Engine = oldBark;
            StartupLadder.ResetForTests(); FeatureIntroPopup.ResetForTests();
            CoreSettings.ServiceProvider = null;
        }
    });
}
