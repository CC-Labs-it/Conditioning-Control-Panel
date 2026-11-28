using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services.PrizeCounter;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void PrizeCounter_BrowserFailureClosesAndNotifiesOnce() => RunCounter((_, _, sent) =>
    {
        var heard = new List<string>();
        var window = OpenCounter(sent, heard.Add);
        window.Web.OnBrowserProcessFailed();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
        Assert.Equal(PrizeCounterRules.CloseFailed, Assert.Single(heard));
        window.Web.OnBrowserProcessFailed();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(heard);
    });
}
