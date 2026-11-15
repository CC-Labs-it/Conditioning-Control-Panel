// WPF: Services/Possession/Effects/DeleteDialogEffect.cs:30-170, 340-414.
using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    public partial class DeleteDialogWindow : Window
    {
        internal static readonly string[] FakeNames = { "Velvet Reverie", "Crimson Daydream", "Ember Waltz", "Midnight Bloom" };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1775) };
        private readonly TextBlock[] _rows;
        private int _index;
        private readonly string[] _names;

        public DeleteDialogWindow() : this(FakeNames) { }

        internal DeleteDialogWindow(string[] names)
        {
            _names = names;
            InitializeComponent();
            var list = this.FindControl<StackPanel>("Names")!;
            _rows = new TextBlock[_names.Length];
            for (int i = 0; i < _names.Length; i++)
            {
                _rows[i] = new TextBlock { Text = "  " + _names[i], Foreground = Brush.Parse("#C09A92"), FontSize = 12 };
                list.Children.Add(_rows[i]);
            }
            this.FindControl<TextBlock>("Status")!.Text = Loc.GetF("possession_delete_removing", _names[0]);
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
                : Loc.GetF("possession_delete_removing", _names[_index]);
            if (_index == _rows.Length) _timer.Interval = TimeSpan.FromSeconds(3);
        }
    }
}

namespace ConditioningControlPanel.Avalonia.Possession
{
    internal sealed class DeleteDialogEffect : PossessionEffectBase
    {
        private Views.Dialogs.DeleteDialogWindow? _dialog;

        internal static string[] ReadSessionNames(string folder)
        {
            var names = new List<string>();
            try
            {
                if (Directory.Exists(folder))
                {
                    var files = new SessionFileService();
                    foreach (var path in Directory.GetFiles(folder, "*.session.json"))
                    {
                        string? name = null;
                        try { name = files.ImportSession(path)?.Name; } catch { }
                        if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                        if (names.Count >= 6) break;
                    }
                }
                foreach (var session in ConditioningControlPanel.Models.Session.GetAllSessions())
                {
                    if (names.Count >= 6) break;
                    var name = session?.Name;
                    if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                }
            }
            catch { }
            return names.Count > 0 ? names.ToArray() : Views.Dialogs.DeleteDialogWindow.FakeNames;
        }
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
            var dialog = _dialog = new Views.Dialogs.DeleteDialogWindow(ReadSessionNames(SessionFileService.CustomSessionsFolder));
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
