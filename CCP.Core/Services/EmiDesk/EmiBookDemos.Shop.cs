using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BOOK DEMO BATCH: the loops for <see cref="EmiBookDeckShop"/>. Full-bleed like the other DEEPER
/// cards: both are places that take the screen, not effects over the desktop.
/// </summary>
internal static class EmiBookDemosShop
{
    /// <summary>This batch's painters.</summary>
    public static readonly EmiDemoPainter[] All = { new CounterDemo(), new RainDemo() };

    // =====================================================================================
    //  THE PRIZE COUNTER  -  a sparkle drops in, one shelf item is stamped yours
    // =====================================================================================

    /// <summary>THE SENTENCE: "sparkles go in, a thing on the shelf becomes yours." Two shelves of
    /// three, a coin falls into the till, and the middle item takes a pink stamp that stays.</summary>
    private sealed class CounterDemo : EmiDemoPainter
    {
        public override string Id => "prize-counter";
        public override int LoopMs => 6000;
        public override double StillMs => 4200;

        public override void Draw(EmiPixelCanvas p, double t)
        {
            p.Clear(EmiPix.Navy);
            // Two shelves, three items each.
            for (int row = 0; row < 2; row++)
            {
                int sy = 8 + row * 22;
                p.Rect(6, sy + 16, 60, 2, EmiPix.Mid);
                for (int i = 0; i < 3; i++)
                {
                    int x = 10 + i * 19;
                    EmiPix.PixImage(p, x, sy, 13, 15, row * 3 + i);
                }
            }

            // The till on the right, with its slot.
            p.Rect(72, 40, 20, 24, EmiPix.Mid);
            p.Rect(74, 42, 16, 6, EmiPix.Dark);
            p.Rect(78, 40, 8, 1, EmiPix.Ink);

            // A sparkle falls into the slot between 1.0 s and 2.2 s.
            if (t >= 1000 && t < 2200)
            {
                double k = (t - 1000) / 1200.0;
                int cy = (int)(6 + k * 32);
                p.Rect(80, cy, 4, 4, EmiPix.Gold);
                p.Px(82, cy - 2, EmiPix.Cream);
                p.Px(85, cy + 1, EmiPix.Cream);
            }

            // From 2.4 s the middle item on the top shelf is stamped: a pink frame and a check.
            if (t >= 2400)
            {
                double a = Math.Min(1.0, (t - 2400) / 400.0);
                p.RectA(27, 6, 17, 19, EmiPix.Pink, a);
                EmiPix.PixImage(p, 29, 8, 13, 15, 1);
                if (t >= 2800)
                {
                    p.Rect(38, 20, 2, 2, EmiPix.Cream);
                    p.Rect(40, 18, 2, 2, EmiPix.Cream);
                    p.Rect(42, 16, 2, 2, EmiPix.Cream);
                    p.Rect(36, 18, 2, 2, EmiPix.Cream);
                }
            }

            // The week strip: a short bar under the shelves that fills over the loop.
            EmiPix.Bar(p, 6, 60, 60, 3, t / LoopMs, EmiPix.Lav);
        }
    }

    // =====================================================================================
    //  RAIN  -  a dress changes in the wardrobe, Apply fills, the tube follows
    // =====================================================================================

    /// <summary>THE SENTENCE: "pick a look, press Apply, and the tube wears it." A figure on the
    /// left tries two colours, an Apply bar fills, and the figure in the tube on the right changes
    /// only when the bar is full.</summary>
    private sealed class RainDemo : EmiDemoPainter
    {
        public override string Id => "rain";
        public override int LoopMs => 6500;
        public override double StillMs => 5200;

        private static readonly uint Teal = EmiPixelCanvas.Rgb(0x3E, 0xB8, 0xB0);

        private static void Figure(EmiPixelCanvas p, int x, int y, uint dress)
        {
            p.Rect(x + 4, y, 6, 6, EmiPix.Cream);          // head
            p.Rect(x + 3, y - 1, 8, 3, EmiPix.Gold);       // hair
            p.Rect(x + 3, y + 7, 8, 8, dress);             // bodice
            p.Rect(x + 1, y + 15, 12, 7, dress);           // skirt
            p.Rect(x + 4, y + 22, 2, 6, EmiPix.Cream);     // legs
            p.Rect(x + 8, y + 22, 2, 6, EmiPix.Cream);
        }

        public override void Draw(EmiPixelCanvas p, double t)
        {
            p.Clear(EmiPix.Navy);

            // The wardrobe: a mirror frame with the draft look.
            p.Rect(6, 8, 30, 46, EmiPix.Mid);
            p.Rect(8, 10, 26, 42, EmiPix.Dark);
            uint draft = t < 1400 ? EmiPix.Pink : Teal;
            Figure(p, 14, 18, draft);

            // The Apply button and its bar: fills from 2.0 s to 4.0 s.
            p.Rect(6, 58, 30, 6, EmiPix.Mid);
            double fill = Math.Clamp((t - 2000) / 2000.0, 0, 1);
            EmiPix.Bar(p, 7, 59, 28, 4, fill, EmiPix.Pink);

            // The tube on the right: glass, and the figure it plays.
            p.Rect(60, 4, 26, 62, EmiPix.Mid);
            p.Rect(62, 6, 22, 58, EmiPixelCanvas.Rgb(0x22, 0x22, 0x3C));
            uint worn = fill >= 1.0 ? Teal : EmiPix.Pink;
            Figure(p, 66, 20, worn);
            if (fill >= 1.0 && t < 4600) p.RectA(62, 6, 22, 58, EmiPix.Cream, 0.25 * (1 - (t - 4000) / 600.0));

            // The arrow between them, lit while the bake runs.
            uint arrow = fill > 0 && fill < 1 ? EmiPix.Gold : EmiPix.Mid;
            p.Rect(42, 30, 12, 2, arrow);
            p.Rect(50, 28, 2, 6, arrow);
            p.Rect(52, 29, 2, 4, arrow);
        }
    }
}
