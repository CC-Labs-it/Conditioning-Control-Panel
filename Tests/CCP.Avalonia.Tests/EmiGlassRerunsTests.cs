using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiGlassRerunsTests
{
    [Fact]
    public void MaterialFailsClosedAndCachesByStamp()
    {
        var path = Path.Combine(CorePaths.UserData, "arcademy_meta.json");
        File.Delete(path);
        Assert.False(RerunsPainter.HasMaterial());
        File.WriteAllText(path, "{\"days\":");
        Assert.False(RerunsPainter.HasMaterial());
        File.WriteAllText(path, "{\"days\":[]}");
        Assert.False(RerunsPainter.HasMaterial());
        var valid = "{\"days\":{\"2026-01-01\":{\"classes\":{\"test_game\":{\"grade\":\"a\"}}}}}";
        File.WriteAllText(path, valid);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        Assert.True(RerunsPainter.HasMaterial());
        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, new string(' ', valid.Length));
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.True(RerunsPainter.HasMaterial());
        File.SetLastWriteTimeUtc(path, stamp.AddSeconds(2));
        Assert.False(RerunsPainter.HasMaterial());
    }

    [Fact]
    public Task CardShowsTapeAndMoves() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var canvas = new Canvas { Width = 152, Height = 137 };
        var painter = new RerunsPainter(canvas, new RerunsPainter.Tape("2026-01-01", "test_game", "A"));
        var labels = canvas.Children.OfType<TextBlock>().ToArray();
        Assert.Equal(new[] { "A", "TEST GAME", "2026-01-01", "<< REW", "0:00" }, labels.Select(t => t.Text));
        painter.Tick(1200);
        Assert.Equal("> PLAY", labels[3].Text);
        Assert.Equal("0:01", labels[4].Text);
        Assert.Equal("CLASS", RerunsPainter.FlapName("__"));
        Assert.Equal("ABCDEFGHIJKLMN", RerunsPainter.FlapName("abcdefghijklmnop"));
        return Task.CompletedTask;
    });
}
