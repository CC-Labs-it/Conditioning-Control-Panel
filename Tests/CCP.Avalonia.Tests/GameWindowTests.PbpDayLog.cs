using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;
public sealed partial class GameWindowTests
{
    [Fact]
    public void PbpLaunch_NotesDayLogOncePerWindow() => Run((_, _) =>
    {
        using var log = new FeatureDayLogService(() => null,
            System.IO.Path.Combine(TestUserDataProfile.Root, "pbp-day-log.json"));
        var property = typeof(ConditioningControlPanel.Avalonia.App).GetProperty("FeatureDayLog",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        property.SetValue(null, log);
        try
        {
            var key = SeasonFeatureKeys.ToDayLogEvent(SeasonFeatureKeys.PieceByPiece)!;
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            Assert.Equal(1, log.Log.Days.Single().Get(key));
            Assert.Same(w, GameWindow.Launch(GameWindow.PbpId));
            Assert.Equal(1, log.Log.Days.Single().Get(key));
            w.Close();
            GameWindow.Launch(GameWindow.PbpId);
            Assert.Equal(2, log.Log.Days.Single().Get(key));
        }
        finally { property.SetValue(null, previous); }
    });
}
