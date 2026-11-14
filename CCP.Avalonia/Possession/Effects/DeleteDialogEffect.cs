// WPF: Services/Possession/Effects/DeleteDialogEffect.cs:30-170, 340-414.
using System;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    // The parameterless constructor supplies preview data only. Production passes session names.
    public partial class DeleteDialogWindow : Window
    {
        internal static readonly string[] FakeNames = { "Velvet Reverie", "Crimson Daydream", "Ember Waltz", "Midnight Bloom" };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly Stopwatch _clock = new();
        private readonly IReadOnlyList<string> _names;
        private readonly TextBlock[] _rows;
        private int _index;

        public DeleteDialogWindow() : this(FakeNames, 2, null) { }

        internal DeleteDialogWindow(IReadOnlyList<string> names, int caption, IImage? portrait)
        {
            _names = names;
            InitializeComponent();
            this.FindControl<TextBlock>("Caption")!.Text = Loc.Get(new[] { "possession_delete_caption_tidy", "possession_delete_caption_remove", "possession_delete_caption_housekeeping" }[caption]);
            this.FindControl<Image>("Portrait")!.Source = portrait;
            this.FindControl<Control>("Fallback")!.IsVisible = portrait == null;
            var list = this.FindControl<StackPanel>("Names")!;
            _rows = new TextBlock[_names.Count];
            for (int i = 0; i < _names.Count; i++)
            {
                _rows[i] = new TextBlock { Text = "  " + _names[i], Foreground = Brush.Parse("#C09A92"), FontSize = 12 };
                list.Children.Add(_rows[i]);
            }
            this.FindControl<TextBlock>("Status")!.Text = Loc.GetF("possession_delete_removing", _names[0]);
            this.FindControl<Button>("Cancel")!.Click += (_, _) => Close();
            _timer.Tick += (_, _) => Advance(_clock.Elapsed.TotalMilliseconds);
            Opened += (_, _) => { _clock.Start(); _timer.Start(); };
            Closed += (_, _) => { _timer.Stop(); _clock.Stop(); };
        }

        internal void Advance(double milliseconds)
        {
            double perName = Math.Max(280, 7100.0 / _rows.Length);
            while (_index < _rows.Length && milliseconds >= (_index + 1) * perName) Step();
            if (milliseconds >= 11000) { Close(); return; }
            if (_index < _rows.Length)
            {
                double t = Math.Clamp((milliseconds - _index * perName) / perName, 0, 1);
                double eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                this.FindControl<ProgressBar>("Progress")!.Value = 100 * (_index + eased) / _rows.Length;
            }
        }

        internal void Step()
        {
            if (_index >= _rows.Length) { Close(); return; }
            _rows[_index].TextDecorations = TextDecorations.Strikethrough;
            _rows[_index].Opacity = 0.35;
            _index++;
            this.FindControl<ProgressBar>("Progress")!.Value = 100.0 * _index / _rows.Length;
            this.FindControl<TextBlock>("Status")!.Text = _index == _rows.Length
                ? Loc.Get("possession_delete_complete")
                : Loc.GetF("possession_delete_removing", _names[_index]);

        }
    }
}

namespace ConditioningControlPanel.Avalonia.Possession
{
    internal sealed class DeleteDialogEffect : PossessionEffectBase
    {
        private Views.Dialogs.DeleteDialogWindow? _dialog;
        private IReadOnlyList<string> _names = Array.Empty<string>();
        internal Func<IReadOnlyList<string>> Names = () => ReadNames(Services.SessionFileService.CustomSessionsFolder, Models.Session.GetAllSessions().Select(s => s.Name));

        // The import service may substitute a filename for a missing name. Never display that fallback.
        internal static IReadOnlyList<string> ReadNames(string folder, IEnumerable<string> builtins)
        {
            var names = new List<string>();
            void Add(string? name)
            {
                if (!string.IsNullOrWhiteSpace(name) && name.IndexOfAny(new[] { '/', '\\' }) < 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase) && names.Count < 6) names.Add(name);
            }
            try
            {
                if (Directory.Exists(folder))
                {
                    var service = new Services.SessionFileService();
                    foreach (var path in Directory.GetFiles(folder, "*.session.json"))
                    {
                        try
                        {
                            var name = service.ImportSession(path)?.Name?.Trim();
                            var stem = Path.GetFileName(path)[..^".session.json".Length];
                            if (!string.Equals(name, stem, StringComparison.OrdinalIgnoreCase) && !string.Equals(name, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) Add(name);
                            if (names.Count == 6) break;
                        }
                        catch (Exception ex) { Serilog.Log.Debug("Possession session read failed: {Error}", ex.Message); }
                    }
                }
                foreach (var name in builtins) Add(name);
            }
            catch (Exception ex) { Serilog.Log.Warning("Possession session name read failed: {Error}", ex.Message); }
            return names;
        }
        public override string Id => "deletedialog";
        public override PossessionRung MinRung => PossessionRung.ItKnows;
        public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
        public override bool IsBig => true;
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(11);
        protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
        {
            if (_dialog != null || (ctx.Host as IAvaloniaPossessionHost)?.PossessionWindow is not { IsVisible: true }) return false;
            _names = Names();
            return _names.Count > 0;
        }
        protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
        {
            var owner = (ctx.Host as IAvaloniaPossessionHost)?.PossessionWindow;
            if (owner == null) return Task.CompletedTask;
            if (_names.Count == 0) _names = Names();
            if (_names.Count == 0) return Task.CompletedTask;
            var portrait = Views.AvatarTube.AvatarTubeWindow.Live?.FindControl<Image>("ImgAvatar")?.Source;
            var dialog = _dialog = new Views.Dialogs.DeleteDialogWindow(_names, ctx.Rng.Next(3), portrait);
            dialog.Closed += (_, _) => { if (ReferenceEquals(_dialog, dialog)) _dialog = null; try { ctx.Name("deletedialog_closed", "your sessions"); } catch { } };
            dialog.Show(owner);
            return Task.CompletedTask;
        }
        protected override Task UndoCoreAsync(TimeSpan duration)
        {
            var dialog = _dialog;
            _dialog = null;
            _names = Array.Empty<string>();
            dialog?.Close();
            return Task.CompletedTask;
        }
    }
}
