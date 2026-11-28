using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesVeilChipTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task LockedVeilChipTracksLanguage(bool spotlight) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var language = LocalizationManager.Instance.CurrentLanguage;
        var (premium, free, pass) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider,
            CoreEntitlement.IntakePassAvailableProvider);
        CoreEntitlement.HasPremiumProvider = () => false;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        CoreEntitlement.IntakePassAvailableProvider = () => false;
        Window? host = null;
        try
        {
            LocalizationManager.Instance.SetLanguage("en");
            var view = new ExclusivesTabView { Width = 1400, Height = 900 };
            host = new Window { Width = 1400, Height = 900, Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();

            IconLabel Chip()
            {
                if (spotlight)
                {
                    var veil = view.FindControl<Border>("SpotVeil")!;
                    Assert.True(veil.IsEffectivelyVisible);
                    return veil.GetVisualDescendants().OfType<IconLabel>().Single();
                }
                var mark = view.GetVisualDescendants().OfType<IconGlyph>()
                    .First(x => x.Name == "VeilLock" && x.IsEffectivelyVisible);
                return mark.GetVisualAncestors().OfType<StackPanel>().First()
                    .GetVisualDescendants().OfType<IconLabel>().Single();
            }

            var english = Loc.Get("plans_chip_unlock");
            Assert.Equal(english, Chip().Label.Text);
            LocalizationManager.Instance.SetLanguage("it");
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();
            Assert.NotEqual(english, Loc.Get("plans_chip_unlock"));
            Assert.Equal(Loc.Get("plans_chip_unlock"), Chip().Label.Text);
        }
        finally
        {
            host?.Close();
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.IsFreeTodayProvider = free;
            CoreEntitlement.IntakePassAvailableProvider = pass;
            LocalizationManager.Instance.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });
}
