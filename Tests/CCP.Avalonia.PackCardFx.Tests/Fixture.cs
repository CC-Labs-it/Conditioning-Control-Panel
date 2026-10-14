using Avalonia.Controls;
using Avalonia.Media;
namespace ConditioningControlPanel.Avalonia.Controls
{
    // Isolate the ambient environment's world inputs, not any controller/adorner/clock behaviour.
    public static class AmbientFxCanvas
    {
        public static EnvironmentInputs Env { get; } = new();
        public sealed class EnvironmentInputs
        {
            public bool AllowTransitions { get; set; } = true;
            public Color GlowColor { get; set; } = Color.Parse("#FF69B4");
        }
    }
}
namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AssetsTabView : UserControl
    {
        public AssetsTabView()
        {
            InitializeComponent();
            DataContext = new { IsExternal = false };
            InitializePackCardFx();
        }
        public Border Card(int n) => this.FindControl<Border>("Card" + n)!;
    }
}
