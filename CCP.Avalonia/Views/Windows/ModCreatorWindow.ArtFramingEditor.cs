using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class ModCreatorWindow
    {
        private readonly Dictionary<string, Button> _frameButtons = new(StringComparer.OrdinalIgnoreCase);

        private Button CreateFrameButton(string slot)
        {
            var button = new Button
            {
                Content = Loc.Get("mod_art_frame"), FontSize = 9, Padding = new Thickness(4, 1),
                Background = Brush.Parse("#BE1E1E32"), Foreground = Brush.Parse("#B0B0C8"),
                BorderThickness = new Thickness(0), HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom, Margin = new Thickness(2), IsVisible = false,
                Tag = slot,
            };
            button.Click += async (_, _) => await OpenFramingEditor(slot);
            _frameButtons[slot] = button;
            return button;
        }

        private void UpdateFrameButton(string slot)
        {
            if (!_frameButtons.TryGetValue(slot, out var button)) return;
            button.IsVisible = _imageSlots.TryGetValue(slot, out var path) && !string.IsNullOrEmpty(path);
            var framed = _artFraming.TryGetValue(slot, out var values) && values.Count > 0;
            button.Content = Loc.Get(framed ? "mod_art_framed" : "mod_art_frame");
            button.Foreground = Brush.Parse(framed ? "#FF69B4" : "#B0B0C8");
        }

        private async System.Threading.Tasks.Task OpenFramingEditor(string slot)
        {
            if (!_imageSlots.TryGetValue(slot, out var path) || string.IsNullOrEmpty(path)) return;
            Bitmap bitmap;
            try { bitmap = new Bitmap(path); }
            catch
            {
                await MessageDialog.ShowAsync(this, Loc.Get("mod_art_frame_title"), Loc.Get("mod_art_frame_decode"));
                return;
            }
            using (bitmap)
            {
                _artFraming.TryGetValue(slot, out var stored);
                var working = BeginFramingEdit(slot, stored);
                if (working.Count == 0) return;
                var dialog = CreateFramingDialog(slot, bitmap, working);
                if (await dialog.ShowDialog<bool>(this))
                {
                    CommitFramingEdit(_artFraming, slot, working);
                    UpdateFrameButton(slot);
                    UpdateStatusBar();
                }
            }
        }

        internal Window CreateFramingDialog(string slot, Bitmap bitmap, Dictionary<string, ModArtFraming> working)
        {
            var aspect = (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;
            var body = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
            body.Children.Add(new TextBlock { Text = Loc.Get("mod_art_frame_help"), TextWrapping = TextWrapping.Wrap, MaxWidth = 780 });
            var cards = new WrapPanel();
            var resets = new List<Action>();
            var caption = _imageNames.TryGetValue(slot, out var name) ? name : System.IO.Path.GetFileNameWithoutExtension(slot);
            var paren = caption.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) caption = caption[..paren];
            foreach (var (id, framing) in working)
            {
                var surface = ModArtFramingRegistry.FindSurface(id);
                if (surface == null) continue;
                var width = Math.Min(300, 160 * surface.AspectRatio);
                var height = width / surface.AspectRatio;
                var card = new StackPanel { Margin = new Thickness(0, 0, 14, 14), Width = width, Spacing = 4, Tag = id };
                card.Children.Add(new TextBlock { Text = surface.DisplayName });
                var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
                var holder = new Grid { Width = width, Height = height, Background = Brushes.Transparent, ClipToBounds = true };
                holder.Children.Add(new Border { Background = brush, CornerRadius = new CornerRadius(surface.CornerRadius), IsHitTestVisible = false });
                if (surface.Scrim != ArtScrim.None)
                {
                    holder.Children.Add(new Border { Background = Brush.Parse("#99000000"),
                        CornerRadius = new CornerRadius(surface.CornerRadius), VerticalAlignment = surface.Scrim == ArtScrim.FootBand ? global::Avalonia.Layout.VerticalAlignment.Bottom : global::Avalonia.Layout.VerticalAlignment.Stretch,
                        Height = surface.Scrim == ArtScrim.FootBand ? height * .3 : double.NaN, IsHitTestVisible = false });
                    holder.Children.Add(new TextBlock { Text = surface.Scrim == ArtScrim.FullFace ? caption + "\n−   15m   +" : caption,
                        Foreground = Brushes.White, TextAlignment = TextAlignment.Center, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = surface.Scrim == ArtScrim.FullFace ? global::Avalonia.Layout.VerticalAlignment.Top : global::Avalonia.Layout.VerticalAlignment.Bottom, IsHitTestVisible = false });
                }
                if (surface.FluidWidth) holder.Children.Add(new Rectangle { Margin = new Thickness(0, height * .15), Stroke = Brush.Parse("#E8D8B0"), StrokeThickness = 1, StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 4, 3 }, IsHitTestVisible = false });
                holder.Children.Add(new Border { CornerRadius = new CornerRadius(surface.CornerRadius), BorderBrush = Brush.Parse("#505070"), BorderThickness = new Thickness(1), IsHitTestVisible = false });
                card.Children.Add(holder);
                if (surface.FluidWidth) card.Children.Add(new TextBlock { Text = Loc.Get("mod_art_frame_fluid"), TextWrapping = TextWrapping.Wrap, FontSize = 10 });
                var readout = new TextBlock { FontSize = 10 };
                card.Children.Add(readout);
                void Refresh()
                {
                    var box = ModArtFramingRegistry.ToViewbox(framing, aspect, surface.AspectRatio);
                    brush.SourceRect = new RelativeRect(box.X, box.Y, box.Width, box.Height, RelativeUnit.Relative);
                    readout.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.###}, {1:0.###}  /  {2:0.##}x", framing.CenterX, framing.CenterY, framing.Zoom);
                }
                void Reset() { framing.CenterX = .5; framing.CenterY = .5; framing.Zoom = 1; Refresh(); }
                resets.Add(Reset);
                var reset = new Button { Content = Loc.Get("mod_art_frame_reset") };
                reset.Click += (_, _) => Reset();
                card.Children.Add(reset);
                Point? start = null; double x = .5, y = .5;
                holder.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(holder).Properties.IsLeftButtonPressed) return;
                    if (e.ClickCount >= 2) { Reset(); start = null; e.Pointer.Capture(null); }
                    else { start = e.GetPosition(holder); x = framing.CenterX; y = framing.CenterY; e.Pointer.Capture(holder); }
                    e.Handled = true;
                };
                holder.PointerMoved += (_, e) =>
                {
                    if (start == null) return;
                    var p = e.GetPosition(holder);
                    PanFraming(framing, aspect, surface.AspectRatio, x, y, (p.X - start.Value.X) / width, (p.Y - start.Value.Y) / height);
                    Refresh();
                };
                holder.PointerReleased += (_, e) => { start = null; e.Pointer.Capture(null); };
                holder.PointerCaptureLost += (_, _) => start = null;
                holder.PointerWheelChanged += (_, e) => { if (e.Delta.Y == 0) return; ZoomFraming(framing, aspect, surface.AspectRatio, e.Delta.Y > 0); Refresh(); e.Handled = true; };
                Refresh();
                cards.Children.Add(card);
            }
            body.Children.Add(cards);
            var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };
            var resetAll = new Button { Content = Loc.Get("mod_art_frame_reset_all") };
            var cancel = new Button { Content = Loc.Get("btn_cancel") };
            var ok = new Button { Content = Loc.Get("btn_ok") };
            row.Children.Add(resetAll); row.Children.Add(cancel); row.Children.Add(ok); body.Children.Add(row);
            var dialog = new Window { Title = Loc.Get("mod_art_frame_title"), Width = 850, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer { Content = body } };
            resetAll.Click += (_, _) => { foreach (var reset in resets) reset(); };
            cancel.Click += (_, _) => dialog.Close(false);
            ok.Click += (_, _) => dialog.Close(true);
            return dialog;
        }

        // Working copies keep pointer edits isolated until the dialog is accepted.
        internal static Dictionary<string, ModArtFraming> BeginFramingEdit(
            string slot, IReadOnlyDictionary<string, ModArtFraming>? stored)
        {
            var result = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
            foreach (var binding in ModArtFramingRegistry.FramableBindingsFor(slot))
                if (!result.ContainsKey(binding.SurfaceId))
                    result[binding.SurfaceId] = stored != null && stored.TryGetValue(binding.SurfaceId, out var value)
                        ? ModArtFramingRules.Sanitize(value) : new ModArtFraming();
            return result;
        }

        internal static void CommitFramingEdit(
            Dictionary<string, Dictionary<string, ModArtFraming>> destination, string slot,
            IReadOnlyDictionary<string, ModArtFraming> working)
        {
            var kept = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
            foreach (var (surface, value) in working)
            {
                if (!ModArtFramingRegistry.IsFramable(slot, surface)) continue;
                var copy = ModArtFramingRules.Sanitize(value);
                if (!copy.IsDefault) kept[surface] = copy;
            }
            if (kept.Count == 0) destination.Remove(slot);
            else destination[slot] = kept;
        }

        internal static double ClampFramingCenter(double value, double size)
        {
            if (double.IsNaN(value) || double.IsNaN(size) || size <= 0 || size >= 1) return 0.5;
            return Math.Clamp(value, size / 2, 1 - size / 2);
        }

        internal static void PanFraming(ModArtFraming framing, double sourceAspect, double surfaceAspect,
            double startX, double startY, double deltaX, double deltaY)
        {
            var window = ModArtFramingRegistry.ToViewbox(framing, sourceAspect, surfaceAspect);
            framing.CenterX = ClampFramingCenter(startX - deltaX * window.Width, window.Width);
            framing.CenterY = ClampFramingCenter(startY - deltaY * window.Height, window.Height);
        }

        internal static void ZoomFraming(ModArtFraming framing, double sourceAspect, double surfaceAspect, bool zoomIn)
        {
            framing.Zoom = Math.Clamp(framing.Zoom * (zoomIn ? 1.12 : 1 / 1.12), 1, ModArtFramingRegistry.MaxZoom);
            var window = ModArtFramingRegistry.ToViewbox(framing, sourceAspect, surfaceAspect);
            framing.CenterX = ClampFramingCenter(framing.CenterX, window.Width);
            framing.CenterY = ClampFramingCenter(framing.CenterY, window.Height);
        }
    }
}
