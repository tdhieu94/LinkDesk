using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkDesk
{
    /// <summary>Quay man hinh dien thoai ra tep, xoay man hinh, cua so nho luon noi (PiP).</summary>
    public partial class MainForm
    {
        // ------------------------------------------------------------------ quay man hinh
        ScreenRecorder recorder;
        RoundButton recBtn;

        static string VideoDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "LinkDesk");

        void ToggleRecord()
        {
            if (recorder != null) { StopRecord(); return; }
            if (client == null || !client.IsOpen || !screen.HasFrame) { Toast("Chưa có hình từ điện thoại để quay."); return; }
            try
            {
                Directory.CreateDirectory(VideoDir);
                var size = screen.FrameSize;
                string basePath = Path.Combine(VideoDir, "quay-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                recorder = ScreenRecorder.Start(basePath, size.Width, size.Height, Cfg.Audio);
                UpdateViewing(true);
                if (recBtn != null) { recBtn.Toggled = true; recBtn.Glyph = ""; recBtn.Invalidate(); }
                Toast(recorder.IsMp4 ? "Đang quay màn hình điện thoại… Bấm lại nút để dừng." : "Đang quay (định dạng AVI)… Bấm lại nút để dừng.");
            }
            catch (Exception ex) { recorder = null; Toast("Không quay được: " + ex.Message); }
        }

        async void StopRecord()
        {
            var r = recorder;
            if (r == null) return;
            recorder = null;
            if (recBtn != null) { recBtn.Toggled = false; recBtn.Glyph = ""; recBtn.Invalidate(); }
            UpdateViewing();
            Toast("Đang lưu video…");
            await Task.Run(() => r.Stop());
            if (IsDisposed) return;
            if (File.Exists(r.FilePath) && new FileInfo(r.FilePath).Length > 0)
            {
                Toast("Đã lưu video " + (int)r.Elapsed.TotalSeconds + " giây: " + r.FilePath);
                try { Process.Start("explorer.exe", "/select,\"" + r.FilePath + "\""); } catch { }
            }
            else Toast("Không lưu được video" + (string.IsNullOrEmpty(r.Error) ? "." : ": " + r.Error));
        }

        void RecordFrame(Bitmap bmp, int w, int h)
        {
            var r = recorder;
            if (r == null) return;
            try { r.AddFrame(bmp, w, h); } catch { }
        }

        string RecordChip()
        {
            var r = recorder;
            if (r == null) return null;
            var t = r.Elapsed;
            return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
        }

        // ------------------------------------------------------------------ xoay man hinh
        void Rotate(string mode)
        {
            if (!PhoneAtLeast(0, 15)) { Toast("Cần cập nhật LinkDesk trên điện thoại lên bản 0.15 để xoay màn hình."); return; }
            client?.Send("t", "rotate", "mode", mode);
        }

        // ------------------------------------------------------------------ cua so nho luon noi
        PipForm pip;

        void TogglePip()
        {
            if (pip != null) { pip.Close(); return; }
            if (fullscreen) ToggleFullscreen();
            pip = new PipForm(this, screen);
            pip.FormClosed += (s, e) =>
            {
                pip.Release();
                center.Controls.Add(screen);
                center.Controls.SetChildIndex(screen, 0);
                screen.Compact = false;
                pip = null;
                ShowFromTray();
                UpdateViewing();
            };
            screen.Compact = true;
            pip.Show();
            Hide();
            UpdateViewing();
        }

        public void SendKey(string k) => Key(k);
    }

    /// <summary>Cua so nho khong vien, luon noi tren cung, chi co man hinh dien thoai. Keo thanh tren de di chuyen, keo goc de doi kich thuoc.</summary>
    public class PipForm : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2, WM_NCHITTEST = 0x84;

        readonly MainForm main;
        readonly ScreenView screen;
        readonly Panel strip = new Panel { Dock = DockStyle.Top };
        readonly int grip;

        public PipForm(MainForm main, ScreenView screen)
        {
            this.main = main; this.screen = screen;
            Text = "LinkDesk";
            Icon = MainForm.AppIcon;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = true;
            BackColor = Theme.Panel;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            grip = Theme.S(6);
            Padding = new Padding(grip, 0, grip, grip);
            MinimumSize = Theme.Sz(180, 260);

            var fs = screen.FrameSize;
            float aspect = fs.Width > 0 && fs.Height > 0 ? (float)fs.Width / fs.Height : 9f / 19.5f;
            int h = Theme.S(620), w = (int)(h * aspect);
            if (aspect > 1) { w = Theme.S(640); h = (int)(w / aspect); }
            var cfg = main.Cfg;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Rectangle r = new Rectangle(wa.Right - w - Theme.S(24) - 2 * grip, wa.Bottom - h - Theme.S(24) - Theme.S(30), w + 2 * grip, h + Theme.S(30) + grip);
            if (cfg.PipW > 0 && cfg.PipH > 0)
            {
                var saved = new Rectangle(cfg.PipX, cfg.PipY, cfg.PipW, cfg.PipH);
                foreach (var s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(saved)) { r = saved; break; }
            }
            Bounds = r;

            strip.Height = Theme.S(30);
            strip.BackColor = Theme.Panel;
            var title = new Label { Text = "LinkDesk", AutoSize = false, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, Padding = Theme.P(4, 0, 0, 0) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0), Padding = new Padding(0) };
            RoundButton B(string g, string tip, Action a)
            {
                var b = new RoundButton(g, "", BtnStyle.Ghost) { Size = Theme.Sz(28, 26), Radius = 6, Margin = Theme.P(0, 2, 0, 0) };
                new ToolTip().SetToolTip(b, tip);
                b.Click += (s, e) => { a(); screen.Focus(); };
                buttons.Controls.Add(b);
                return b;
            }
            B("", "Quay lại", () => main.SendKey("back"));
            B("", "Màn hình chính", () => main.SendKey("home"));
            B("", "Ứng dụng gần đây", () => main.SendKey("recents"));
            B("", "Mở lại cửa sổ lớn", Close);
            strip.Controls.Add(title);
            strip.Controls.Add(buttons);
            MouseEventHandler drag = (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero); } };
            strip.MouseDown += drag; title.MouseDown += drag;
            title.DoubleClick += (s, e) => Close();

            Controls.Add(screen);
            Controls.Add(strip);
            screen.Dock = DockStyle.Fill;
            Controls.SetChildIndex(screen, 0);
            KeyPreview = false;
        }

        /// <summary>Tra man hinh ve cho cua so chinh.</summary>
        public void Release()
        {
            if (screen.Parent == this) Controls.Remove(screen);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var b = Bounds;
            main.Cfg.PipX = b.X; main.Cfg.PipY = b.Y; main.Cfg.PipW = b.Width; main.Cfg.PipH = b.Height;
            main.Cfg.Save();
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && (int)m.Result == 1 /*HTCLIENT*/)
            {
                var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                bool l = p.X < grip, r = p.X >= Width - grip, b = p.Y >= Height - grip;
                if (b && r) m.Result = (IntPtr)17; else if (b && l) m.Result = (IntPtr)16;
                else if (b) m.Result = (IntPtr)15; else if (r) m.Result = (IntPtr)11; else if (l) m.Result = (IntPtr)10;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
