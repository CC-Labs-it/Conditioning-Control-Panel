using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public class SubliminalScheduleReviewTests
{
    [Fact]
    public async Task Ambient_tick_shows_in_all_afterglow_modes_skips_when_disabled_and_rearms()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-subliminal-schedule-");
        var service = new SettingsService(Path.Combine(dir.FullName, "settings.json"));
        var oldSettings = CoreSettings.ServiceProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldTrying = SuperAccess.TryingProvider;
        var oldTierHook = SuperAccess.TierEventsHook;
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var boundField = typeof(SuperAccess).GetField("_baseSettings", flags)!;
        var hookedField = typeof(SuperAccess).GetField("_baseHooked", flags)!;
        var oldBound = (AppSettings?)boundField.GetValue(null);
        bool oldHooked = (bool)hookedField.GetValue(null)!;
        var changed = typeof(SuperAccess).GetMethod("OnBasePropertyChanged", flags)!.CreateDelegate<PropertyChangedEventHandler>();
        var replaced = typeof(SuperAccess).GetMethod("OnSettingsReplaced", flags)!.CreateDelegate<Action>();
        var oldPost = CoreDispatch.PostProvider;
        var oldShow = CoreSubliminal.ShowProvider;
        var oldRun = CoreSubliminal.RunStateChanged;
        bool wasRunning = CoreSubliminal.IsRunning;
        int shown = 0;
        var automatic = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            CoreSubliminal.Stop();
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => true;
            SuperAccess.TryingProvider = () => null;
            SuperAccess.TierEventsHook = null;
            CoreDispatch.PostProvider = action => action();
            CoreSubliminal.RunStateChanged = null;
            CoreSubliminal.ShowProvider = text =>
            {
                Assert.Equal("test phrase", text);
                if (Interlocked.Increment(ref shown) >= 4) automatic.TrySetResult(true);
            };
            var settings = service.Current;
            settings.SubliminalEnabled = true;
            settings.SubliminalFrequency = 30;
            settings.SubliminalPool = new Dictionary<string, bool> { ["test phrase"] = true };
            var tick = typeof(CoreSubliminal).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Static)!;
            var timerField = typeof(CoreSubliminal).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Static)!;
            CoreSubliminal.Start();
            ((Timer)timerField.GetValue(null)!).Change(Timeout.Infinite, Timeout.Infinite);
            foreach (var mode in new[] { SuperMode.Classic, SuperMode.Both, SuperMode.SuperOnly })
            {
                settings.SuperModes = new Dictionary<string, int> { [SuperEffect.Afterglow.ToString()] = (int)mode };
                Assert.False(SuperAccess.ReplacesBase(SuperEffect.Afterglow));
                tick.Invoke(null, new object?[] { null });
            }
            Assert.Equal(3, Volatile.Read(ref shown));
            settings.SubliminalEnabled = false;
            tick.Invoke(null, new object?[] { null });
            Assert.Equal(3, Volatile.Read(ref shown));
            settings.SubliminalEnabled = true;
            tick.Invoke(null, new object?[] { null });
            Assert.True(await automatic.Task.WaitAsync(TimeSpan.FromSeconds(8)));
            // One more firing must come from the timer rearmed by the tick, not reflection.
            var next = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CoreSubliminal.ShowProvider = _ => next.TrySetResult(true);
            Assert.True(await next.Task.WaitAsync(TimeSpan.FromSeconds(8)));
        }
        finally
        {
            CoreSubliminal.Stop();
            CoreSubliminal.ShowProvider = oldShow; CoreSubliminal.RunStateChanged = oldRun;
            CoreDispatch.PostProvider = oldPost; SuperAccess.TryingProvider = oldTrying;
            SuperAccess.TierEventsHook = oldTierHook;
            if (ReferenceEquals(boundField.GetValue(null), service.Current))
            {
                service.Current.PropertyChanged -= changed;
                if (oldBound != null) oldBound.PropertyChanged += changed;
                boundField.SetValue(null, oldBound);
            }
            if (!oldHooked && (bool)hookedField.GetValue(null)!)
            {
                service.CurrentReplaced -= replaced;
                hookedField.SetValue(null, false);
            }
            CoreEntitlement.HasPremiumProvider = oldPremium; CoreSettings.ServiceProvider = oldSettings;
            if (wasRunning) CoreSubliminal.Start();
            dir.Delete(true);
        }
    }
}
