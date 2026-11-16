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
        // Mau thay doi theo giao dien sang/toi (goi Apply truoc khi tao cua so).
        public static Color Bg, Panel, Panel2, Hover, Line, Text, Text2, Muted, Accent, AccentHover, AccentSoft, AccentText,
            Red, RedSoft, Green, GreenSoft, Orange, ToastBg;
        public static bool IsDark = true;
        static Theme() { Apply("dark"); }

        /// <summary>mode: "dark", "light" hoac "system" (theo Windows).</summary>
        public static void Apply(string mode)
        {
            IsDark = mode == "light" ? false : mode == "dark" ? true : !WindowsUsesLight();
            if (IsDark)
            {
                Bg = Color.FromArgb(17, 19, 24); Panel = Color.FromArgb(26, 29, 36); Panel2 = Color.FromArgb(36, 40, 49);
                Hover = Color.FromArgb(44, 49, 59); Line = Color.FromArgb(50, 56, 67); Text = Color.FromArgb(236, 238, 241);
                Text2 = Color.FromArgb(200, 205, 214); Muted = Color.FromArgb(146, 153, 165);
                Accent = Color.FromArgb(76, 139, 255); AccentHover = Color.FromArgb(100, 156, 255);
                AccentSoft = Color.FromArgb(32, 51, 86); AccentText = Color.FromArgb(150, 190, 255);
                Red = Color.FromArgb(240, 98, 90); RedSoft = Color.FromArgb(66, 31, 33);
                Green = Color.FromArgb(74, 200, 116); GreenSoft = Color.FromArgb(27, 58, 40); Orange = Color.FromArgb(240, 170, 70);
            }
            else
            {
                Bg = Color.FromArgb(242, 244, 248); Panel = Color.White; Panel2 = Color.FromArgb(235, 238, 243);
                Hover = Color.FromArgb(226, 230, 237); Line = Color.FromArgb(218, 222, 230); Text = Color.FromArgb(22, 25, 31);
                Text2 = Color.FromArgb(55, 61, 72); Muted = Color.FromArgb(98, 106, 119);
                Accent = Color.FromArgb(37, 99, 235); AccentHover = Color.FromArgb(29, 84, 210);
                AccentSoft = Color.FromArgb(222, 233, 255); AccentText = Color.FromArgb(29, 78, 216);
                Red = Color.FromArgb(215, 45, 45); RedSoft = Color.FromArgb(253, 228, 228);
                Green = Color.FromArgb(22, 150, 70); GreenSoft = Color.FromArgb(220, 245, 228); Orange = Color.FromArgb(200, 110, 0);
            }
            ToastBg = Color.FromArgb(38, 42, 52);
        }

        static bool WindowsUsesLight()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
            }
            catch { return false; }
        }

        // ---- cua so: thanh tieu de cung mau, thanh cuon toi
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(System.IntPtr h, int attr, ref int val, int size);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(System.IntPtr h, string app, string id);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135")]
        static extern int SetPreferredAppMode(int mode);

        static int Ref(Color c) => c.R | (c.G << 8) | (c.B << 16);

        /// <summary>Goi 1 lan khi khoi dong: cho phep thanh cuon/menu toi.</summary>
        public static void InitProcess()
        {
            try { if (System.Environment.OSVersion.Version.Build >= 18362) SetPreferredAppMode(IsDark ? 2 : 3); } catch { }
        }

        /// <summary>Thanh tieu de toi/sang, Windows 11: cung mau voi dau trang.</summary>
        public static void StyleWindow(Form f, Color? caption = null)
        {
            f.HandleCreated += (s, e) =>
            {
                try
                {
                    int dark = IsDark ? 1 : 0;
                    if (DwmSetWindowAttribute(f.Handle, 20, ref dark, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref dark, 4);
                    int cap = Ref(caption ?? Panel), txt = Ref(Text), border = Ref(Line);
                    DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);
                    DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);
                    DwmSetWindowAttribute(f.Handle, 34, ref border, 4);
                }
                catch { }
            };
        }

        /// <summary>Thanh cuon theo giao dien (toi tren Windows 10 1809+).</summary>
        public static T Scroll<T>(T c) where T : Control
        {
            c.HandleCreated += (s, e) => { try { SetWindowTheme(c.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { } };
            return c;
        }

        /// <summary>Doi mau khi re chuot: sang len (giao dien toi) hoac toi di (giao dien sang).</summary>
        public static Color Shade(Color c, float amt)
        {
            if (IsDark) return Lighten(c, amt);
            int f(int v) => (int)System.Math.Max(0, v - 255 * amt);
            return Color.FromArgb(c.A, f(c.R), f(c.G), f(c.B));
        }

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
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Line;
            return b;
        }

        public static Label Lbl(string text, Font font = null, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, Font = font ?? UI, ForeColor = color ?? Text, BackColor = Color.Transparent };
        }

        public static TextBox Input(string placeholder = null)
        {
            var t = new TextBox { BackColor = Panel2, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Font = UI };
            Scroll(t);
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
