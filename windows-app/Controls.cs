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

    /// <summary>Thanh dieu huong doc ben trai (kieu Phone Link / Windows 11).</summary>
    public class NavRail : Panel
    {
        public event Action<int> ItemClicked;
        public int Selected { get; private set; } = -1;
        readonly System.Collections.Generic.List<RoundButton> items = new System.Collections.Generic.List<RoundButton>();
        readonly FlowLayoutPanel top = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Top, AutoSize = true, Padding = Theme.P(6, 10, 6, 0) };
        readonly FlowLayoutPanel bottom = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Bottom, AutoSize = true, Padding = Theme.P(6, 0, 6, 10) };

        public NavRail()
        {
            Dock = DockStyle.Left; Width = Theme.S(76); BackColor = Theme.Panel;
            top.BackColor = bottom.BackColor = Theme.Panel;
            Controls.Add(top); Controls.Add(bottom);
            Controls.Add(new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Theme.Line });
        }

        public RoundButton Add(string glyph, string label)
        {
            int idx = items.Count;
            var b = Make(glyph, label);
            b.Click += (s, e) => ItemClicked?.Invoke(idx);
            items.Add(b);
            top.Controls.Add(b);
            return b;
        }

        public RoundButton AddBottom(string glyph, string label, Action click)
        {
            var b = Make(glyph, label);
            b.Click += (s, e) => click();
            bottom.Controls.Add(b);
            return b;
        }

        static RoundButton Make(string glyph, string label)
        {
            var b = new RoundButton(glyph, label, BtnStyle.Nav) { Vertical = true, Radius = 10, Margin = Theme.P(0, 0, 0, 4) };
            b.AutoFit();
            return b;
        }

        public void Select(int i)
        {
            Selected = i;
            for (int k = 0; k < items.Count; k++) { items[k].Toggled = k == i; items[k].Invalidate(); }
        }

        public void SetBadge(int i, int n) { items[i].Badge = n; items[i].Invalidate(); }
    }

    /// <summary>Hang "chip" trang thai: ket noi, pin, khung/s.</summary>
    public class Chips : Control
    {
        public readonly System.Collections.Generic.List<(string glyph, string text, Color color)> Items = new System.Collections.Generic.List<(string, string, Color)>();

        public Chips()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(24); Font = Theme.Small;
        }

        public void Set(params (string glyph, string text, Color color)[] items)
        {
            Items.Clear(); Items.AddRange(items);
            int w = 0;
            foreach (var it in Items) w += ChipWidth(it) + Theme.S(6);
            Width = Math.Max(1, w);
            Invalidate();
        }

        int ChipWidth((string glyph, string text, Color color) it) =>
            Theme.S(10) + (it.glyph != "" ? Theme.S(18) : 0) + TextRenderer.MeasureText(it.text, Font).Width + Theme.S(4);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int x = 0;
            foreach (var it in Items)
            {
                int w = ChipWidth(it);
                var r = new Rectangle(x, 0, w, Height - 1);
                using (var p = Theme.Round(r, r.Height / 2)) using (var b = new SolidBrush(Theme.Panel2)) g.FillPath(b, p);
                int tx = x + Theme.S(8);
                if (it.glyph == "●")
                {
                    int d = Theme.S(8);
                    using (var b = new SolidBrush(it.color)) g.FillEllipse(b, tx + Theme.S(3), (Height - d) / 2, d, d);
                    tx += Theme.S(18);
                }
                else if (it.glyph != "")
                {
                    TextRenderer.DrawText(g, it.glyph, Theme.IconsSmall, new Rectangle(tx, 0, Theme.S(18), Height), it.color, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                    tx += Theme.S(18);
                }
                TextRenderer.DrawText(g, it.text, Font, new Rectangle(tx, 0, w, Height), it.glyph == "●" ? Theme.Text : it.color, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                x += w + Theme.S(6);
            }
        }
    }

    /// <summary>Hang tab ngang kieu Phone Link: bieu tuong + chu, gach chan mau nhan.</summary>
    public class TabStrip : Control
    {
        public event Action<int> Changed;
        public int Selected { get; private set; }
        readonly System.Collections.Generic.List<(string glyph, string text)> items = new System.Collections.Generic.List<(string, string)>();
        readonly System.Collections.Generic.List<int> badges = new System.Collections.Generic.List<int>();
        int hover = -1;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(44); Font = Theme.UI; Cursor = Cursors.Hand;
        }

        public void Add(string glyph, string text) { items.Add((glyph, text)); badges.Add(0); Width = TotalWidth(); tip.SetToolTip(this, null); Invalidate(); }
        readonly ToolTip tip = new ToolTip();
        bool compact;
        /// <summary>Thieu cho: bo bieu tuong, chi giu chu (vi tri cac tab khong nhay).</summary>
        public bool Compact { get => compact; set { if (compact != value) { compact = value; Width = TotalWidth(); Invalidate(); } } }
        public int FullWidth { get { bool c = compact; compact = false; int w = TotalWidth(); compact = c; return w; } }
        bool ShowText(int i) => true;
        public void SetBadge(int i, int n) { badges[i] = n; Invalidate(); }
        public void Select(int i) { Selected = i; Invalidate(); Changed?.Invoke(i); }

        int ItemWidth(int i) => compact ? Theme.S(12) + TextRenderer.MeasureText(items[i].text, Theme.UIBold).Width + Theme.S(12) : Theme.S(12) + Theme.S(24) + TextRenderer.MeasureText(items[i].text, Theme.UIBold).Width + Theme.S(12);
        int TotalWidth() { int w = 0; for (int i = 0; i < items.Count; i++) w += ItemWidth(i) + Theme.S(4); return w; }

        int HitTest(int x)
        {
            int cx = 0;
            for (int i = 0; i < items.Count; i++) { int w = ItemWidth(i); if (x >= cx && x < cx + w) return i; cx += w + Theme.S(4); }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.X); if (h != hover) { hover = h; Invalidate(); tip.SetToolTip(this, h >= 0 && !ShowText(h) ? items[h].text : null); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { int h = HitTest(e.X); if (h >= 0) Select(h); base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int x = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int w = ItemWidth(i);
                var r = new Rectangle(x, Theme.S(4), w, Height - Theme.S(10));
                bool sel = i == Selected;
                if (hover == i && !sel) using (var p = Theme.Round(r, Theme.S(8))) using (var b = new SolidBrush(Theme.Hover)) g.FillPath(b, p);
                var fg = sel ? Theme.Text : Theme.Text2;
                var gr = compact ? new Rectangle(x + Theme.S(6), r.Y, 0, r.Height) : new Rectangle(x + Theme.S(12), r.Y, Theme.S(20), r.Height);
                if (!compact) TextRenderer.DrawText(g, items[i].glyph, Theme.IconsSmall, gr, sel ? Theme.AccentText : Theme.Text2, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                if (ShowText(i)) TextRenderer.DrawText(g, items[i].text, sel ? Theme.UIBold : Theme.UI, new Rectangle(gr.Right + Theme.S(6), r.Y, w, r.Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (badges[i] > 0)
                {
                    string bs = badges[i] > 99 ? "99+" : badges[i].ToString();
                    int bw = Math.Max(Theme.S(16), TextRenderer.MeasureText(bs, Theme.Tiny).Width + Theme.S(4)), bh = Theme.S(16);
                    var br = new Rectangle(gr.X + Theme.S(10), r.Y + Theme.S(3), bw, bh);
                    using (var p = Theme.Round(br, bh / 2)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
                    TextRenderer.DrawText(g, bs, Theme.Tiny, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                if (sel)
                {
                    int lw = Theme.S(18), lh = Theme.S(3);
                    var lr = new Rectangle(x + (w - lw) / 2, Height - lh - Theme.S(2), lw, lh);
                    using (var p = Theme.Round(lr, 1)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
                }
                x += w + Theme.S(4);
            }
        }
    }

    /// <summary>Anh nho man hinh dien thoai (goc tren cot trai).</summary>
    public class PhoneThumb : Control
    {
        Image img;
        public Image Image { get => img; set { var old = img; img = value; old?.Dispose(); Invalidate(); } }

        public PhoneThumb()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = Theme.Sz(58, 116);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = Theme.Round(r, Theme.S(8)))
            {
                using (var b = new SolidBrush(Color.FromArgb(30, 32, 38))) g.FillPath(b, p);
                var ir = Rectangle.Inflate(r, -Theme.S(3), -Theme.S(3));
                using (var ip = Theme.Round(ir, Theme.S(6)))
                {
                    g.FillPath(Brushes.Black, ip);
                    if (img != null)
                    {
                        g.SetClip(ip);
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        float s = Math.Max((float)ir.Width / img.Width, (float)ir.Height / img.Height);
                        float sw = ir.Width / s, sh = ir.Height / s;
                        g.DrawImage(img, ir, new RectangleF((img.Width - sw) / 2, (img.Height - sh) / 2, sw, sh), GraphicsUnit.Pixel);
                        g.ResetClip();
                    }
                    else TextRenderer.DrawText(g, "\uE8EA", Theme.Icons, ir, Color.FromArgb(110, 116, 128), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                using (var pen = new Pen(Color.FromArgb(70, 76, 88))) g.DrawPath(pen, p);
            }
        }
    }
}
