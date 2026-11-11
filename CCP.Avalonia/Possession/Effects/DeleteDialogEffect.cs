// WPF: Services/Possession/Effects/DeleteDialogEffect.cs:30-170, 340-414.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    // The names are invented. No session store, path, or file is consulted.
    public partial class DeleteDialogWindow : Window
    {
        internal static readonly string[] FakeNames = { "Velvet Reverie", "Crimson Daydream", "Ember Waltz", "Midnight Bloom" };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1775) };
        private readonly TextBlock[] _rows;
        private int _index;

        public DeleteDialogWindow()
        {
            InitializeComponent();
            var list = this.FindControl<StackPanel>("Names")!;
            _rows = new TextBlock[FakeNames.Length];
            for (int i = 0; i < FakeNames.Length; i++)
            {
                _rows[i] = new TextBlock { Text = "  " + FakeNames[i], Foreground = Brush.Parse("#C09A92"), FontSize = 12 };
                list.Children.Add(_rows[i]);
            }
            this.FindControl<TextBlock>("Status")!.Text = Loc.GetF("possession_delete_removing", FakeNames[0]);
            this.FindControl<Button>("Cancel")!.Click += (_, _) => Close();
            _timer.Tick += (_, _) => Step();
            Opened += (_, _) => _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        internal void Step()
        {
            if (_index >= _rows.Length) { Close(); return; }
            _rows[_index].TextDecorations = TextDecorations.Strikethrough;
            _rows[_index].Opacity = 0.35;
            _index++;
            this.FindControl<ProgressBar>("Progress")!.Value = 100.0 * _index / _rows.Length;
            this.FindControl<TextBlock>("Status")!.Text = _index == _rows.Length
                ? "nothing was deleted. i just wanted to watch you read it."
                : Loc.GetF("possession_delete_removing", FakeNames[_index]);
            if (_index == _rows.Length) _timer.Interval = TimeSpan.FromSeconds(3);
        }
    }
}

namespace ConditioningControlPanel.Avalonia.Possession
{
    internal sealed class DeleteDialogEffect : PossessionEffectBase
    {
        private Views.Dialogs.DeleteDialogWindow? _dialog;
        public override string Id => "deletedialog";
        public override PossessionRung MinRung => PossessionRung.ItKnows;
        public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
        public override bool IsBig => true;
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(8);
        protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
            _dialog == null && (ctx.Host as IAvaloniaPossessionHost)?.PossessionWindow is { IsVisible: true };
        protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            var owner = (ctx.Host as IAvaloniaPossessionHost)?.PossessionWindow;
            if (owner == null) return Task.CompletedTask;
            var dialog = _dialog = new Views.Dialogs.DeleteDialogWindow();
            dialog.Closed += (_, _) => { if (ReferenceEquals(_dialog, dialog)) _dialog = null; try { ctx.Name("deletedialog_closed", "your sessions"); } catch { } };
            dialog.Show(owner);
            return Task.CompletedTask;
        }
        protected override Task UndoCoreAsync(TimeSpan duration)
        {
            var dialog = _dialog;
            _dialog = null;
            dialog?.Close();
            return Task.CompletedTask;
        }
    }
}
