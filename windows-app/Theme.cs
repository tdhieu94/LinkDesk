using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LinkDesk
{
    public static class Theme
    {
        /// <summary>He so DPI (1.0 = 100%).</summary>
        public static readonly float Scale = GetScale();
        static float GetScale()
        {
            try { using (var g = Graphics.FromHwnd(System.IntPtr.Zero)) return g.DpiX / 96f; } catch { return 1f; }
        }
        public static int S(int px) => (int)System.Math.Round(px * Scale);
        public static Size Sz(int w, int h) => new Size(S(w), S(h));
        public static Padding P(int l, int t, int r, int b) => new Padding(S(l), S(t), S(r), S(b));
        public static readonly Color Bg = Color.FromArgb(15, 17, 21);
        public static readonly Color Panel = Color.FromArgb(24, 27, 33);
        public static readonly Color Panel2 = Color.FromArgb(34, 38, 46);
        public static readonly Color Hover = Color.FromArgb(40, 45, 54);
        public static readonly Color Line = Color.FromArgb(48, 54, 64);
        public static readonly Color Text = Color.FromArgb(236, 238, 241);
        public static readonly Color Muted = Color.FromArgb(150, 157, 168);
        public static readonly Color Accent = Color.FromArgb(79, 140, 255);
        public static readonly Color AccentSoft = Color.FromArgb(31, 50, 84);
        public static readonly Color AccentText = Color.FromArgb(150, 190, 255);
        public static readonly Color Red = Color.FromArgb(240, 98, 90);
        public static readonly Color RedSoft = Color.FromArgb(64, 30, 32);
        public static readonly Color Green = Color.FromArgb(70, 196, 110);
        public static readonly Color Orange = Color.FromArgb(240, 170, 70);

        public static Color Lighten(Color c, float amt)
        {
            int f(int v) => (int)System.Math.Min(255, v + 255 * amt);
            return Color.FromArgb(c.A, f(c.R), f(c.G), f(c.B));
        }

        public static readonly Font UI = new Font("Segoe UI", 9.75f);
        public static readonly Font UIBold = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 12f);
        public static readonly Font Tiny = new Font("Segoe UI Semibold", 7.5f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 15f);
        static readonly string IconFamily = PickIconFont();
        public static readonly Font Icons = new Font(IconFamily, 12.5f);
        public static readonly Font IconsSmall = new Font(IconFamily, 10.5f);
        static string PickIconFont()
        {
            try
            {
                foreach (var f in FontFamily.Families)
                    if (f.Name == "Segoe Fluent Icons") return f.Name;
            }
            catch { }
            return "Segoe MDL2 Assets";
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Button Btn(string text, bool primary = false, bool danger = false)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = primary ? Accent : Panel2, ForeColor = danger ? Red : (primary ? Color.White : Text),
                Font = UI, Padding = P(8, 3, 8, 3), Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = danger ? Red : (primary ? Accent : Line);
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(96, 158, 250) : Line;
            return b;
        }

        public static Label Lbl(string text, Font font = null, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, Font = font ?? UI, ForeColor = color ?? Text, BackColor = Color.Transparent };
        }

        public static TextBox Input(string placeholder = null)
        {
            var t = new TextBox { BackColor = Panel2, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Font = UI };
            if (placeholder != null) Cue(t, placeholder);
            return t;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern System.IntPtr SendMessage(System.IntPtr h, int msg, System.IntPtr w, string l);

        public static void Cue(TextBox t, string text)
        {
            t.HandleCreated += (s, e) => { try { SendMessage(t.Handle, 0x1501, (System.IntPtr)1, text); } catch { } };
        }
    }
}
