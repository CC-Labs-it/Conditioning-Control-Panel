using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class EmiPremiumTeaseTests
{
    [Fact]
    public Task DenialSendsFeatureAndPanicSuppressesQueuedDenial() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = new MainShellWindow();
        var moments = new List<(string Name, object Context)>();
        var oldReconnect = TierGate.ReconnectIsTheAnswerProvider;
        shell.EmiMoment = (name, context) => moments.Add((name, context));
        try
        {
            PanicAftermath.EndQuiet();
            TierGate.ReconnectIsTheAnswerProvider = () => false;
            var verdict = new TierVerdict(false, "Locked Feature", PatreonTier.Level2, "Denied");
            shell.ShowTierDenied(verdict, AvApp.Notifications);
            var moment = Assert.Single(moments);
            Assert.Equal("premiumTeaseSeen", moment.Name);
            Assert.Equal("locked feature", moment.Context.GetType().GetProperty("target")!.GetValue(moment.Context));
            PanicSurfaces.StopAll("test", shell);
            moments.Clear();
            shell.ShowTierDenied(verdict, AvApp.Notifications);
            Assert.Empty(moments);
        }
        finally
        {
            PanicAftermath.EndQuiet();
            TierGate.ReconnectIsTheAnswerProvider = oldReconnect;
            shell.Close();
        }
        return Task.CompletedTask;
    });
}
