using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LinkDesk
{
    // Tab "Ung dung": mo app tren dien thoai tu may tinh (giong Phone Link).
    public partial class MainForm
    {
        readonly Panel appsPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
        readonly FlowLayoutPanel appsGrid = Theme.Scroll(new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = Theme.P(20, 4, 12, 16) });
        readonly Label appsInfo = new Label { AutoSize = true, Font = Theme.UI, ForeColor = Theme.Muted, Margin = Theme.P(4, 10, 0, 0) };
        RoundInput appSearch;
        List<(string pkg, string name)> appData = new List<(string, string)>();
        bool appsLoaded;
        int appsGen;

        void BuildApps()
        {
            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(108), BackColor = Theme.Bg };
            head.Controls.Add(new Label { Text = "Ứng dụng", Font = new Font("Segoe UI Semibold", 14f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(Theme.S(20), Theme.S(16)), BackColor = Theme.Bg });
            head.Controls.Add(new Label { Text = "Bấm vào một ứng dụng để mở trên điện thoại.", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(Theme.S(21), Theme.S(44)), BackColor = Theme.Bg });
            appSearch = new RoundInput("Tìm ứng dụng…");
            appSearch.Box.TextChanged += (s, e) => RenderApps();
            var reload = new RoundButton("", "", BtnStyle.Ghost);
            new ToolTip().SetToolTip(reload, "Tải lại danh sách");
            reload.Click += (s, e) => LoadApps();
            head.Resize += (s, e) =>
            {
                reload.Location = new Point(head.Width - reload.Width - Theme.S(16), Theme.S(64));
                appSearch.SetBounds(Theme.S(20), Theme.S(64), Math.Min(Theme.S(420), reload.Left - Theme.S(30)), appSearch.Height);
            };
            head.Controls.Add(appSearch); head.Controls.Add(reload);
            appsGrid.Controls.Add(appsInfo);
            appsPage.Controls.Add(appsGrid);
            appsPage.Controls.Add(head);
        }

        void OnAppsTab() { if (!appsLoaded) LoadApps(); appSearch.Box.Focus(); }

        async void LoadApps()
        {
            var c = client;
            if (c == null) return;
            int gen = ++appsGen;
            appsInfo.Text = "Đang tải…"; appsInfo.Visible = true;
            try
            {
                var arr = J.ParseArray(System.Text.Encoding.UTF8.GetString(await c.GetBytes("/apps")));
                if (gen != appsGen || client != c) return;
                appData = arr.OfType<Dictionary<string, object>>().Select(d => (d.S("pkg"), d.S("name"))).ToList();
                appsLoaded = true;
                RenderApps();
            }
            catch (Exception ex)
            {
                if (gen == appsGen) appsInfo.Text = "Không tải được danh sách ứng dụng: " + ex.Message + "\nCập nhật app LinkDesk trên điện thoại lên bản mới nhất.";
            }
        }

        void RenderApps()
        {
            string q = (appSearch?.Box.Text ?? "").Trim();
            var list = appData.Where(a => q == "" || Fold(a.name).Contains(Fold(q))).ToList();
            appsGrid.SuspendLayout();
            foreach (var x in appsGrid.Controls.OfType<AppTile>().ToList()) { appsGrid.Controls.Remove(x); x.Dispose(); }
            foreach (var a in list)
            {
                var t = new AppTile(a.pkg, a.name);
                if (appIcons.TryGetValue(a.pkg, out var img)) t.Icon = img; else LoadIcon(a.pkg);
                string pkg = a.pkg;
                t.Click += (s, e) => { client?.Send("t", "launch", "pkg", pkg); ShowPage(0); };
                appsGrid.Controls.Add(t);
            }
            appsInfo.Visible = list.Count == 0;
            if (list.Count == 0) appsInfo.Text = appData.Count == 0 ? "Chưa có ứng dụng nào." : "Không tìm thấy ứng dụng phù hợp.";
            appsGrid.ResumeLayout();
        }

        /// <summary>Bo dau tieng Viet de tim "zalo" ra "Zalo", "tin nhan" ra "Tin nhắn".</summary>
        static string Fold(string s)
        {
            var n = (s ?? "").ToLowerInvariant().Replace('đ', 'd').Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(n.Length);
            foreach (var ch in n) if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return sb.ToString();
        }

        void OnAppIcon(string pkg, Image img)
        {
            foreach (var t in appsGrid.Controls.OfType<AppTile>()) if (t.Pkg == pkg) { t.Icon = img; t.Invalidate(); }
        }
    }

    /// <summary>O ung dung: bieu tuong + ten, sang len khi re chuot.</summary>
    public class AppTile : Control
    {
        public readonly string Pkg;
        public new Image Icon;
        bool hover;

        public AppTile(string pkg, string name)
        {
            Pkg = pkg; Text = name;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = Theme.Sz(104, 104); Margin = Theme.P(2, 2, 2, 2); Cursor = Cursors.Hand; BackColor = Theme.Bg;
            new ToolTip().SetToolTip(this, name);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (hover)
                using (var p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(10)))
                using (var b = new SolidBrush(Theme.Hover)) g.FillPath(b, p);
            int s = Theme.S(48);
            var r = new Rectangle((Width - s) / 2, Theme.S(12), s, s);
            if (Icon != null) g.DrawImage(Icon, r);
            else
                using (var p = Theme.Round(r, Theme.S(12)))
                using (var b = new SolidBrush(Theme.Panel2)) g.FillPath(b, p);
            var tr = new Rectangle(Theme.S(4), r.Bottom + Theme.S(6), Width - Theme.S(8), Height - r.Bottom - Theme.S(8));
            TextRenderer.DrawText(g, Text, Theme.Small, tr, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
}
