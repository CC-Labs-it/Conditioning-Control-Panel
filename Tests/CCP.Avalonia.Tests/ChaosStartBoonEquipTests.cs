using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosStartBoonEquipTests
{
    // WPF ChaosHubWindow.xaml.cs:1261-1276,1335-1341: real catalogue, discovery gate,
    // non-curse left-click toggle, saved selection and rebuilt gold ring.
    // WPF ChaosModeService.cs:417-437: FALL IN carries that selection onto the live ribbon.
    [Fact]
    public async Task Hub_selection_persists_reopens_toggles_and_reaches_the_run_ribbon()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var boon = ChaosBoonPool.All.First(b => !b.IsCurse);
            var hidden = ChaosBoonPool.All.First(b => !b.IsCurse && b.Id != boon.Id);
            var curse = ChaosBoonPool.All.First(b => b.IsCurse);
            ChaosHubWindow? hub = null;
            try
            {
                ChaosMeta.EquipStartBoon(null);
                ChaosMeta.State.DiscoveredCodexIds.Clear();
                ChaosMeta.MarkDiscovered("boon:" + boon.Id);
                ChaosMeta.MarkDiscovered("boon:" + curse.Id);
                hub = new ChaosHubWindow();
                var rows = hub.FindControl<StackPanel>("MantrasHost")!;
                Assert.Equal(ChaosBoonPool.All.Count, rows.Children.Count);
                Border Row(string id) => Assert.IsType<Border>(rows.Children[
                    ChaosBoonPool.All.ToList().FindIndex(b => b.Id == id)]);
                void Press(string id, bool left = true)
                {
                    var row = Row(id);
                    row.RaiseEvent(new PointerPressedEventArgs(row,
                        new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), hub, new Point(1, 1), 0,
                        new PointerPointProperties(left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton,
                            left ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.RightButtonPressed),
                        KeyModifiers.None, 1));
                }
                string[] Texts(string id) => Row(id).GetLogicalDescendants().OfType<TextBlock>()
                    .Select(t => t.Text ?? "").ToArray();
                Assert.Contains("???", Texts(hidden.Id));
                Assert.DoesNotContain(hidden.Name, Texts(hidden.Id));
                Assert.Contains(curse.Name, Texts(curse.Id));
                Press(hidden.Id);
                Press(curse.Id);
                Press(boon.Id, left: false);
                Assert.Null(ChaosMeta.State.EquippedStartBoon);
                Press(boon.Id);
                Assert.Equal(boon.Id, ChaosMetaStore.Load().EquippedStartBoon);
                Assert.Equal(new Thickness(3), Row(boon.Id).BorderThickness);
                Press(curse.Id);
                Assert.Equal(boon.Id, ChaosMeta.State.EquippedStartBoon);
                Press(boon.Id);
                Assert.Null(ChaosMetaStore.Load().EquippedStartBoon);
                Assert.Equal(new Thickness(2), Row(boon.Id).BorderThickness);
                Press(boon.Id);
                hub.Close();
                hub = new ChaosHubWindow();
                rows = hub.FindControl<StackPanel>("MantrasHost")!;
                Assert.Equal(new Thickness(3), Row(boon.Id).BorderThickness);
                hub.FallIn();
                Dispatcher.UIThread.RunJobs();
                Assert.True(ChaosRunHost.IsActive);
                Assert.Null(ChaosRunHost.BoonBar);
                ChaosRunHost.Overlay!.FinishCountdown();
                Assert.Equal(boon.Id, Assert.Single(ChaosRunHost.State!.RunPickTiles).Id);
                Assert.Single(ChaosRunHost.BoonBar!.Row.Children);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                hub?.Close();
                ChaosMeta.EquipStartBoon(null);
            }
            return Task.CompletedTask;
        });
    }
}
