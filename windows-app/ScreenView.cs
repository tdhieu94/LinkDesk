using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LinkDesk
{
    /// <summary>O hien thi man hinh dien thoai, nhan chuot va ban phim.</summary>
    public class ScreenView : Control
    {
        Bitmap frame;
        int fw, fh;
        public string Placeholder = "Đang kết nối…";
        public Action<object> SendCmd;   // gui lenh (Dictionary) toi dien thoai
        string banner;
        public Action BannerClick;       // bam vao khi dang hien thong bao (vd. bat man hinh)

        /// <summary>Dong thong bao phu len hinh (null = an).</summary>
        public string Banner
        {
            get => banner;
            set { if (banner != value) { banner = value; Invalidate(); } }
        }

        public ScreenView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Muted;
            Cursor = Cursors.Hand;
            TabStop = true;
            textTimer.Interval = 40;
            textTimer.Tick += (s, e) => FlushText();
            wheelTimer.Interval = 90;
            wheelTimer.Tick += (s, e) => FlushWheel();
        }

        public bool HasFrame => frame != null;

        public void SetFrame(Bitmap b, int w, int h)
        {
            var old = frame;
            frame = b; fw = w; fh = h;
            old?.Dispose();
            Invalidate();
        }

        public void ClearFrame(string placeholder)
        {
            Placeholder = placeholder;
            var old = frame; frame = null; old?.Dispose();
            Invalidate();
        }

        /// <summary>Anh man hinh hien tai (da cat vien), de luu.</summary>
        public Bitmap Snapshot()
        {
            if (frame == null) return null;
            var b = new Bitmap(fw, fh);
            using (var g = Graphics.FromImage(b)) g.DrawImage(frame, new Rectangle(0, 0, fw, fh), new Rectangle(0, 0, fw, fh), GraphicsUnit.Pixel);
            return b;
        }

        Rectangle ImageRect()
        {
            int w = fw > 0 ? fw : 9, h = fh > 0 ? fh : 19;
            int pad = 12;
            float s = Math.Min((Width - 2f * pad) / w, (Height - 2f * pad) / h);
            if (s <= 0) s = 0.01f;
            int dw = (int)(w * s), dh = (int)(h * s);
            return new Rectangle((Width - dw) / 2, (Height - dh) / 2, dw, dh);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var r = ImageRect();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), 12))
            using (var pen = new Pen(Focused ? Theme.Accent : Theme.Line, Focused ? 2 : 1))
            {
                g.FillPath(Brushes.Black, path);
                if (frame != null)
                {
                    g.SetClip(path);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(frame, r, new Rectangle(0, 0, fw, fh), GraphicsUnit.Pixel);
                    g.ResetClip();
                }
                g.DrawPath(pen, path);
            }
            if (banner != null && frame != null)
            {
                using (var b = new SolidBrush(Color.FromArgb(200, 10, 12, 15)))
                using (var path = Theme.Round(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), 12))
                    g.FillPath(b, path);
            }
            if (banner != null)
            {
                TextRenderer.DrawText(g, banner, Theme.UI, new Rectangle(r.X + 20, r.Y, r.Width - 40, r.Height), Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            if (frame == null)
            {
                TextRenderer.DrawText(g, Placeholder, Font, new Rectangle(r.X + 16, r.Y, r.Width - 32, r.Height), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ------------------------------------------------------------------ chuot
        double[] Norm(Point p)
        {
            var r = ImageRect();
            double x = (p.X - r.X) / (double)Math.Max(1, r.Width), y = (p.Y - r.Y) / (double)Math.Max(1, r.Height);
            return new[] { Math.Max(0, Math.Min(1, x)), Math.Max(0, Math.Min(1, y)) };
        }

        bool InImage(Point p) => ImageRect().Contains(p);

        class Down { public DateTime Start; public List<double[]> Pts = new List<double[]>(); public DateTime MovedAt; public bool Moved; }
        Down down;

        static Dictionary<string, object> Cmd(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        void Send(params object[] kv) => SendCmd?.Invoke(Cmd(kv));

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (banner != null && e.Button == MouseButtons.Left) { BannerClick?.Invoke(); return; }
            if (frame == null) return;
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.XButton1) { Send("t", "key", "k", "back"); return; }
            if (e.Button == MouseButtons.Middle) { Send("t", "key", "k", "home"); return; }
            if (e.Button != MouseButtons.Left || !InImage(e.Location)) return;
            down = new Down { Start = DateTime.UtcNow };
            down.Pts.Add(Norm(e.Location));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (down == null) return;
            var p = Norm(e.Location);
            var first = down.Pts[0];
            var last = down.Pts[down.Pts.Count - 1];
            var r = ImageRect();
            double dx = (p[0] - first[0]) * r.Width, dy = (p[1] - first[1]) * r.Height;
            if (!down.Moved && Math.Sqrt(dx * dx + dy * dy) > 8) { down.Moved = true; down.MovedAt = DateTime.UtcNow; }
            double lx = (p[0] - last[0]) * r.Width, ly = (p[1] - last[1]) * r.Height;
            if (Math.Sqrt(lx * lx + ly * ly) > 4) down.Pts.Add(p);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (down == null || e.Button != MouseButtons.Left) return;
            var d = down; down = null;
            var now = DateTime.UtcNow;
            d.Pts.Add(Norm(e.Location));
            double total = (now - d.Start).TotalMilliseconds;
            if (!d.Moved)
            {
                var f = d.Pts[0];
                Send("t", "tap", "x", f[0], "y", f[1], "dur", total < 350 ? 40 : (long)Math.Min(total, 3000));
                return;
            }
            var pts = Simplify(d.Pts, 40);
            double hold = (d.MovedAt - d.Start).TotalMilliseconds;
            if (hold > 450)
                Send("t", "drag", "pts", pts, "hold", (long)hold, "dur", (long)Math.Max(80, (now - d.MovedAt).TotalMilliseconds));
            else
                Send("t", "swipe", "pts", pts, "dur", (long)Math.Max(60, Math.Min(total, 3000)));
        }

        static List<double[]> Simplify(List<double[]> pts, int max)
        {
            var o = new List<double[]>();
            int step = Math.Max(1, (int)Math.Ceiling(pts.Count / (double)max));
            for (int i = 0; i < pts.Count; i += step) o.Add(new[] { Math.Round(pts[i][0], 4), Math.Round(pts[i][1], 4) });
            var l = pts[pts.Count - 1];
            o.Add(new[] { Math.Round(l[0], 4), Math.Round(l[1], 4) });
            return o;
        }

        readonly Timer wheelTimer = new Timer();
        int wheelAcc;
        double[] wheelPos;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (frame == null || !InImage(e.Location)) return;
            wheelAcc += -e.Delta; // Delta > 0: lan len
            wheelPos = Norm(e.Location);
            if (!wheelTimer.Enabled) wheelTimer.Start();
        }

        void FlushWheel()
        {
            wheelTimer.Stop();
            double amount = Math.Max(-0.6, Math.Min(0.6, wheelAcc / 120.0 * 0.15));
            wheelAcc = 0;
            if (wheelPos != null && Math.Abs(amount) > 0.001) Send("t", "scroll", "x", wheelPos[0], "y", wheelPos[1], "dy", amount);
        }

        // ------------------------------------------------------------------ ban phim
        readonly Timer textTimer = new Timer();
        string pendingText = "";

        void FlushText()
        {
            textTimer.Stop();
            if (pendingText.Length > 0) { Send("t", "text", "s", pendingText); pendingText = ""; }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Tab: case Keys.Enter: case Keys.Escape:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        // Nhan moi ky tu (khong de form xu ly phim tat kieu &chu)
        protected override bool IsInputChar(char charCode) => true;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            string edit = null;
            if (e.Control)
            {
                switch (e.KeyCode)
                {
                    case Keys.V:
                        FlushText();
                        try { if (Clipboard.ContainsText()) Send("t", "text", "s", Clipboard.GetText()); } catch { }
                        e.SuppressKeyPress = true; return;
                    case Keys.C: edit = "copy"; break;
                    case Keys.X: edit = "cut"; break;
                    case Keys.A: edit = "selectall"; break;
                }
            }
            else
            {
                switch (e.KeyCode)
                {
                    case Keys.Escape: FlushText(); Send("t", "key", "k", "back"); e.SuppressKeyPress = true; return;
                    case Keys.Back: edit = "backspace"; break;
                    case Keys.Delete: edit = "delete"; break;
                    case Keys.Enter: edit = "enter"; break;
                    case Keys.Left: edit = "left"; break;
                    case Keys.Right: edit = "right"; break;
                    case Keys.Home: edit = "home"; break;
                    case Keys.End: edit = "end"; break;
                }
            }
            if (edit != null)
            {
                FlushText();
                Send("t", "edit", "k", edit);
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar))
            {
                pendingText += e.KeyChar;
                textTimer.Stop(); textTimer.Start();
                e.Handled = true;
            }
            base.OnKeyPress(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { textTimer.Dispose(); wheelTimer.Dispose(); frame?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
