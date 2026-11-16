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
        public static readonly int[] SizeValues = { 720, 1080, 1280, 1600, 1920, 4096 };

        readonly Segmented fps = new Segmented("10", "15", "24", "30", "45", "60");
        readonly Segmented size = new Segmented("720", "1080", "1280", "1600", "1920", "Gốc");
        readonly Slider quality = new Slider { Minimum = 30, Maximum = 95, Step = 5 };
        readonly Label qualityLbl = Theme.Lbl("", Theme.UIBold, Theme.Text);

        static Toggle Chk(string text, bool value) { var t = new Toggle(text, value); t.SetWidth(Theme.S(494)); return t; }

        static Label Hint(string text)
        {
            var l = Theme.Lbl(text, Theme.Small, Theme.Muted);
            l.MaximumSize = Theme.Sz(490, 0);
            l.Margin = Theme.P(0, 2, 0, 4);
            return l;
        }

        Card Section(FlowLayoutPanel parent, string title)
        {
            var c = new Card { MinimumSize = Theme.Sz(528, 0), MaximumSize = Theme.Sz(528, 0), Padding = Theme.P(18, 16, 16, 16) };
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

        static Label RowLabel(string t) { var l = Theme.Lbl(t, Theme.UI); l.Width = Theme.S(150); l.AutoSize = false; l.Height = Theme.S(42); l.TextAlign = ContentAlignment.MiddleLeft; return l; }

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
            ClientSize = Theme.Sz(604, 700);
            ShowInTaskbar = false;

            Theme.StyleWindow(this, Theme.Bg);
            var flow = Theme.Scroll(new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = Theme.P(20, 18, 20, 18), BackColor = Theme.Bg });
            Controls.Add(flow);

            // --- hinh anh
            var v = Section(flow, "Hình ảnh");
            int curFps = (int)st.L("fps", 30), curSize = (int)st.L("maxSize", 1280), curQ = (int)st.L("quality", 70);
            fps.Select(Math.Max(0, Array.FindIndex(FpsValues, x => x >= curFps)));
            size.Select(Math.Max(0, Array.FindIndex(SizeValues, x => x >= curSize)));
            quality.Value = Math.Max(30, Math.Min(95, curQ));
            v.Controls.Add(Row(RowLabel("Số khung hình/giây"), fps));
            v.Controls.Add(Row(RowLabel("Độ nét (cạnh dài, px)"), size));
            qualityLbl.Margin = Theme.P(8, 12, 0, 0); quality.Margin = Theme.P(0, 7, 0, 0);
            v.Controls.Add(Row(RowLabel("Chất lượng ảnh"), quality, qualityLbl));
            var h264 = Chk("Video H.264 (thử nghiệm): mượt hơn, ít tốn mạng hơn", cfg.H264);
            h264.CheckedChanged += (s, e) => { if (!loading) main.SetH264(h264.Checked); };
            v.Controls.Add(h264);
            v.Controls.Add(Hint("H.264 dùng bộ giải mã có sẵn của Windows. Nếu hình bị lỗi màu, nhoè hoặc đứng, hãy tắt mục này để quay về chế độ ảnh. Trang web trên trình duyệt luôn dùng chế độ ảnh."));
            v.Controls.Add(Hint("Khung/giây và độ nét cao cần WiFi mạnh. Nếu bị giật hoặc trễ, giảm độ nét trước (ví dụ 1080), sau đó mới giảm khung/giây."));
            UpdateQualityLabel();
            fps.Changed += i => SendVideo();
            size.Changed += i => SendVideo();
            quality.ValueChanged += (s, e) => UpdateQualityLabel();
            quality.Committed += (s, e) => SendVideo();

            // --- am thanh
            var au = Section(flow, "Âm thanh");
            var audio = Chk("Nghe âm thanh điện thoại (nhạc, video) trên máy tính", cfg.Audio);
            audio.CheckedChanged += (s, e) => { if (!loading) main.SetAudio(audio.Checked); };
            au.Controls.Add(audio);
            var vol = new Slider { Minimum = 0, Maximum = 100, Step = 5, Value = cfg.AudioVolume };
            var volLbl = new Label { AutoSize = true, Font = Theme.UI, ForeColor = Theme.Muted, Text = cfg.AudioVolume + "%", Margin = Theme.P(8, 12, 0, 0) };
            vol.Margin = Theme.P(0, 7, 0, 0);
            vol.ValueChanged += (s, e) => { volLbl.Text = vol.Value + "%"; main.SetAudioVolume(vol.Value); };
            au.Controls.Add(Row(RowLabel("Âm lượng trên máy tính"), vol, volLbl));
            au.Controls.Add(Hint(st.B("audioOk")
                ? "Cần Android 10 trở lên và đang chia sẻ màn hình. Một số app (ví dụ app ngân hàng, cuộc gọi) không cho thu âm thanh nên sẽ im lặng. Điện thoại vẫn phát tiếng cùng lúc (Android không cho app thường tắt loa khi thu)."
                : "Trên điện thoại: mở app LinkDesk, mục Quyền, bấm \"Cho phép âm thanh\" (cần Android 10 trở lên)."));

            // --- giao dien
            var look = Section(flow, "Giao diện");
            string[] modes = { "dark", "light", "system" };
            var theme = new Segmented("Tối", "Sáng", "Theo Windows");
            theme.Select(Math.Max(0, Array.IndexOf(modes, cfg.Theme)));
            var restart = new RoundButton("\uE72C", "Mở lại để áp dụng", BtnStyle.Primary) { Visible = false, Margin = Theme.P(10, 2, 0, 0) };
            restart.Click += (s, e) => main.Restart();
            theme.Changed += i => { if (loading) return; cfg.Theme = modes[i]; cfg.Save(); restart.Visible = true; };
            look.Controls.Add(Row(RowLabel("Chế độ màu"), theme, restart));

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
            var clip = Chk("Tự gửi chữ vừa chép trên máy tính sang điện thoại", cfg.ClipSync);
            clip.CheckedChanged += (s, e) => { cfg.ClipSync = clip.Checked; cfg.Save(); };
            a.Controls.Add(startup); a.Controls.Add(tray); a.Controls.Add(notif); a.Controls.Add(clip); a.Controls.Add(upd);

            // --- dien thoai
            var p = Section(flow, "Điện thoại");
            
            var info = Theme.Lbl(main.PhoneInfo, Theme.UI, Theme.Muted);
            info.MaximumSize = Theme.Sz(460, 0);
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

        void UpdateQualityLabel() => qualityLbl.Text = quality.Value + "%";

        void SendVideo()
        {
            if (loading) return;
            int f = FpsValues[Math.Max(0, fps.Selected)];
            int sz = SizeValues[Math.Max(0, size.Selected)];
            main.Send("t", "video", "maxSize", sz, "quality", quality.Value, "fps", f);
        }
    }
}
