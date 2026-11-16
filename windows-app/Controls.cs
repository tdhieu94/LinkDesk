using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LinkDesk
{
    public enum BtnStyle { Ghost, Soft, Primary, Danger, Nav }

    /// <summary>Nut bo goc, ve tay: bieu tuong (font icon) + chu.</summary>
    public class RoundButton : Control
    {
        public string Glyph = "";
        public BtnStyle Style = BtnStyle.Soft;
        public int Radius = 10;
        public bool Toggled;
        public bool Vertical;          // bieu tuong tren, chu duoi (thanh dieu huong)
        public int Badge;              // so tren goc (vd. so thong bao)
        bool hover, press;

        public RoundButton(string glyph, string text, BtnStyle style = BtnStyle.Soft)
        {
            Glyph = glyph ?? ""; Text = text ?? ""; Style = style;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            Font = Theme.UI;
            Cursor = Cursors.Hand;
            Margin = Theme.P(3, 3, 3, 3);
            AutoFit();
        }

        public void AutoFit(int minW = 0)
        {
            int h = Theme.S(Vertical ? 58 : 36);
            int w;
            if (Vertical) w = Theme.S(64);
            else
            {
                w = Theme.S(14);
                if (Glyph != "") w += Theme.S(20);
                if (Text != "") w += TextRenderer.MeasureText(Text, Font).Width + (Glyph != "" ? Theme.S(6) : 0);
                w += Theme.S(14) - (Text == "" ? Theme.S(8) : 0);
                if (Text == "") w = h;
            }
            Size = new Size(Math.Max(w, minW), h);
        }

        public void SetText(string t) { Text = t; AutoFit(); Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; press = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { press = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { press = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg, fg;
            switch (Style)
            {
                case BtnStyle.Primary: bg = Theme.Accent; fg = Color.White; break;
                case BtnStyle.Danger: bg = Theme.RedSoft; fg = Theme.Red; break;
                case BtnStyle.Soft: bg = Theme.Panel2; fg = Theme.Text; break;
                default: bg = Color.Transparent; fg = Theme.Text; break;
            }
            if (Toggled) { bg = Theme.AccentSoft; fg = Theme.AccentText; }
            if (Style == BtnStyle.Nav && !Toggled) fg = Theme.Muted;
            if (hover && Enabled) bg = bg.A == 0 ? Theme.Hover : Theme.Lighten(bg, press ? 0.02f : 0.08f);
            if (!Enabled) fg = Theme.Line;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bg.A > 0)
                using (var p = Theme.Round(r, Theme.S(Radius)))
                using (var b = new SolidBrush(bg)) g.FillPath(b, p);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (Vertical)
            {
                var gr = new Rectangle(0, Theme.S(8), Width, Theme.S(24));
                TextRenderer.DrawText(g, Glyph, Theme.Icons, gr, fg, flags | TextFormatFlags.HorizontalCenter);
                var tr = new Rectangle(0, Theme.S(33), Width, Theme.S(18));
                TextRenderer.DrawText(g, Text, Theme.Small, tr, fg, flags | TextFormatFlags.HorizontalCenter);
            }
            else if (Text == "")
            {
                TextRenderer.DrawText(g, Glyph, Theme.Icons, r, fg, flags | TextFormatFlags.HorizontalCenter);
            }
            else
            {
                int x = Theme.S(14);
                if (Glyph != "")
                {
                    TextRenderer.DrawText(g, Glyph, Theme.IconsSmall, new Rectangle(x, 0, Theme.S(20), Height), fg, flags | TextFormatFlags.HorizontalCenter);
                    x += Theme.S(26);
                }
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - Theme.S(8), Height), fg, flags);
            }
            if (Badge > 0)
            {
                string s = Badge > 99 ? "99+" : Badge.ToString();
                var sz = TextRenderer.MeasureText(s, Theme.Tiny);
                int bw = Math.Max(Theme.S(18), sz.Width + Theme.S(6)), bh = Theme.S(18);
                var br = new Rectangle(Width - bw - Theme.S(6), Theme.S(4), bw, bh);
                using (var p = Theme.Round(br, bh / 2)) using (var b = new SolidBrush(Theme.Red)) g.FillPath(b, p);
                TextRenderer.DrawText(g, s, Theme.Tiny, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>Khung bo goc (FlowLayout doc) dung lam the.</summary>
    public class Card : FlowLayoutPanel
    {
        public Color Fill = Theme.Panel;
        public int Radius = 14;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Theme.P(16, 14, 16, 16);
            Margin = Theme.P(0, 0, 0, 12);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(Radius)))
            using (var b = new SolidBrush(Fill)) g.FillPath(b, p);
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            e.Control.BackColor = Fill;
            if (e.Control is RoundButton) e.Control.BackColor = Color.Transparent;
        }
    }

    /// <summary>Thanh chuyen tab dang "segmented".</summary>
    public class Segmented : FlowLayoutPanel
    {
        public event Action<int> Changed;
        public int Selected { get; private set; }
        readonly System.Collections.Generic.List<RoundButton> items = new System.Collections.Generic.List<RoundButton>();

        public Segmented(params string[] labels)
        {
            WrapContents = false;
            AutoSize = true;
            Padding = Theme.P(3, 3, 3, 3);
            BackColor = Theme.Panel2;
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = new RoundButton("", labels[i], BtnStyle.Ghost) { Radius = 8, Margin = new Padding(0) };
                b.Click += (s, e) => Select(idx);
                items.Add(b);
                Controls.Add(b);
            }
            Select(0);
        }

        public void SetLabel(int i, string text) { items[i].SetText(text); }

        public new void Select(int i)
        {
            Selected = i;
            for (int k = 0; k < items.Count; k++) { items[k].Style = k == i ? BtnStyle.Soft : BtnStyle.Ghost; items[k].Toggled = false; items[k].Invalidate(); }
            for (int k = 0; k < items.Count; k++) items[k].BackColor = Color.Transparent;
            Changed?.Invoke(i);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(10)))
            using (var b = new SolidBrush(Theme.Bg)) e.Graphics.FillPath(b, p);
        }
    }
}
