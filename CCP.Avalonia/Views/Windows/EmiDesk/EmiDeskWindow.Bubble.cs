using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;
namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    // WPF EmiDeskWindow.Bubble.cs:40-493. Voice remains missing; the book dodge is WPF :346-361.
    public partial class EmiDeskWindow
    {
        private static readonly IBrush BubbleFill = new ImmutableSolidColorBrush(Color.FromRgb(0x18, 0x18, 0x32));
        private static readonly IBrush BubbleInk = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
        private const double BubbleLeftFrac = 0.58, BubbleBottomFrac = 0.96;
        private const double BubbleFontAtDefaultWidth = 11.0, BubbleFontFloor = 10.0, BubbleFontRefWidth = 220.0;
        private const double BubbleMinWidth = 220.0, BubbleMaxWidth = 380.0, BubbleWidthOfBody = 1.5;
        /// <summary>WPF EmiFace.PixelFont: Press Start 2P (shipped), then the mono chain.</summary>
        private static readonly FontFamily PixelFont = new(
            "avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Noto Sans Mono, Cascadia Mono, Consolas, Courier New, monospace");
        private Border? _bubble;
        private TextBlock? _bubbleText;
        private Polygon? _bubbleTail;
        private StackPanel? _bubbleContent;
        private StackPanel? _askChips;
        private AskDraw? _ask;
        private EmiLineEngine? _askEngine;
        internal bool AskLive => _ask != null;

        internal void ShowAsk(AskDraw ask, EmiLineEngine engine)
        {
            if (!IsVisible || PresentationActive) return;
            CancelChain();
            CancelHold();
            EnsureBubble();
            _ask = ask;
            _askEngine = engine;
            engine.Ack(ask.Id);
            DrawFace(ask.Face);
            ShowBubble(ask.Question);
            _askChips!.Children.Clear();
            for (int i = 0; i < 2; i++)
            {
                int answer = i;
                var button = new Button { Content = ask.Chips[i], Margin = new Thickness(0, 4, 4, 0) };
                button.Click += (_, _) => AnswerAsk(answer);
                _askChips.Children.Add(button);
            }
            _askChips.IsVisible = true;
            _bubble!.IsHitTestVisible = true;
            _bubbleCanvas.IsHitTestVisible = true;
            LayoutBubble();
            (_askChips.Children[0] as Button)?.Focus();
        }
        internal void AnswerAsk(int index)
        {
            var ask = _ask;
            var engine = _askEngine;
            if (ask == null || engine == null) return;
            ClearAsk();
            engine.NoteAskAnswered();
            EmiDeskService.Instance.Fire("askAnswered", new { answer = index == 0 ? "yes" : "no" });
            SpeakLine(index == 0 ? ask.Yes : ask.No, engine);
            if (index == 0 && string.Equals(ask.Effect, "pinTop:fyp", StringComparison.OrdinalIgnoreCase))
            {
                EmiSuggester.PinToTop("fyp");
                RebuildRing();
                EmiDeskService.Instance.Fire("pinAdded", new { target = "fyp", fromAsk = true });
            }
        }
        internal void CancelAsk()
        {
            if (_ask == null) return;
            _askEngine?.NoteAskIgnored();
            ClearAsk();
        }
        private void ClearAsk()
        {
            _ask = null;
            _askEngine = null;
            if (_askChips != null) { _askChips.Children.Clear(); _askChips.IsVisible = false; }
            if (_bubble != null) _bubble.IsHitTestVisible = false;
            _bubbleCanvas.IsHitTestVisible = false;
            HideBubble();
        }
        /// <summary>The bubble's text while it is up, else null (test seam).</summary>
        internal string? BubbleText => _bubble is { IsVisible: true } ? _bubbleText?.Text : null;
        partial void OnBubbleTextCore(string? text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) { HideBubble(); return; }
                ShowBubble(text!);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bubble paint failed"); }
        }
        private void EnsureBubble()
        {
            if (_bubble != null) return;
            KeyDown += (_, e) => { if (e.Key == Key.Escape && AskLive) { CancelAsk(); e.Handled = true; } };
            _bubbleText = new TextBlock
            {
                Foreground = BubbleInk,
                FontFamily = PixelFont,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
            _askChips = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, IsVisible = false };
            _bubbleContent = new StackPanel();
            _bubbleContent.Children.Add(_bubbleText);
            _bubbleContent.Children.Add(_askChips);
            _bubble = new Border
            {
                Background = BubbleFill,
                BorderBrush = BubbleInk,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 8, 10, 8),
                IsHitTestVisible = false,
                Child = _bubbleContent,
                IsVisible = false
            };
            _bubbleTail = new Polygon
            {
                Fill = BubbleFill,
                Stroke = BubbleInk,
                StrokeThickness = 1,
                IsHitTestVisible = false,
                IsVisible = false
            };
            _bubbleCanvas.Children.Add(_bubbleTail);
            _bubbleCanvas.Children.Add(_bubble);
            _bubble.SizeChanged += (_, _) => LayoutBubble();
            Resized += (_, _) => LayoutBubble();
            Moved += (_, _) => LayoutBubble();
        }
        private void ShowBubble(string text)
        {
            EnsureBubble();
            if (_bubble == null || _bubbleText == null) return;
            _bubbleText.Text = text;
            _bubble.IsVisible = true;
            if (_bubbleTail != null) _bubbleTail.IsVisible = true;
            LayoutBubble();
        }
        /// <summary>WPF EmiBook.SideChanged subscriber: the book opened, closed or changed side.</summary>
        internal void OnBookSideChanged() => LayoutBubble();

        private void HideBubble()
        {
            if (_bubble != null) _bubble.IsVisible = false;
            if (_bubbleTail != null) _bubbleTail.IsVisible = false;
        }
        private void LayoutBubble()
        {
            try
            {
                if (_bubble == null || _bubbleText == null || !_bubble.IsVisible) return;
                double bw = BodyWidth;
                double bh = bw * BodyAspect;
                double fs = Math.Max(BubbleFontFloor, Math.Round(BubbleFontAtDefaultWidth * bw / BubbleFontRefWidth));
                _bubbleText.FontSize = fs;
                _bubbleText.LineHeight = Math.Round(fs * 1.4);
                _bubble.MaxWidth = Math.Max(BubbleMinWidth, Math.Min(BubbleMaxWidth, bw * BubbleWidthOfBody));
                _bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = new Size(Math.Max(_bubble.DesiredSize.Width, _bubble.Bounds.Width),
                                    Math.Max(_bubble.DesiredSize.Height, _bubble.Bounds.Height));
                double bodyX = OverlayPadX, bodyY = OverlayPad;
                double left = bodyX + bw * BubbleLeftFrac;
                double bottom = bodyY + bh - bh * BubbleBottomFrac;
                double top = bottom - size.Height;
                double winW = double.IsNaN(Width) ? Bounds.Width : Width;
                double workLeft = double.NegativeInfinity, workRight = double.PositiveInfinity;
                if (Screens.ScreenFromWindow(this) is { } screen)
                {
                    workLeft = this.PointToClient(screen.WorkingArea.TopLeft).X;
                    workRight = this.PointToClient(screen.WorkingArea.TopRight).X;
                }
                // WPF :355-361 THE BOOK DODGE: the book on her right (+1) sends the bubble to her left;
                // the screen-edge arbitration still has the final word.
                int bookSide = 0;
                try { bookSide = _book?.SideOfHer ?? 0; }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book side unreadable, bubble keeps its default"); }
                var placement = EmiBubblePlacement.Resolve(
                    left, bodyX + bw * (1.0 - BubbleLeftFrac) - size.Width, size.Width,
                    bookSide > 0, winW, workLeft, workRight);
                left = placement.Left;
                bool flip = placement.Flip;
                if (top < 2) top = 2;
                Canvas.SetLeft(_bubble, Math.Round(left));
                Canvas.SetTop(_bubble, Math.Round(top));
                LayoutTail(left, top + size.Height, size.Width, flip);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bubble layout failed"); }
        }
        private void LayoutTail(double left, double bubbleBottom, double bubbleWidth, bool flip)
        {
            if (_bubbleTail == null) return;
            const double w = 10, h = 10;
            double x = flip ? left + bubbleWidth - w : left;
            double y = bubbleBottom - 1;
            _bubbleTail.Points = flip
                ? new Point[] { new(0, 0), new(w, 0), new(w, h) }
                : new Point[] { new(w, 0), new(0, 0), new(0, h) };
            Canvas.SetLeft(_bubbleTail, Math.Round(x));
            Canvas.SetTop(_bubbleTail, Math.Round(y));
        }
    }
}
