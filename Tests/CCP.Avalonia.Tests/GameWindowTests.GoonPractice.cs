using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void GoonPractice_RegisteredBootsAndPanicCloses() => Run((_, _) =>
    {
        var window = GameWindow.Launch("goon")!;
        Assert.NotNull(window);
        Assert.Equal("goon/index.html", window.Spec.Page);
        var frames = new System.Collections.Generic.List<JObject>();
        window.Posted += json => frames.Add(JObject.Parse(json));
        window.Web.OnNavigationCompleted(window.PageUrl);
        window.Web.OnWebMessage("{\"type\":\"ready\"}");
        var init = Assert.Single(frames.Where(f => (string?)f["type"] == "init"));
        Assert.Null(init["practiceOnly"]);
        Assert.Equal("https://codebambi-proxy.vercel.app", (string?)init["net"]?["serverBase"]);
        Assert.True((bool?)init["net"]?["viaHost"]);
        Assert.True(window.IsReady);
        for (int i = 0; i < 1000 && !frames.Any(f => (string?)f["type"] == "manifest"); i++)
        {
            System.Threading.Thread.Sleep(10);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Assert.Contains(frames, f => (string?)f["type"] == "manifest" && f["images"] is JArray && f["videos"] is JArray);

        GameWindow.CloseAllForPanic();
        Assert.DoesNotContain(window, GameWindow.OpenWindowsForTests());
    });
}
