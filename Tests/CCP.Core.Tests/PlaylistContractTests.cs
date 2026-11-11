using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public class PlaylistContractTests
{
    [Fact]
    public void PlaylistLanesShareThePortableContract()
    {
        var assembly = typeof(AppSettings).Assembly;
        var contract = assembly.GetType("ConditioningControlPanel.Services.AudioPlayer.IAudioPlaylistService");
        Assert.NotNull(contract);
        Assert.True(contract.IsInterface);
        var loop = assembly.GetType("ConditioningControlPanel.Services.AudioPlayer.AudioPlaylistLoop");
        Assert.NotNull(loop);
        Assert.Equal(new[] { "Off", "All", "One" }, Enum.GetNames(loop));
        Assert.Equal(new[] { 0, 1, 2 }, Enum.GetValues(loop).Cast<object>().Select(Convert.ToInt32).ToArray());
        Assert.Equal(typeof(IReadOnlyList<string>), contract.GetProperty("Tracks")!.PropertyType);
        Assert.Equal(typeof(string), contract.GetProperty("CurrentPath")!.PropertyType);
        Assert.Equal(typeof(int), contract.GetMethod("Add")!.ReturnType);
        Assert.Equal(typeof(IEnumerable<string>), contract.GetMethod("Add")!.GetParameters()[0].ParameterType);
        foreach (var name in new[] { "Volume", "Loop", "Shuffle" })
            Assert.True(contract.GetProperty(name)!.CanWrite);
        foreach (var name in new[] { "Play", "Pause", "Stop", "Next", "Previous", "Clear" })
            Assert.Equal(typeof(void), contract.GetMethod(name)!.ReturnType);
        Assert.Equal(typeof(Action), contract.GetEvent("StateChanged")!.EventHandlerType);
        Assert.Equal(typeof(Action<string>), contract.GetEvent("TrackChanged")!.EventHandlerType);
        Assert.Equal(typeof(Action<double>), contract.GetEvent("PositionChanged")!.EventHandlerType);
    }
}
