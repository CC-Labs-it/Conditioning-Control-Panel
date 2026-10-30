using System;
using System.Text;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ScrolllerRefusedBodyTests
{
    [Fact]
    public void ARefusedBodyIsRememberedOnlyUntilLocalMidnight()
    {
        const string url = "https://images.scrolller.com/sync7/error.jpg";
        var body = Encoding.UTF8.GetBytes("<html>upstream error</html>");
        Assert.True(ScrolllerWebGate.RefusedBody(url, "picture", body, body.Length));
        var now = new DateTime(2026, 10, 19, 23, 0, 0, DateTimeKind.Local);
        ScrolllerWebGate.ClearRefusedForTests();
        try
        {
            Assert.False(ScrolllerWebGate.IsRefusedToday(url, now));
            ScrolllerWebGate.RefuseForToday(url, now);
            Assert.True(ScrolllerWebGate.IsRefusedToday(url, now.AddMinutes(59)));
            Assert.False(ScrolllerWebGate.IsRefusedToday(url, now.AddHours(1)));
            Assert.False(ScrolllerWebGate.IsRefusedToday(url + "?other", now));
        }
        finally { ScrolllerWebGate.ClearRefusedForTests(); }
    }
}
