using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkDesk
{
    /// <summary>Hop thoai ket noi: tu tim dien thoai trong WiFi, nhap PIN.</summary>
    public class ConnectForm : Form
    {
        readonly ListBox list = new ListBox();
        readonly TextBox addr = Theme.Input("Ví dụ 192.168.1.5");
        readonly TextBox pin = Theme.Input("6 số");
        readonly Label status = Theme.Lbl("", Theme.Small, Theme.Muted);
        readonly Label err = Theme.Lbl("", Theme.UI, Theme.Red);
        readonly Button scanBtn = Theme.Btn("Tìm lại");
        readonly Button okBtn = Theme.Btn("Kết nối", primary: true);
        readonly Config cfg;
        public PhoneEntry Result;
        string foundId = "", foundName = "";

        public ConnectForm(Config cfg, string message = null)
        {
            this.cfg = cfg;
            Text = "Kết nối điện thoại – LinkDesk";
            Icon = MainForm.AppIcon;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = Theme.UI;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = Theme.Sz(460, 470);
            Padding = Theme.P(20, 20, 20, 20);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Panel };
            Controls.Add(t);
            t.Controls.Add(Theme.Lbl("Kết nối điện thoại", Theme.Title));
            var help = Theme.Lbl("Trên điện thoại: mở app LinkDesk → bấm \"Bắt đầu chia sẻ màn hình\". Hai máy phải dùng chung WiFi.", Theme.UI, Theme.Muted);
            help.MaximumSize = Theme.Sz(410, 0);
            help.Margin = Theme.P(0, 4, 0, 10);
            t.Controls.Add(help);

            var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Theme.P(0, 0, 0, 0), WrapContents = false };
            row.Controls.Add(Theme.Lbl("Điện thoại tìm thấy:", Theme.UIBold));
            row.Controls.Add(status);
            t.Controls.Add(row);

            list.Dock = DockStyle.Fill;
            list.Height = Theme.S(120);
            list.BackColor = Theme.Panel2; list.ForeColor = Theme.Text; list.BorderStyle = BorderStyle.FixedSingle;
            list.IntegralHeight = false;
            list.Font = Theme.UI;
            list.SelectedIndexChanged += (s, e) =>
            {
                if (list.SelectedItem is FoundPhone p)
                {
                    addr.Text = p.Port == 8686 ? p.Host : p.Host + ":" + p.Port;
                    foundId = p.Id; foundName = p.Name;
                    var saved = cfg.Phones.FirstOrDefault(x => x.Id == p.Id && p.Id != "");
                    if (saved != null && pin.Text == "") pin.Text = saved.Pin;
                    pin.Focus();
                }
            };
            list.DoubleClick += (s, e) => okBtn.PerformClick();
            t.Controls.Add(list);
            scanBtn.Margin = Theme.P(0, 6, 0, 12);
            scanBtn.Click += async (s, e) => await Scan();
            t.Controls.Add(scanBtn);

            t.Controls.Add(Theme.Lbl("Địa chỉ (nếu không tự tìm thấy):", Theme.UIBold));
            addr.Width = Theme.S(410); addr.Margin = Theme.P(0, 4, 0, 10);
            t.Controls.Add(addr);
            t.Controls.Add(Theme.Lbl("Mã PIN đang hiện trong app:", Theme.UIBold));
            pin.Width = Theme.S(160); pin.MaxLength = 6; pin.Font = new Font("Segoe UI", 14f); pin.Margin = Theme.P(0, 4, 0, 6);
            pin.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; okBtn.PerformClick(); } };
            t.Controls.Add(pin);
            err.MaximumSize = Theme.Sz(410, 0);
            err.Text = message ?? "";
            t.Controls.Add(err);
            okBtn.Margin = Theme.P(0, 8, 0, 0);
            okBtn.MinimumSize = Theme.Sz(140, 34);
            okBtn.Click += async (s, e) => await DoConnect();
            t.Controls.Add(okBtn);
            AcceptButton = null;

            var last = cfg.Last;
            if (last != null) { addr.Text = last.Port == 8686 ? last.Host : last.Host + ":" + last.Port; pin.Text = last.Pin; foundId = last.Id; foundName = last.Name; }
            Shown += async (s, e) => await Scan();
        }

        async Task Scan()
        {
            scanBtn.Enabled = false;
            status.Text = "  đang tìm…";
            try
            {
                var found = await Discovery.Scan(1500);
                list.Items.Clear();
                foreach (var f in found) list.Items.Add(f);
                status.Text = found.Count == 0 ? "  không thấy máy nào" : "  " + found.Count + " máy";
                if (found.Count == 1 && string.IsNullOrEmpty(addr.Text)) list.SelectedIndex = 0;
                else if (found.Count > 0)
                {
                    int i = found.FindIndex(f => f.Id == foundId && foundId != "");
                    if (i >= 0) list.SelectedIndex = i;
                }
            }
            catch { status.Text = "  lỗi khi tìm"; }
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
            if (p.Length != 6) { err.Text = "Mã PIN gồm 6 số."; pin.Focus(); return; }
            okBtn.Enabled = false; err.ForeColor = Theme.Muted; err.Text = "Đang kết nối…";
            var (st, msg) = await PhoneClient.Check(a, port, p);
            okBtn.Enabled = true; err.ForeColor = Theme.Red;
            if (st != ConnState.Connected) { err.Text = msg; return; }
            Result = new PhoneEntry { Host = a, Port = port, Pin = p, Id = foundId ?? "", Name = string.IsNullOrEmpty(foundName) ? a : foundName };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
