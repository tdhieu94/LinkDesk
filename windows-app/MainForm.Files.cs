using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LinkDesk
{
    // Nhac dang phat, duyet tep trong dien thoai, nhan tep "Gui sang may tinh".
    public partial class MainForm
    {
        // ------------------------------------------------------------------ nhac dang phat
        Card mediaCard;
        PictureBox mediaArt;
        Label mediaApp, mediaTitle, mediaArtist;
        RoundButton mediaPlay;
        long mediaArtId = -1;

        Control BuildMediaCard()
        {
            var host = new Panel { Dock = DockStyle.Top, Height = Theme.S(112), BackColor = Theme.Panel, Padding = Theme.P(20, 4, 20, 8), Visible = false };
            mediaCard = new Card { Fill = Theme.Bg, Dock = DockStyle.Fill, AutoSize = false, FlowDirection = FlowDirection.LeftToRight, Padding = Theme.P(10, 10, 10, 10) };
            mediaArt = new PictureBox { Size = Theme.Sz(80, 80), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Panel2, Margin = Theme.P(0, 0, 12, 0) };
            var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
            int tw = Theme.S(360 - 40 - 20 - 92 - 4);
            mediaApp = new Label { AutoSize = false, Size = new Size(tw, Theme.S(16)), Font = Theme.Small, ForeColor = Theme.Muted, AutoEllipsis = true, Margin = new Padding(0) };
            mediaTitle = new Label { AutoSize = false, Size = new Size(tw, Theme.S(20)), Font = Theme.UIBold, ForeColor = Theme.Text, AutoEllipsis = true, Margin = new Padding(0) };
            mediaArtist = new Label { AutoSize = false, Size = new Size(tw, Theme.S(18)), Font = Theme.Small, ForeColor = Theme.Text2, AutoEllipsis = true, Margin = new Padding(0) };
            var ctl = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.P(-4, 0, 0, 0) };
            RoundButton B(string g, string tip, string a)
            {
                var b = new RoundButton(g, "", BtnStyle.Ghost) { Size = Theme.Sz(32, 30), Radius = 8, Margin = new Padding(0) };
                new ToolTip().SetToolTip(b, tip);
                b.Click += (s, e) => client?.Send("t", "media", "a", a);
                ctl.Controls.Add(b);
                return b;
            }
            B("", "Bài trước", "prev");
            mediaPlay = B("", "Phát / tạm dừng", "toggle");
            B("", "Bài tiếp", "next");
            col.Controls.Add(mediaApp); col.Controls.Add(mediaTitle); col.Controls.Add(mediaArtist); col.Controls.Add(ctl);
            mediaCard.Controls.Add(mediaArt); mediaCard.Controls.Add(col);
            foreach (Control c in col.Controls) c.BackColor = mediaCard.Fill;
            ctl.BackColor = mediaCard.Fill;
            host.Controls.Add(mediaCard);
            mediaArt.Resize += (s, e) => { using (var p = Theme.Round(new Rectangle(0, 0, mediaArt.Width, mediaArt.Height), Theme.S(8))) mediaArt.Region = new Region(p); };
            using (var p = Theme.Round(new Rectangle(0, 0, mediaArt.Width, mediaArt.Height), Theme.S(8))) mediaArt.Region = new Region(p);
            return host;
        }

        async void OnMedia(Dictionary<string, object> m)
        {
            var host = mediaCard.Parent;
            if (!m.B("active")) { host.Visible = false; return; }
            host.Visible = true;
            mediaApp.Text = m.S("app");
            mediaTitle.Text = m.S("title") == "" ? "(không có tên)" : m.S("title");
            mediaArtist.Text = m.S("artist");
            mediaPlay.Glyph = m.B("playing") ? "" : "";
            mediaPlay.Invalidate();
            long id = m.L("art");
            if (id != mediaArtId)
            {
                mediaArtId = id;
                if (id == 0) { SetArt(null); return; }
                try
                {
                    var b = await client.GetBytes("/mediaart", "i", id.ToString());
                    if (mediaArtId != id) return;
                    using (var ms = new MemoryStream(b)) SetArt(new Bitmap(Image.FromStream(ms)));
                }
                catch { SetArt(null); }
            }
        }

        void SetArt(Image img)
        {
            var old = mediaArt.Image;
            mediaArt.Image = img;
            old?.Dispose();
        }

        // ------------------------------------------------------------------ tep trong dien thoai
        readonly FlowLayoutPanel phoneFiles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Theme.P(0, 8, 0, 0) };
        readonly Label phonePath = new Label { AutoSize = true, Font = Theme.UIBold, ForeColor = Theme.Text, Margin = Theme.P(0, 8, 0, 0) };
        readonly Label phoneFilesInfo = new Label { AutoSize = true, Font = Theme.UI, ForeColor = Theme.Muted, Margin = Theme.P(0, 6, 0, 0) };
        string phoneDir = "Download";
        int filesGen;
        static string DownloadDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "LinkDesk");

        void BuildPhoneFiles()
        {
            var c = PageCard(filesFlow, "", "Tệp trong điện thoại");
            c.Controls.Add(Wrap("Bấm thư mục để mở, bấm tệp để tải về thư mục Tải xuống\\LinkDesk. Kéo tệp ra Desktop hoặc thư mục để lưu ở đó, chuột phải để chọn chỗ lưu."));
            var quick = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = Theme.Sz(490, 0), Margin = Theme.P(0, 10, 0, 0) };
            foreach (var (label, dir) in new[] { ("Tải xuống", "Download"), ("Ảnh chụp", "DCIM"), ("Tài liệu", "Documents"), ("Ảnh", "Pictures"), ("Phim", "Movies"), ("Nhạc", "Music"), ("Tất cả", "") })
            {
                var b = new RoundButton("", label, BtnStyle.Soft) { Margin = Theme.P(0, 0, 6, 6) };
                var d = dir;
                b.Click += (s, e) => LoadPhoneDir(d);
                quick.Controls.Add(b);
            }
            c.Controls.Add(quick);
            var nav = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.P(0, 4, 0, 0) };
            var up = new RoundButton("", "Lên", BtnStyle.Ghost) { Margin = Theme.P(0, 0, 6, 0) };
            up.Click += (s, e) => { if (phoneDir != "") LoadPhoneDir(phoneDir.Contains('/') ? phoneDir.Substring(0, phoneDir.LastIndexOf('/')) : ""); };
            var refresh = new RoundButton("", "", BtnStyle.Ghost) { Margin = new Padding(0) };
            refresh.Click += (s, e) => LoadPhoneDir(phoneDir);
            nav.Controls.Add(up); nav.Controls.Add(refresh); nav.Controls.Add(phonePath);
            c.Controls.Add(nav);
            c.Controls.Add(phoneFilesInfo);
            c.Controls.Add(phoneFiles);
            phonePath.MaximumSize = Theme.Sz(380, 0);
            foreach (Control x in c.Controls) if (!(x is RoundButton)) x.BackColor = c.Fill;
        }

        bool phoneFilesLoaded;
        void OnFilesTab() { if (!phoneFilesLoaded) LoadPhoneDir(phoneDir); }

        async void LoadPhoneDir(string dir)
        {
            var c = client;
            int gen = ++filesGen;
            phoneDir = dir;
            phonePath.Text = "Bộ nhớ trong" + (dir == "" ? "" : " › " + dir.Replace("/", " › "));
            foreach (Control x in phoneFiles.Controls.Cast<Control>().ToList()) { phoneFiles.Controls.Remove(x); x.Dispose(); }
            if (c == null || !c.IsOpen) { phoneFilesInfo.Text = "Chưa kết nối điện thoại."; return; }
            phoneFilesInfo.ForeColor = Theme.Muted;
            phoneFilesInfo.Text = "Đang tải…";
            try
            {
                var b = await c.GetBytes("/files", "path", dir);
                if (gen != filesGen) return;
                var o = J.Parse(System.Text.Encoding.UTF8.GetString(b));
                var items = (o.TryGetValue("items", out var it) ? it as object[] : null)?.OfType<Dictionary<string, object>>().ToList() ?? new List<Dictionary<string, object>>();
                phoneFilesLoaded = true;
                phoneFilesInfo.Text = items.Count == 0 ? "Thư mục trống." : items.Count + " mục";
                phoneFiles.SuspendLayout();
                foreach (var f in items.Take(400))
                {
                    var row = new RoundButton(f.B("dir") ? "" : FileGlyph(f.S("name")), "", BtnStyle.Ghost) { Margin = new Padding(0), Radius = 8 };
                    string name = f.S("name");
                    row.Text = name.Length > 46 ? name.Substring(0, 43) + "…" : name;
                    row.AutoFit(Theme.S(490));
                    if (!f.B("dir")) new ToolTip().SetToolTip(row, name + "\n" + FmtSize(f.L("size")) + " · " + TimeStr(f.L("time")));
                    string rel = dir == "" ? name : dir + "/" + name;
                    if (f.B("dir")) row.Click += (s, e) => LoadPhoneDir(rel);
                    else
                    {
                        row.Click += (s, e) => { if (!WasDragged(row)) DownloadPhoneFile(rel, name, null, true); };
                        long size = f.L("size");
                        EnableDragOut(row, () => FetchForDrag(rel, name, size));
                        var menu = new ContextMenuStrip();
                        menu.Items.Add("Tải về và mở", null, (s, e) => DownloadPhoneFile(rel, name, null, true));
                        menu.Items.Add("Lưu thành…", null, (s, e) =>
                        {
                            using (var d = new SaveFileDialog { FileName = name, Title = "Lưu tệp từ điện thoại" })
                                if (d.ShowDialog(this) == DialogResult.OK) DownloadPhoneFile(rel, name, d.FileName, false);
                        });
                        row.ContextMenuStrip = menu;
                    }
                    phoneFiles.Controls.Add(row);
                }
                phoneFiles.ResumeLayout();
            }
            catch (Exception ex)
            {
                if (gen != filesGen) return;
                phoneFilesInfo.ForeColor = Theme.Orange;
                phoneFilesInfo.MaximumSize = Theme.Sz(480, 0);
                phoneFilesInfo.Text = ex.Message;
            }
        }

        static string FileGlyph(string name)
        {
            string e = Path.GetExtension(name).ToLowerInvariant();
            if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic" }.Contains(e)) return "";
            if (new[] { ".mp4", ".mkv", ".mov", ".3gp", ".webm" }.Contains(e)) return "";
            if (new[] { ".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus" }.Contains(e)) return "";
            if (new[] { ".zip", ".rar", ".7z", ".apk" }.Contains(e)) return "";
            return "";
        }

        async void DownloadPhoneFile(string rel, string name, string target, bool open)
        {
            var c = client;
            if (c == null) return;
            string file = target ?? UniquePath(DownloadDir, name);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                Toast("Đang tải " + name + "…");
                int lastPct = -1;
                await c.DownloadTo("/file", file, new Progress<double>(v => { int p = (int)(v * 100); if (p / 10 != lastPct / 10) { lastPct = p; Toast("Đang tải " + name + "… " + p + "%"); } }), "path", rel);
                Toast("Đã tải về " + file);
                if (open) try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { }
            }
            catch (Exception ex) { Toast("Không tải được: " + ex.Message); }
        }

        static string UniquePath(string dir, string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            string p = Path.Combine(dir, name);
            if (!File.Exists(p)) return p;
            string b = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 2; ; i++) { p = Path.Combine(dir, b + " (" + i + ")" + ext); if (!File.Exists(p)) return p; }
        }

        // ------------------------------------------------------------------ tep dien thoai gui sang
        async void OnIncoming(Dictionary<string, object> m)
        {
            var c = client;
            if (c == null) return;
            string name = m.S("name");
            string file = UniquePath(DownloadDir, name == "" ? "tep" : name);
            try
            {
                Directory.CreateDirectory(DownloadDir);
                Toast("Đang nhận " + name + "…");
                await c.DownloadTo("/shared", file, null, "id", m.S("id"));
                if (m.B("clip")) { SetClipboardImageFromPhone(file); return; }
                Toast("Đã nhận " + Path.GetFileName(file) + " (Tải xuống\\LinkDesk)");
                if (!Visible || WindowState == FormWindowState.Minimized || !ContainsFocus)
                {
                    balloonIsUpdate = false; lastReceived = file;
                    tray.ShowBalloonTip(5000, "Đã nhận tệp từ điện thoại", Path.GetFileName(file), ToolTipIcon.None);
                }
                else
                {
                    try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { }
                }
            }
            catch (Exception ex) { Toast("Không nhận được tệp: " + ex.Message); }
        }

        string lastReceived;
    }
}
