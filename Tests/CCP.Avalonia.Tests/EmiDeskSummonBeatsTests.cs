using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiDeskSummonBeatsTests
{
    [Fact]
    public void EmptyLibraryIsRecheckedAndDismissCancels() => AvaloniaTestDispatcher.Run(() =>
    {
        var root = Directory.CreateTempSubdirectory("ccp-emi-empty-").FullName;
        var previous = CorePaths.EffectiveAssetsProvider;
        var source = CoreSettings.Current.MediaSource;
        var desk = new EmiDeskService();
        var seen = new List<string>();
        desk.EmptyLibraryMoment = seen.Add;
        typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, true);
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            CoreSettings.Current.MediaSource = "local";
            desk.GreetNow();
            var timer = (DispatcherTimer)typeof(EmiDeskService).GetField("_emptyLibraryTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(desk)!;
            Assert.Equal(TimeSpan.FromSeconds(7), timer.Interval);
            desk.EmptyLibraryDue(); desk.EmptyLibraryDue();
            Assert.Equal(new[] { "noMediaYet" }, seen);
            seen.Clear();
            desk.GreetNow();
            Directory.CreateDirectory(Path.Combine(root, "images"));
            File.WriteAllText(Path.Combine(root, "images", "new.png"), "x");
            desk.EmptyLibraryDue();
            Assert.Empty(seen);
            File.Delete(Path.Combine(root, "images", "new.png"));
            CoreSettings.Current.MediaSource = "online";
            desk.GreetNow(); desk.EmptyLibraryDue();
            Assert.Empty(seen);
            CoreSettings.Current.MediaSource = "local";
            desk.GreetNow(); desk.Dismiss(); desk.EmptyLibraryDue();
            Assert.Empty(seen);
            Directory.CreateDirectory(Path.Combine(root, "videos"));
            File.WriteAllText(Path.Combine(root, "videos", "new.mp4"), "x");
            Assert.False(EmiOffers.LibraryIsEmpty());
        }
        finally
        {
            desk.Dismiss();
            CorePaths.EffectiveAssetsProvider = previous;
            CoreSettings.Current.MediaSource = source;
            Directory.Delete(root, true);
        }
    });
}
