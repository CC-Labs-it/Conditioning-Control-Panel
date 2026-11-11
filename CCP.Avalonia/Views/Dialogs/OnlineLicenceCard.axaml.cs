using Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// The one-time licence card (owner, 2026-10-21). Shown from the startup ladder while
    /// <see cref="OnlineMeterRules"/> says the official note is due; the caller marks it seen
    /// (MainShellWindow.OnlineMeter.cs OfferOnlineLicenceCard).
    /// <para>PORTED from ConditioningControlPanel/Dialogs/OnlineLicenceCard.xaml.cs, 1:1.</para>
    /// </summary>
    public partial class OnlineLicenceCard : Window
    {
        /// <summary>The "what keeps it flowing" lines, in order.</summary>
        internal static readonly string[] FlowKeys =
        {
            "online_official_flow_kept", "online_official_flow_mix",
            "online_official_flow_spent", "online_official_flow_local",
        };

        /// <summary>Render constructor (--render-view): the Free plan, as the WPF harness draws it.</summary>
        internal OnlineLicenceCard() : this(ScrolllerPlan.Free) { }

        internal OnlineLicenceCard(ScrolllerPlan plan)
        {
            InitializeComponent();
            var lines = new string[FlowKeys.Length];
            for (var i = 0; i < FlowKeys.Length; i++) lines[i] = Loc.Get(FlowKeys[i]);
            FlowLines.ItemsSource = lines;
            TxtPlan.Text = Loc.Get(OnlineMeterRules.OfficialPlanKey(plan));
            BtnNice.Click += (_, _) => Close();
        }
    }
}
