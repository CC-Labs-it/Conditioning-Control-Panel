using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Themed dialog for starting a roadmap step.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/RoadmapStartDialog.xaml.cs. Deviations:
    ///  - WPF's <c>DialogResult</c> becomes <c>Close(bool)</c>; Avalonia carries the result
    ///    through <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - The step icon is set from code for BOTH step types, not just Boss: see the comment in
    ///    the .axaml for why the {loc:Str} binding cannot stay under it.
    ///  - The Click handlers are wired in the constructor rather than in markup.
    /// </summary>
    public partial class RoadmapStartDialog : Window
    {
        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        internal RoadmapStartDialog()
            : this(new RoadmapStepDefinition("t1_step1", RoadmapTrack.EmptyDoll, 1, "The Blank Slate",
                "Sit still for five minutes and let every thought drain away.",
                "A photo of your empty desk, taken from above.")) { }

        public RoadmapStartDialog(RoadmapStepDefinition stepDef)
        {
            AvaloniaXamlLoader.Load(this);

            // Set icon based on step type
            // WPF: Boss "🏆" else label_text_9 "📷".
            if (stepDef.StepType == RoadmapStepType.Boss)
            {
                var icon = this.FindControl<IconGlyph>("TxtStepIcon")!;
                icon.Kind = IconKind.Trophy;
                icon.Variant = IconVariant.Filled;
                icon.Bind(IconGlyph.ForegroundProperty, icon.GetResourceObservable(IconBrushes.Gold));
            }

            this.FindControl<TextBlock>("TxtStepTitle")!.Text = stepDef.StepType == RoadmapStepType.Boss
                ? $"BOSS: {stepDef.Title}"
                : $"Step {stepDef.StepNumber}: {stepDef.Title}";

            this.FindControl<TextBlock>("TxtObjective")!.Text = stepDef.Objective;
            this.FindControl<TextBlock>("TxtPhotoRequirement")!.Text = stepDef.PhotoRequirement;

            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnStart")!.Click += (_, _) => Close(true);
        }
    }
}
