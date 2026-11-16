using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LinkDesk
{
    /// <summary>Tu sao luu anh moi, ket noi lai nhanh, trang Chan doan.</summary>
    public partial class MainForm
    {
        // ------------------------------------------------------------------ sao luu anh
        public static string DefaultBackupDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LinkDesk", "Sao lưu");
        public string BackupRoot => string.IsNullOrEmpty(Cfg.BackupDir) ? DefaultBackupDir : Cfg.BackupDir;
        public string BackupInfo = "";
        bool backupRunning, backupAgain;
        Timer backupTimer, backupDelay;

        public void StartBackupSoon(int ms = 5000)
        {
            if (!Cfg.PhotoBackup) return;
            if (backupTimer == null)
            {
                backupTimer = new Timer { Interval = 10 * 60 * 1000 };
                backupTimer.Tick += (s, e) => RunBackup();
                backupTimer.Start();
            }
            if (backupDelay == null)
            {
                backupDelay = new Timer();
                backupDelay.Tick += (s, e) => { backupDelay.Stop(); RunBackup(); };
            }
            backupDelay.Stop(); backupDelay.Interval = Math.Max(1, ms); backupDelay.Start();
        }

        void OnPhotosChanged()
        {
            StartBackupSoon(1000);
            if (photosLoaded && tabs.Selected == 2) LoadPhotos();
        }

        static string BackupStateFile(PhoneEntry e) =>
            Path.Combine(Config.Dir, "backup-" + Safe(string.IsNullOrEmpty(e.Id) ? e.Host : e.Id) + ".txt");

        static string Safe(string s)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Trim() == "" ? "dien-thoai" : s.Trim();
        }

        public async void RunBackup()
        {
            var c = client; var entry = current;
            if (!Cfg.PhotoBackup || c == null || entry == null || !c.IsOpen) return;
            if (!LastStatus.B("photos")) { BackupInfo = "Chưa sao lưu được: điện thoại chưa cho phép xem ảnh."; return; }
            if (backupRunning) { backupAgain = true; return; }
            backupRunning = true;
            int count = 0, failed = 0;
            try
            {
                if (entry.BackupSince == 0) { entry.BackupSince = DateTimeOffset.Now.AddDays(-7).ToUnixTimeMilliseconds(); Cfg.Save(); }
                var bytes = await c.GetBytes("/photos", "limit", "300");
                var items = J.ParseArray(Encoding.UTF8.GetString(bytes)).OfType<Dictionary<string, object>>()
                    .Where(i => i.L("time") >= entry.BackupSince).OrderBy(i => i.L("time")).ToList();
                string stateFile = BackupStateFile(entry);
                var done = new HashSet<string>();
                try { if (File.Exists(stateFile)) foreach (var l in File.ReadAllLines(stateFile)) done.Add(l.Trim()); } catch { }
                string phoneDir = Path.Combine(BackupRoot, Safe(string.IsNullOrEmpty(phoneName) ? entry.Host : phoneName));
                foreach (var it in items)
                {
                    if (client != c || !Cfg.PhotoBackup) break;
                    string id = it.S("id");
                    if (done.Contains(id)) continue;
                    var when = DateTimeOffset.FromUnixTimeMilliseconds(it.L("time")).LocalDateTime;
                    string dir = Path.Combine(phoneDir, when.ToString("yyyy-MM"));
                    string name = Safe(it.S("name") == "" ? id + ".jpg" : it.S("name"));
                    string file = Path.Combine(dir, name);
                    try
                    {
                        Directory.CreateDirectory(dir);
                        if (!(File.Exists(file) && new FileInfo(file).Length == it.L("size")))
                        {
                            if (File.Exists(file)) file = UniquePath(dir, name);
                            BackupInfo = "Đang sao lưu " + name + "…";
                            await c.DownloadTo("/photo", file, null, "id", id);
                            try { File.SetLastWriteTime(file, when); } catch { }
                            count++;
                        }
                        done.Add(id);
                        File.AppendAllText(stateFile, id + Environment.NewLine);
                    }
                    catch { failed++; }
                }
                BackupInfo = "Lần sao lưu gần nhất: " + DateTime.Now.ToString("HH:mm dd/MM") +
                    (count > 0 ? " · " + count + " ảnh mới" : " · không có ảnh mới") + (failed > 0 ? " · " + failed + " ảnh lỗi" : "");
                if (count > 0) Toast("Đã sao lưu " + count + " ảnh mới vào " + phoneDir);
            }
            catch (Exception ex) { BackupInfo = "Sao lưu lỗi: " + ex.Message; }
            finally
            {
                backupRunning = false;
                if (backupAgain) { backupAgain = false; StartBackupSoon(2000); }
            }
        }

        // ------------------------------------------------------------------ ket noi lai nhanh
        void InitReconnectHooks()
        {
            NetworkChange.NetworkAddressChanged += (s, e) => KickSoon();
            NetworkChange.NetworkAvailabilityChanged += (s, e) => KickSoon();
            try { SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == PowerModes.Resume) KickSoon(); }; } catch { }
        }

        void KickSoon()
        {
            try { BeginInvoke(new Action(() => { var c = client; if (c != null && !c.IsOpen) c.Kick(); })); } catch { }
        }

        // ------------------------------------------------------------------ chan doan
        public PhoneClient Client => client;
        public PhoneEntry Current => current;
        public string ConnText => connText;
        public void Reconnect() { if (current != null) Connect(current); }

        public void ShowDiagnostics()
        {
            using (var f = new DiagForm(this)) f.ShowDialog(this);
        }
    }

    /// <summary>Trang Chan doan: tinh trang ket noi, do tre, quyen tren dien thoai, goi y khac phuc.</summary>
    public class DiagForm : Form
    {
        readonly MainForm main;
        readonly Label conn = new Label(), perms = new Label(), pc = new Label(), tips = new Label();
        readonly Timer t = new Timer { Interval = 1000 };

        public DiagForm(MainForm main)
        {
            this.main = main;
            Text = "Chẩn đoán kết nối – LinkDesk";
            Icon = MainForm.AppIcon;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.UI;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Theme.Sz(584, 700);
            ShowInTaskbar = false;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Theme.StyleWindow(this, Theme.Bg);

            var flow = Theme.Scroll(new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = Theme.P(20, 18, 20, 18), BackColor = Theme.Bg });
            Controls.Add(flow);
            Add(flow, "Kết nối", conn);
            Add(flow, "Gợi ý", tips);
            Add(flow, "Quyền trên điện thoại", perms);
            Add(flow, "Máy tính", pc);

            var copy = new RoundButton("", "Chép thông tin", BtnStyle.Soft) { Margin = Theme.P(0, 4, 8, 0) };
            copy.Click += (s, e) => { try { Clipboard.SetText(Report()); copy.SetText("Đã chép"); } catch { } };
            var re = new RoundButton("", "Kết nối lại", BtnStyle.Ghost) { Margin = Theme.P(0, 4, 0, 0) };
            re.Click += (s, e) => main.Reconnect();
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.Add(copy); row.Controls.Add(re);
            flow.Controls.Add(row);

            t.Tick += (s, e) => Fill();
            Shown += (s, e) => { Fill(); t.Start(); };
        }

        static void Add(FlowLayoutPanel flow, string title, Label body)
        {
            var c = new Card { MinimumSize = Theme.Sz(528, 0), MaximumSize = Theme.Sz(528, 0), Padding = Theme.P(18, 14, 16, 14), Margin = Theme.P(0, 0, 0, 12) };
            c.Controls.Add(new Label { Text = title, AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, Margin = Theme.P(0, 0, 0, 6) });
            body.AutoSize = true; body.MaximumSize = Theme.Sz(490, 0); body.Font = Theme.UI; body.ForeColor = Theme.Text2; body.BackColor = Color.Transparent;
            c.Controls.Add(body);
            flow.Controls.Add(c);
        }

        static string Yes(bool b) => b ? "✓" : "✗";

        void Fill()
        {
            var c = main.Client; var e = main.Current; var st = main.LastStatus;
            bool open = c != null && c.IsOpen;
            var sb = new StringBuilder();
            if (e == null) sb.AppendLine("Chưa chọn điện thoại nào.");
            else
            {
                sb.AppendLine("Điện thoại: " + (string.IsNullOrEmpty(e.Name) ? e.Host : e.Name) + (st.Has("appVersion") ? " · app " + st.S("appVersion") : ""));
                sb.AppendLine("Địa chỉ: " + (c?.Host ?? e.Host) + ":" + e.Port + (c != null && c.Secure ? " (cổng mã hoá " + (e.Port + Tls.PortOffset) + ")" : ""));
                sb.AppendLine("Trạng thái: " + main.ConnText + (open ? " từ " + c.ConnectedSince.ToString("HH:mm:ss") + " (" + Dur(DateTime.Now - c.ConnectedSince) + ")" : ""));
                if (c != null)
                {
                    sb.AppendLine("Mã hoá: " + (c.Secure ? "có · mã bảo mật " + Tls.ShortCode(c.ObservedSha ?? e.CertSha) : "không"));
                    sb.AppendLine("Độ trễ: " + (c.LatencyMs < 0 ? "đang đo…" : c.LatencyMs + " ms" + (c.LatencyMs < 60 ? " (tốt)" : c.LatencyMs < 150 ? " (trung bình)" : " (chậm)")));
                    sb.AppendLine("Hình: " + main.FpsNow + "/" + st.L("fps", 30) + " khung/s · " + (st.S("codec") == "h264" ? "H.264" : "JPEG") + " · " + st.L("maxSize", 1280) + " px · nhận " + (main.RxPerSec / 1024) + " KB/s");
                    sb.AppendLine("Âm thanh: " + (st.B("audio") ? "đang phát" : "tắt"));
                    sb.AppendLine("Số lần mất kết nối: " + c.Reconnects + (string.IsNullOrEmpty(c.LastError) ? "" : "\nLỗi gần nhất: " + c.LastError));
                }
            }
            conn.Text = sb.ToString().TrimEnd();

            perms.Text = !open ? "Kết nối để xem." :
                Yes(st.B("mirror")) + " Chia sẻ màn hình    " + Yes(st.B("control")) + " Điều khiển (Trợ năng)\n" +
                Yes(st.B("notif")) + " Thông báo    " + Yes(st.B("photos")) + " Ảnh    " + Yes(st.B("files")) + " Tệp\n" +
                Yes(st.B("sms")) + " Tin nhắn    " + Yes(st.B("calls")) + " Cuộc gọi    " + Yes(st.B("audioOk")) + " Âm thanh" +
                (st.Has("battery") ? "\nPin: " + st.L("battery") + "%" + (st.B("charging") ? " (đang sạc)" : "") : "");

            pc.Text = "LinkDesk " + Installer.Version + " · " + Environment.OSVersion.VersionString + "\n" +
                "Sao lưu ảnh: " + (main.Cfg.PhotoBackup ? "bật · " + main.BackupRoot + (main.BackupInfo != "" ? "\n" + main.BackupInfo : "") : "tắt");

            var tip = new List<string>();
            if (e != null && !open) tip.Add("Chưa kết nối được: kiểm tra điện thoại và máy tính cùng một WiFi, app LinkDesk trên điện thoại đang chạy (có thông báo \"LinkDesk đang chạy\"), và WiFi không bật chế độ cách ly thiết bị (mạng khách, mạng công cộng).");
            if (open && c.LatencyMs > 150) tip.Add("Độ trễ cao: đứng gần bộ phát WiFi hơn, dùng WiFi 5 GHz, hoặc cắm dây mạng cho máy tính.");
            if (open && main.FpsNow > 0 && main.FpsNow < st.L("fps", 30) / 2 && st.B("mirror")) tip.Add("Hình bị giật: vào Cài đặt giảm Độ nét xuống 1080 hoặc 720, hoặc giảm Chất lượng ảnh.");
            if (open && !c.Secure) tip.Add("Kết nối chưa mã hoá: cập nhật app LinkDesk trên điện thoại lên bản mới.");
            if (open && st.Has("control") && !st.B("control")) tip.Add("Chưa bật Trợ năng nên chỉ xem được: mở app LinkDesk trên điện thoại và bấm \"Cho phép điều khiển\".");
            if (open && !st.B("mirror")) tip.Add("Điện thoại đang không chia sẻ màn hình: mở app LinkDesk trên điện thoại và bấm \"Chia sẻ lại màn hình\".");
            if (c != null && c.Reconnects >= 5 && open) tip.Add("Hay mất kết nối: trên điện thoại vào Cài đặt > Pin, cho LinkDesk \"Không hạn chế\" để Android không tắt app khi chạy nền.");
            tips.Text = tip.Count == 0 ? "Mọi thứ đều ổn." : "• " + string.Join("\n• ", tip);
        }

        static string Dur(TimeSpan d) => d.TotalHours >= 1 ? (int)d.TotalHours + " giờ " + d.Minutes + " phút" : d.TotalMinutes >= 1 ? (int)d.TotalMinutes + " phút" : d.Seconds + " giây";

        string Report() => "LinkDesk – chẩn đoán " + DateTime.Now.ToString("HH:mm dd/MM/yyyy") + "\n\n" + conn.Text + "\n\n" + perms.Text + "\n\n" + pc.Text + "\n\nGợi ý:\n" + tips.Text;

        protected override void Dispose(bool disposing)
        {
            if (disposing) t.Dispose();
            base.Dispose(disposing);
        }
    }
}
