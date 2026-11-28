using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHubHabitTipTests
{
    [Fact]
    public void Habit_icon_has_a_card_with_name_description_and_flavour() => AvaloniaTestDispatcher.Run(() =>
    {
        var hub = new ChaosHubWindow();
        try
        {
            var shelf = typeof(ChaosHubWindow).GetNestedType("Shelf", BindingFlags.NonPublic)!;
            var habits = (System.Collections.IEnumerable)shelf.GetProperty("Habits")!.GetValue(null)!;
            var habit = habits.Cast<object>().First();
            var type = habit.GetType();
            string Get(string name) => (string)type.GetProperty(name)!.GetValue(habit)!;
            var row = (Border)typeof(ChaosHubWindow).GetMethod("BuildUpgradeRow", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(hub, new[] { habit })!;
            var icon = ((Grid)row.Child!).Children[0];
            var tip = Assert.IsType<StackPanel>(ToolTip.GetTip(icon));
            var texts = tip.Children.OfType<TextBlock>().ToArray();
            Assert.Equal(new[] { Get("Name"), Get("Desc"), Get("Flavor") }, texts.Select(t => t.Text));
            Assert.Equal(FontStyle.Italic, texts[2].FontStyle);
        }
        finally { hub.Close(); }
    });
}
