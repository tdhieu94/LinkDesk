using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkDesk
{
    /// <summary>Hop thoai ket noi: tu tim dien thoai trong WiFi, nhap PIN.</summary>
    public class ConnectForm : Form
    {
        const int W = 420;
        readonly FlowLayoutPanel list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Theme.P(0, 6, 0, 0) };
        readonly Label empty = Theme.Lbl("", Theme.UI, Theme.Muted);
        readonly RoundInput addr = new RoundInput("Ví dụ 192.168.1.5") { Width = Theme.S(W) };
        readonly RoundInput pin = new RoundInput("6 số", false, new Font("Segoe UI Semibold", 15f)) { Width = Theme.S(180) };
        readonly Label status = Theme.Lbl("", Theme.Small, Theme.Muted);
        readonly Label err = Theme.Lbl("", Theme.UI, Theme.Red);
        readonly RoundButton scanBtn = new RoundButton("", "", BtnStyle.Ghost);
        readonly RoundButton okBtn = new RoundButton("", "Kết nối", BtnStyle.Primary);
        readonly Config cfg;
        public PhoneEntry Result;
        string foundId = "", foundName = "";
        readonly List<PhoneRow> rows = new List<PhoneRow>();

        public ConnectForm(Config cfg, string message = null)
        {
            this.cfg = cfg;
            Text = "Kết nối điện thoại – LinkDesk";
            Icon = MainForm.AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Theme.StyleWindow(this, Theme.Bg);

            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = Theme.P(28, 22, 28, 22), BackColor = Theme.Bg };
            Controls.Add(f);

            // tieu de
            var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.P(0, 0, 0, 14) };
            var logo = new Label { Text = "", Font = Theme.Icons, ForeColor = Theme.AccentText, BackColor = Theme.AccentSoft, Size = Theme.Sz(44, 44), TextAlign = ContentAlignment.MiddleCenter, Margin = Theme.P(0, 0, 12, 0) };
            using (var lp = new GraphicsPath()) { lp.AddEllipse(0, 0, logo.Width, logo.Height); logo.Region = new Region(lp); }
            var tt = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            tt.Controls.Add(new Label { Text = "Kết nối điện thoại", Font = Theme.Big, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0) });
            tt.Controls.Add(new Label { Text = "Mở app LinkDesk trên điện thoại và bấm \"Bắt đầu\".", Font = Theme.UI, ForeColor = Theme.Muted, AutoSize = true, Margin = Theme.P(1, 2, 0, 0) });
            head.Controls.Add(logo); head.Controls.Add(tt);
            f.Controls.Add(head);

            // danh sach
            var lh = new Panel { Size = Theme.Sz(W, 32), Margin = new Padding(0) };
            var lt = Theme.Lbl("Điện thoại trong cùng WiFi", Theme.UIBold); lt.Location = new Point(0, Theme.S(7));
            status.Location = new Point(lt.PreferredWidth + Theme.S(8), Theme.S(9));
            scanBtn.Location = new Point(Theme.S(W) - scanBtn.Width, 0);
            new ToolTip().SetToolTip(scanBtn, "Tìm lại");
            scanBtn.Click += async (s, e) => await Scan();
            lh.Controls.Add(lt); lh.Controls.Add(status); lh.Controls.Add(scanBtn);
            f.Controls.Add(lh);
            empty.MaximumSize = Theme.Sz(W, 0);
            empty.Margin = Theme.P(0, 6, 0, 0);
            f.Controls.Add(list);
            f.Controls.Add(empty);

            // dia chi + PIN
            var a = Theme.Lbl("Địa chỉ (nếu không tự tìm thấy)", Theme.UIBold); a.Margin = Theme.P(0, 18, 0, 6);
            f.Controls.Add(a);
            f.Controls.Add(addr);
            var pl = Theme.Lbl("Mã PIN đang hiện trong app", Theme.UIBold); pl.Margin = Theme.P(0, 14, 0, 6);
            f.Controls.Add(pl);
            pin.Box.MaxLength = 6;
            pin.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = DoConnect(); } };
            pin.Box.KeyPress += (s, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            f.Controls.Add(pin);
            err.MaximumSize = Theme.Sz(W, 0);
            err.Margin = Theme.P(0, 12, 0, 0);
            err.Text = message ?? "";
            f.Controls.Add(err);
            okBtn.AutoFit(Theme.S(W));
            okBtn.Height = Theme.S(42);
            okBtn.Margin = Theme.P(0, 14, 0, 0);
            okBtn.Click += async (s, e) => await DoConnect();
            f.Controls.Add(okBtn);

            foreach (Control c in f.Controls) if (!(c is RoundButton)) c.BackColor = Theme.Bg;
            lh.BackColor = head.BackColor = tt.BackColor = Theme.Bg;
            foreach (Control c in lh.Controls) if (!(c is RoundButton)) c.BackColor = Theme.Bg;
            foreach (Control c in tt.Controls) c.BackColor = Theme.Bg;

            ClientSize = Theme.Sz(W + 56, 520);

            var last = cfg.Last;
            if (last != null) { addr.Text = last.Port == 8686 ? last.Host : last.Host + ":" + last.Port; pin.Text = last.Pin; foundId = last.Id; foundName = last.Name; }
            Shown += async (s, e) => { if (pin.Text == "") addr.Box.Focus(); await Scan(); };
        }

        void Pick(PhoneRow r)
        {
            foreach (var x in rows) { x.Selected = x == r; x.Invalidate(); }
            var p = r.Phone;
            addr.Text = p.Port == 8686 ? p.Host : p.Host + ":" + p.Port;
            foundId = p.Id; foundName = p.Name;
            var saved = cfg.Phones.FirstOrDefault(x => x.Id == p.Id && p.Id != "");
            if (saved != null && pin.Text == "") pin.Text = saved.Pin;
            pin.Box.Focus(); pin.Box.SelectAll();
        }

        async Task Scan()
        {
            scanBtn.Enabled = false;
            status.Text = "đang tìm…";
            try
            {
                var found = await Discovery.Scan(1500);
                foreach (var r in rows) { list.Controls.Remove(r); r.Dispose(); }
                rows.Clear();
                foreach (var p in found)
                {
                    var r = new PhoneRow(p) { Width = Theme.S(W) };
                    r.Click += (s, e) => Pick(r);
                    r.DoubleClick += async (s, e) => { Pick(r); await DoConnect(); };
                    rows.Add(r);
                    list.Controls.Add(r);
                }
                status.Text = found.Count == 0 ? "" : found.Count + " máy";
                empty.Text = found.Count == 0 ? "Chưa thấy điện thoại nào. Kiểm tra app LinkDesk đã bấm \"Bắt đầu\" và hai máy dùng chung WiFi, hoặc nhập địa chỉ bên dưới." : "";
                empty.Visible = found.Count == 0;
                var pre = rows.FirstOrDefault(x => x.Phone.Id == foundId && foundId != "") ?? (rows.Count == 1 && addr.Text == "" ? rows[0] : null);
                if (pre != null) Pick(pre);
                int extra = Math.Max(0, rows.Count - 1) * Theme.S(64) - (found.Count == 0 ? 0 : Theme.S(20));
                ClientSize = new Size(ClientSize.Width, Theme.S(520) + Math.Min(extra, Theme.S(200)));
            }
            catch { status.Text = "lỗi khi tìm"; }
            scanBtn.Enabled = true;
        }

        async Task DoConnect()
        {
            string a = addr.Text.Trim().Replace("http://", "").Replace("https://", "").TrimEnd('/');
            if (a == "") { err.Text = "Hãy chọn điện thoại trong danh sách hoặc nhập địa chỉ."; return; }
            int port = 8686;
            int c = a.LastIndexOf(':');
            if (c > 0 && int.TryParse(a.Substring(c + 1), out int pp)) { port = pp; a = a.Substring(0, c); }
            string p = pin.Text.Trim();
            if (p.Length != 6) { err.Text = "Mã PIN gồm 6 số."; pin.Box.Focus(); return; }
            okBtn.Enabled = false; okBtn.SetText("Đang kết nối…"); okBtn.AutoFit(Theme.S(W)); okBtn.Height = Theme.S(42);
            err.Text = "";
            var (st, msg, secure, sha) = await PhoneClient.CheckSecure(a, port, p, "");
            okBtn.Enabled = true; okBtn.SetText("Kết nối"); okBtn.AutoFit(Theme.S(W)); okBtn.Height = Theme.S(42);
            if (st != ConnState.Connected) { err.Text = msg; return; }
            Result = new PhoneEntry { Host = a, Port = port, Pin = p, CertSha = secure ? sha ?? "" : "", Id = foundId ?? "", Name = string.IsNullOrEmpty(foundName) ? a : foundName };
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Mot dong dien thoai tim thay: bieu tuong, ten, dia chi.</summary>
    public class PhoneRow : Control
    {
        public readonly FoundPhone Phone;
        public bool Selected;
        bool hover;

        public PhoneRow(FoundPhone p)
        {
            Phone = p;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(58); Margin = Theme.P(0, 0, 0, 6); Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = Theme.Round(r, Theme.S(12)))
            {
                using (var b = new SolidBrush(Selected ? Theme.AccentSoft : hover ? Theme.Hover : Theme.Panel)) g.FillPath(b, p);
                using (var pen = new Pen(Selected ? Theme.Accent : Theme.Line, Selected ? 1.5f : 1f)) g.DrawPath(pen, p);
            }
            var ic = new Rectangle(Theme.S(12), (Height - Theme.S(34)) / 2, Theme.S(34), Theme.S(34));
            using (var b = new SolidBrush(Selected ? Theme.Accent : Theme.Panel2)) g.FillEllipse(b, ic);
            TextRenderer.DrawText(g, "", Theme.IconsSmall, ic, Selected ? Color.White : Theme.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int x = ic.Right + Theme.S(12);
            string name = string.IsNullOrEmpty(Phone.Name) ? Phone.Model : Phone.Name;
            TextRenderer.DrawText(g, name, Theme.UIBold, new Rectangle(x, Theme.S(9), Width - x - Theme.S(40), Theme.S(22)), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            string sub = (string.IsNullOrEmpty(Phone.Model) || Phone.Model == name ? "" : Phone.Model + " · ") + Phone.Host;
            TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(x, Theme.S(31), Width - x - Theme.S(40), Theme.S(20)), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (Selected) TextRenderer.DrawText(g, "", Theme.IconsSmall, new Rectangle(Width - Theme.S(36), 0, Theme.S(24), Height), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
