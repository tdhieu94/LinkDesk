using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LinkDesk
{
    public enum BtnStyle { Ghost, Soft, Primary, Danger, Nav, Raised }

    /// <summary>Nut bo goc, ve tay: bieu tuong (font icon) + chu.</summary>
    public class RoundButton : Control
    {
        public string Glyph = "";
        public BtnStyle Style = BtnStyle.Soft;
        public int Radius = 10;
        public bool Toggled;
        public bool Vertical;          // bieu tuong tren, chu duoi (thanh dieu huong)
        public int Badge;              // so tren goc (vd. so thong bao)
        public bool Compact;           // le hai ben hep (dung trong Segmented)
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
                int pad = Theme.S(Compact ? 9 : 14);
                w = pad;
                if (Glyph != "") w += Theme.S(20);
                if (Text != "") w += TextRenderer.MeasureText(Text, Font).Width + (Glyph != "" ? Theme.S(6) : 0);
                w += pad - (Text == "" ? Theme.S(8) : 0);
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
                case BtnStyle.Raised: bg = Theme.IsDark ? Theme.Hover : Theme.Panel; fg = Theme.Text; break;
                default: bg = Color.Transparent; fg = Theme.Text; break;
            }
            if (Toggled) { bg = Theme.AccentSoft; fg = Theme.AccentText; }
            if (Style == BtnStyle.Nav && !Toggled) fg = Theme.Muted;
            if (hover && Enabled && Style != BtnStyle.Raised) bg = bg.A == 0 ? Theme.Hover : Theme.Shade(bg, press ? 0.02f : 0.07f);
            if (!Enabled) fg = Theme.Line;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bg.A > 0)
                using (var p = Theme.Round(r, Theme.S(Radius)))
                using (var b = new SolidBrush(bg))
                {
                    if (Style == BtnStyle.Raised && !Theme.IsDark)
                        using (var sp = Theme.Round(new Rectangle(0, 1, Width - 1, Height - 1), Theme.S(Radius)))
                        using (var sb = new SolidBrush(Color.FromArgb(28, 0, 0, 0))) g.FillPath(sb, sp);
                    g.FillPath(b, p);
                }

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
                if (Glyph == "") TextRenderer.DrawText(g, Text, Font, r, fg, flags | TextFormatFlags.HorizontalCenter);
                else TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - Theme.S(8), Height), fg, flags);
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
                var b = new RoundButton("", labels[i], BtnStyle.Ghost) { Radius = 8, Margin = new Padding(0), Compact = true };
                b.AutoFit(Theme.S(44));
                b.Click += (s, e) => Select(idx);
                items.Add(b);
                Controls.Add(b);
            }
            Select(0);
        }

        public void SetLabel(int i, string text) { items[i].SetText(text); items[i].AutoFit(Theme.S(44)); }

        public new void Select(int i)
        {
            Selected = i;
            for (int k = 0; k < items.Count; k++) { items[k].Style = k == i ? BtnStyle.Raised : BtnStyle.Ghost; items[k].Toggled = false; items[k].Invalidate(); }
            for (int k = 0; k < items.Count; k++) items[k].BackColor = Color.Transparent;
            Changed?.Invoke(i);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(10)))
            using (var b = new SolidBrush(Theme.Panel2)) e.Graphics.FillPath(b, p);
        }
    }

    /// <summary>Cong tac bat/tat kieu Windows 11, kem chu ben trai.</summary>
    public class Toggle : Control
    {
        bool on, hover;
        public event EventHandler CheckedChanged;
        public bool Checked { get => on; set { if (on != value) { on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } } }
        public int TextWidth = Theme.S(400);

        public Toggle(string text, bool value)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Text = text; on = value; Font = Theme.UI; Cursor = Cursors.Hand;
            Margin = Theme.P(0, 4, 0, 4);
            Fit();
        }

        void Fit()
        {
            var sz = TextRenderer.MeasureText(Text, Font, new Size(TextWidth, 0), TextFormatFlags.WordBreak);
            Size = new Size(TextWidth + Theme.S(60), Math.Max(Theme.S(34), sz.Height + Theme.S(12)));
        }

        public void SetWidth(int w) { TextWidth = w - Theme.S(60); Fit(); Invalidate(); }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, TextWidth, Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int w = Theme.S(40), h = Theme.S(20);
            var r = new Rectangle(Width - w - 2, (Height - h) / 2, w, h);
            using (var p = Theme.Round(r, h / 2))
            {
                if (on) using (var b = new SolidBrush(hover ? Theme.AccentHover : Theme.Accent)) g.FillPath(b, p);
                else using (var pen = new Pen(hover ? Theme.Text : Theme.Muted, Math.Max(1, Theme.S(1)))) g.DrawPath(pen, p);
            }
            int d = on ? Theme.S(12) : Theme.S(10);
            int cx = on ? r.Right - Theme.S(10) : r.X + Theme.S(10);
            using (var b = new SolidBrush(on ? Color.White : (hover ? Theme.Text : Theme.Muted)))
                g.FillEllipse(b, cx - d / 2, r.Y + (h - d) / 2, d, d);
        }
    }

    /// <summary>Thanh truot don gian, ve tay.</summary>
    public class Slider : Control
    {
        public int Minimum = 0, Maximum = 100, Step = 1;
        int val;
        bool drag;
        public event EventHandler ValueChanged, Committed;
        public int Value { get => val; set { int v = Math.Max(Minimum, Math.Min(Maximum, value)); if (v != val) { val = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } } }

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = Theme.Sz(220, 28); Cursor = Cursors.Hand;
        }

        int Pad => Theme.S(10);
        void SetFromX(int x)
        {
            float f = (x - Pad) / (float)Math.Max(1, Width - 2 * Pad);
            int v = Minimum + (int)Math.Round(Math.Max(0, Math.Min(1, f)) * (Maximum - Minimum) / Step) * Step;
            Value = v;
        }
        protected override void OnMouseDown(MouseEventArgs e) { drag = true; Focus(); SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (drag) { drag = false; Committed?.Invoke(this, EventArgs.Empty); } base.OnMouseUp(e); }
        protected override bool IsInputKey(Keys k) => k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) { Value -= Step; Committed?.Invoke(this, EventArgs.Empty); }
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) { Value += Step; Committed?.Invoke(this, EventArgs.Empty); }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y = Height / 2, th = Theme.S(4);
            float f = (val - Minimum) / (float)Math.Max(1, Maximum - Minimum);
            int x = Pad + (int)(f * (Width - 2 * Pad));
            using (var p = Theme.Round(new Rectangle(Pad, y - th / 2, Width - 2 * Pad, th), th / 2)) using (var b = new SolidBrush(Theme.Line)) g.FillPath(b, p);
            if (x > Pad) using (var p = Theme.Round(new Rectangle(Pad, y - th / 2, x - Pad, th), th / 2)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
            int d = Theme.S(18);
            using (var b = new SolidBrush(Theme.IsDark ? Theme.Hover : Color.White)) g.FillEllipse(b, x - d / 2, y - d / 2, d, d);
            using (var pen = new Pen(Theme.Line)) g.DrawEllipse(pen, x - d / 2, y - d / 2, d, d);
            int d2 = Theme.S(drag || Focused ? 10 : 8);
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, x - d2 / 2, y - d2 / 2, d2, d2);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>O nhap chu bo goc, vien sang khi dang go.</summary>
    public class RoundInput : Panel
    {
        public readonly TextBox Box = new TextBox { BorderStyle = BorderStyle.None };
        bool focus;

        public RoundInput(string placeholder = null, bool multiline = false, Font font = null)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Box.Font = font ?? Theme.UI;
            Box.BackColor = Theme.Panel2; Box.ForeColor = Theme.Text;
            Box.Multiline = multiline;
            if (multiline) { Box.ScrollBars = ScrollBars.Vertical; Theme.Scroll(Box); }
            if (placeholder != null && !multiline) Theme.Cue(Box, placeholder);
            Box.GotFocus += (s, e) => { focus = true; Invalidate(); };
            Box.LostFocus += (s, e) => { focus = false; Invalidate(); };
            Controls.Add(Box);
            Width = Theme.S(320);
            Height = multiline ? Theme.S(96) : Box.PreferredHeight + Theme.S(16);
            Cursor = Cursors.IBeam;
            Click += (s, e) => Box.Focus();
        }

        public override string Text { get => Box.Text; set => Box.Text = value; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int px = Theme.S(11);
            if (Box.Multiline) Box.Bounds = new Rectangle(px, Theme.S(8), Width - 2 * px + Theme.S(4), Height - Theme.S(16));
            else Box.Bounds = new Rectangle(px, (Height - Box.Height) / 2, Width - 2 * px, Box.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = Theme.Round(r, Theme.S(8)))
            {
                using (var b = new SolidBrush(Theme.Panel2)) g.FillPath(b, p);
                using (var pen = new Pen(focus ? Theme.Accent : Theme.Line, focus ? Math.Max(1, Theme.S(1)) + 0.5f : 1f)) g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>Thong bao nho dang vien thuoc (toast).</summary>
    public class Pill : Label
    {
        public Pill()
        {
            AutoSize = true; Font = Theme.UI; ForeColor = Color.White; BackColor = Theme.ToastBg;
            Padding = Theme.P(16, 9, 16, 9);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using (var p = Theme.Round(new Rectangle(0, 0, Width, Height), Math.Min(Height / 2, Theme.S(18)))) Region = new Region(p);
        }
    }
}
