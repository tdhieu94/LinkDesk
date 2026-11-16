using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LinkDesk
{
    /// <summary>Cua so Cai dat: hinh anh (khung/s, do net, chat luong), man hinh dien thoai, ung dung, cap nhat.</summary>
    public class SettingsForm : Form
    {
        readonly MainForm main;
        bool loading = true;

        public static readonly int[] FpsValues = { 10, 15, 24, 30, 45, 60 };
        public static readonly (int value, string label)[] SizeValues =
        {
            (720, "720p — nhẹ nhất"), (1080, "1080"), (1280, "1280 — cân bằng"), (1600, "1600"), (1920, "1920 — nét"), (4096, "Gốc của điện thoại"),
        };

        readonly ComboBox fps = Combo();
        readonly ComboBox size = Combo();
        readonly TrackBar quality = new TrackBar { Minimum = 30, Maximum = 95, TickFrequency = 5, SmallChange = 5, LargeChange = 10, AutoSize = false };
        readonly Label qualityLbl = Theme.Lbl("", Theme.UI, Theme.Muted);

        static ComboBox Combo() => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.Panel2, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Font = Theme.UI, Width = Theme.S(220) };

        static CheckBox Chk(string text, bool value) => new CheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = Theme.Text, Font = Theme.UI, Margin = Theme.P(0, 8, 0, 0) };

        static Label Hint(string text)
        {
            var l = Theme.Lbl(text, Theme.Small, Theme.Muted);
            l.MaximumSize = Theme.Sz(470, 0);
            l.Margin = Theme.P(22, 0, 0, 4);
            return l;
        }

        Card Section(FlowLayoutPanel parent, string title)
        {
            var c = new Card { MinimumSize = Theme.Sz(520, 0) };
            c.Controls.Add(new Label { Text = title, AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, Margin = Theme.P(0, 0, 0, 8) });
            parent.Controls.Add(c);
            return c;
        }

        static FlowLayoutPanel Row(params Control[] cs)
        {
            var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.P(0, 4, 0, 4) };
            foreach (var c in cs) r.Controls.Add(c);
            return r;
        }

        static Label RowLabel(string t) { var l = Theme.Lbl(t, Theme.UI); l.Width = Theme.S(170); l.AutoSize = false; l.Height = Theme.S(28); l.TextAlign = ContentAlignment.MiddleLeft; return l; }

        public SettingsForm(MainForm main)
        {
            this.main = main;
            var st = main.LastStatus;
            var cfg = main.Cfg;
            Text = "Cài đặt – LinkDesk";
            Icon = MainForm.AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Theme.Sz(580, 680);
            ShowInTaskbar = false;

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = Theme.P(20, 18, 20, 18), BackColor = Theme.Bg };
            Controls.Add(flow);

            // --- hinh anh
            var v = Section(flow, "Hình ảnh");
            foreach (var f in FpsValues) fps.Items.Add(f + " khung/giây");
            foreach (var s in SizeValues) size.Items.Add(s.label);
            int curFps = (int)st.L("fps", 30), curSize = (int)st.L("maxSize", 1280), curQ = (int)st.L("quality", 70);
            fps.SelectedIndex = Math.Max(0, Array.FindIndex(FpsValues, x => x >= curFps));
            size.SelectedIndex = Math.Max(0, Array.FindIndex(SizeValues, x => x.value >= curSize));
            quality.Value = Math.Max(30, Math.Min(95, curQ));
            quality.Width = Theme.S(220); quality.Height = Theme.S(30);
            quality.BackColor = Theme.Panel;
            v.Controls.Add(Row(RowLabel("Số khung hình/giây"), fps));
            v.Controls.Add(Row(RowLabel("Độ nét (cạnh dài)"), size));
            v.Controls.Add(Row(RowLabel("Chất lượng ảnh"), quality, qualityLbl));
            v.Controls.Add(Hint("Khung/giây cao và độ nét cao cần WiFi mạnh và điện thoại khoẻ. Nếu bị giật hoặc trễ, hãy giảm độ nét trước (ví dụ 1080), sau đó mới giảm khung/giây."));
            UpdateQualityLabel();
            EventHandler apply = (s, e) => { UpdateQualityLabel(); if (!loading) SendVideo(); };
            fps.SelectedIndexChanged += apply;
            size.SelectedIndexChanged += apply;
            quality.ValueChanged += (s, e) => UpdateQualityLabel();
            quality.MouseUp += (s, e) => SendVideo();
            quality.KeyUp += (s, e) => SendVideo();

            // --- man hinh dien thoai
            var d = Section(flow, "Màn hình điện thoại khi đang kết nối");
            var awake = Chk("Không tự tắt màn hình (để không bị ngắt kết nối)", st.Has("keepAwake") ? st.B("keepAwake") : true);
            awake.CheckedChanged += (s, e) => { if (!loading) main.Send("t", "keepAwake", "on", awake.Checked); };
            d.Controls.Add(awake);
            var dark = Chk("Làm tối màn hình điện thoại (máy tính vẫn thấy hình)", st.B("dark"));
            dark.CheckedChanged += (s, e) => { if (!loading) main.Send("t", "dark", "on", dark.Checked); };
            d.Controls.Add(dark);
            d.Controls.Add(Hint(st.B("superDark")
                ? "Chế độ siêu tối đang dùng được: ngoài độ sáng thấp nhất, điện thoại còn bật \"Làm mờ thêm\" nên gần như đen hẳn."
                : "Android không cho tắt hẳn màn hình mà vẫn gửi hình nếu không có quyền gỡ lỗi. Muốn màn hình tối gần như đen hẳn (siêu tối), xem mục \"Nâng cao\" trong hướng dẫn: chạy lệnh adb 1 lần rồi tắt gỡ lỗi."));

            // --- ung dung
            var a = Section(flow, "Ứng dụng");
            var startup = Chk("Khởi động cùng Windows (chạy ẩn ở khay)", Installer.IsStartupEnabled());
            startup.CheckedChanged += (s, e) => Installer.SetStartup(startup.Checked);
            var tray = Chk("Bấm X thì thu xuống khay hệ thống", cfg.MinimizeToTray);
            tray.CheckedChanged += (s, e) => { cfg.MinimizeToTray = tray.Checked; cfg.Save(); };
            var notif = Chk("Báo tin nhắn của điện thoại bằng thông báo Windows", cfg.ShowPhoneNotifications);
            notif.CheckedChanged += (s, e) => { cfg.ShowPhoneNotifications = notif.Checked; cfg.Save(); };
            var upd = Chk("Tự kiểm tra bản cập nhật", cfg.AutoUpdate);
            upd.CheckedChanged += (s, e) => { cfg.AutoUpdate = upd.Checked; cfg.Save(); };
            a.Controls.Add(startup); a.Controls.Add(tray); a.Controls.Add(notif); a.Controls.Add(upd);

            // --- dien thoai
            var p = Section(flow, "Điện thoại");
            var info = Theme.Lbl(main.PhoneInfo, Theme.UI, Theme.Muted);
            info.MaximumSize = Theme.Sz(480, 0);
            p.Controls.Add(info);
            var other = new RoundButton("", "Kết nối điện thoại khác…", BtnStyle.Soft) { Margin = Theme.P(0, 10, 8, 0) };
            other.Click += (s, e) => { Close(); main.ShowConnect(null); };
            var bank = new RoundButton("", "Chế độ ngân hàng (tắt hết)", BtnStyle.Danger) { Margin = Theme.P(0, 10, 0, 0) };
            bank.Click += (s, e) => { Close(); main.BankMode(); };
            p.Controls.Add(Row(other, bank));

            // --- ve
            var ab = Section(flow, "LinkDesk " + Installer.Version);
            var updLbl = Theme.Lbl("", Theme.UI, Theme.Muted);
            var check = new RoundButton("", "Kiểm tra cập nhật", BtnStyle.Soft) { Margin = Theme.P(0, 4, 8, 0) };
            check.Click += async (s, e) =>
            {
                updLbl.Text = "Đang kiểm tra…";
                var i = await Updater.Check();
                if (i == null) updLbl.Text = "Bạn đang dùng bản mới nhất.";
                else { updLbl.Text = "Có bản " + i.Version + "."; main.OfferUpdate(i, true); }
            };
            var unin = new RoundButton("", "Gỡ cài đặt…", BtnStyle.Ghost) { Margin = Theme.P(0, 4, 0, 0) };
            unin.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Installer.IsInstalled ? Installer.InstalledExe : Installer.CurrentExe, "--uninstall") { UseShellExecute = true }); } catch { }
            };
            ab.Controls.Add(Row(check, unin));
            updLbl.Margin = Theme.P(0, 8, 0, 0);
            ab.Controls.Add(updLbl);

            Shown += (s, e) => loading = false;
        }

        void UpdateQualityLabel() => qualityLbl.Text = "  " + quality.Value + "%";

        void SendVideo()
        {
            if (loading) return;
            int f = FpsValues[Math.Max(0, fps.SelectedIndex)];
            int sz = SizeValues[Math.Max(0, size.SelectedIndex)].value;
            main.Send("t", "video", "maxSize", sz, "quality", quality.Value, "fps", f);
        }
    }
}
