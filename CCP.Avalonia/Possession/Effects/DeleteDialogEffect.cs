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
        internal static readonly string[] Captions = { "tidying up after you", "removing what you do not need", "housekeeping, sweetheart" };
        internal static readonly string[] FakeNames = { "Velvet Reverie", "Crimson Daydream", "Ember Waltz", "Midnight Bloom" };
        private readonly DispatcherTimer _timer = new();
        private readonly DispatcherTimer _failsafe = new();
        private readonly TaskCompletionSource _walk = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TextBlock[] _rows;
        private int _index;
        private readonly string[] _names;

        public DeleteDialogWindow() : this(FakeNames, TimeSpan.FromSeconds(8)) { }

        internal DeleteDialogWindow(string[] names, TimeSpan? hold = null, Random? rng = null)
        {
            _names = names;
            var holdFor = hold ?? TimeSpan.FromSeconds(8);
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(280, (holdFor.TotalMilliseconds - 900) / names.Length));
            _failsafe.Interval = holdFor + TimeSpan.FromSeconds(3);
            InitializeComponent();
            this.FindControl<TextBlock>("Caption")!.Text = Captions[(rng ?? Random.Shared).Next(Captions.Length)];
            var list = this.FindControl<StackPanel>("Names")!;
            _rows = new TextBlock[_names.Length];
            for (int i = 0; i < _names.Length; i++)
            {
                _rows[i] = new TextBlock { Text = "  " + _names[i], Foreground = Brush.Parse("#C09A92"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
                list.Children.Add(_rows[i]);
            }
            this.FindControl<TextBlock>("Status")!.Text = Loc.GetF("possession_delete_removing", _names[0]);
            this.FindControl<Button>("Cancel")!.Click += (_, _) => Close();
            _timer.Tick += (_, _) => Step();
            _failsafe.Tick += (_, _) => Close();
            Opened += (_, _) => { _timer.Start(); _failsafe.Start(); };
            Closed += (_, _) => { _timer.Stop(); _failsafe.Stop(); _walk.TrySetResult(); };
        }

        internal Task Walk => _walk.Task;

        internal void Step()
        {
            if (_index >= _rows.Length) { Close(); return; }
            _rows[_index].TextDecorations = TextDecorations.Strikethrough;
            _rows[_index].Opacity = 0.35;
            _index++;
            this.FindControl<ProgressBar>("Progress")!.Value = 100.0 * _index / _rows.Length;
            this.FindControl<TextBlock>("Status")!.Text = _index == _rows.Length
                ? Loc.Get("possession_delete_closing")
                : Loc.GetF("possession_delete_removing", _names[_index]);
            if (_index == _rows.Length)
            {
                _timer.Stop();
                _walk.TrySetResult();
            }
        }
    }
}

namespace ConditioningControlPanel.Avalonia.Possession
{
    internal sealed class DeleteDialogEffect : PossessionEffectBase
    {
        private Views.Dialogs.DeleteDialogWindow? _dialog;
        private readonly Func<string[]> _names;
        public DeleteDialogEffect() : this(() => ReadSessionNames(SessionFileService.CustomSessionsFolder)) { }
        internal DeleteDialogEffect(Func<string[]> names) => _names = names;

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
        protected override string? NameOverride => "your sessions";
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(8);
        protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
            _dialog == null && (ctx.Host as IAvaloniaPossessionHost)?.PossessionWindow is { IsVisible: true };
        protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            var owner = (ctx.Host as IAvaloniaPossessionHost)?.PossessionWindow;
            if (owner == null) return;
            var dialog = _dialog = new Views.Dialogs.DeleteDialogWindow(_names(), HoldFor, ctx.Rng);
            dialog.Closed += (_, _) => { if (ReferenceEquals(_dialog, dialog)) _dialog = null; try { ctx.Name("deletedialog_closed", "your sessions"); } catch { } };
            dialog.Show(owner);
            await dialog.Walk.WaitAsync(ct).ConfigureAwait(true);
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
