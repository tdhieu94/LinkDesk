using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LinkDesk
{
    // Phan "Anh", bieu tuong ung dung trong thong bao, dong bo clipboard, ghim cua so.
    public partial class MainForm
    {
        readonly FlowLayoutPanel photosFlow = NewPage();
        readonly FlowLayoutPanel photoGrid = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, Margin = new Padding(0), Padding = new Padding(0) };
        readonly Label photoInfo = Theme.Lbl("", Theme.UI, Theme.Muted);
        RoundButton photoRefresh;
        bool photosLoaded;
        int photoGen;

        static string PhotoDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LinkDesk", "Ảnh điện thoại");

        void BuildPhotos()
        {
            var c = PageCard(photosFlow, "", "Ảnh gần đây");
            c.Controls.Add(Wrap("Bấm vào ảnh để mở trên máy tính. Chuột phải để lưu, chép hoặc mở thư mục. Ảnh được lưu vào Ảnh\\LinkDesk."));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.P(0, 10, 0, 0), BackColor = c.Fill };
            photoRefresh = new RoundButton("", "Làm mới", BtnStyle.Soft) { Margin = Theme.P(0, 0, 8, 0) };
            photoRefresh.Click += (s, e) => LoadPhotos();
            var open = new RoundButton("", "Mở thư mục", BtnStyle.Ghost) { Margin = new Padding(0) };
            open.Click += (s, e) => { Directory.CreateDirectory(PhotoDir); Process.Start("explorer.exe", "\"" + PhotoDir + "\""); };
            row.Controls.Add(photoRefresh); row.Controls.Add(open);
            c.Controls.Add(row);
            photoInfo.MaximumSize = Theme.Sz(340, 0);
            photoInfo.Margin = Theme.P(2, 0, 0, 10);
            photosFlow.Controls.Add(photoInfo);
            photosFlow.Controls.Add(photoGrid);
            photoCard = c;
            photosFlow.Resize += (s, e) => LayoutThumbs();
        }

        Card photoCard;
        int PhotoAvail() => Math.Max(Theme.S(300), photosFlow.ClientSize.Width - photosFlow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        int ThumbSize()
        {
            int avail = PhotoAvail(), gap = Theme.S(6);
            int cols = Math.Max(3, (avail + gap) / (Theme.S(150) + gap));
            return (avail + gap) / cols - gap;
        }

        void LayoutThumbs()
        {
            int t = ThumbSize(), avail = PhotoAvail();
            if (photoCard != null) photoCard.MinimumSize = photoCard.MaximumSize = new Size(avail, 0);
            photoGrid.MaximumSize = new Size(avail + Theme.S(6), 0);
            foreach (Control x in photoGrid.Controls) x.Size = new Size(t, t);
        }

        void OnPhotosTab()
        {
            if (!photosLoaded) LoadPhotos();
        }

        async void LoadPhotos()
        {
            var c = client;
            int gen = ++photoGen;
            foreach (Control x in photoGrid.Controls.Cast<Control>().ToList()) { photoGrid.Controls.Remove(x); x.Dispose(); }
            if (c == null || !c.IsOpen) { photoInfo.Text = "Chưa kết nối điện thoại."; return; }
            if (LastStatus.Has("photos") && !LastStatus.B("photos"))
            {
                photoInfo.Text = "Điện thoại chưa cho phép xem ảnh. Mở app LinkDesk trên điện thoại → mục Quyền → \"Cho phép xem ảnh\", rồi bấm Làm mới.";
                photoInfo.ForeColor = Theme.Orange;
                return;
            }
            photoInfo.ForeColor = Theme.Muted;
            photoInfo.Text = "Đang tải danh sách ảnh…";
            photoRefresh.Enabled = false;
            try
            {
                var bytes = await c.GetBytes("/photos", "limit", "90");
                if (gen != photoGen) return;
                var list = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(System.Text.Encoding.UTF8.GetString(bytes)) as object[];
                var items = (list ?? new object[0]).OfType<Dictionary<string, object>>().ToList();
                photoInfo.Text = items.Count == 0 ? "Không có ảnh nào." : items.Count + " ảnh mới nhất";
                photosLoaded = true;
                int t = ThumbSize();
                photoGrid.SuspendLayout();
                foreach (var it in items)
                {
                    var th = new Thumb { Size = new Size(t, t), Margin = Theme.P(0, 0, 6, 6), Tag = it };
                    new ToolTip().SetToolTip(th, it.S("name") + "\n" + TimeStr(it.L("time")) + " · " + FmtSize(it.L("size")));
                    th.Click += (s, e) => { if (((MouseEventArgs)e).Button == MouseButtons.Left) OpenPhoto(it, null); };
                    th.ContextMenuStrip = PhotoMenu(it);
                    photoGrid.Controls.Add(th);
                }
                photoGrid.ResumeLayout();
                LayoutThumbs();
                // tai anh nho lan luot (it ket noi cung luc de dien thoai khong qua tai)
                foreach (var th in photoGrid.Controls.OfType<Thumb>().ToList())
                {
                    if (gen != photoGen) return;
                    try
                    {
                        var tb = await c.GetBytes("/thumb", "id", ((Dictionary<string, object>)th.Tag).S("id"));
                        if (gen != photoGen || th.IsDisposed) return;
                        using (var ms = new MemoryStream(tb)) th.Image = new Bitmap(Image.FromStream(ms));
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                if (gen == photoGen) { photoInfo.Text = "Không tải được ảnh: " + ex.Message; photoInfo.ForeColor = Theme.Orange; }
            }
            finally { if (gen == photoGen) photoRefresh.Enabled = true; }
        }

        static string FmtSize(long b) => b >= 1 << 20 ? (b / 1048576.0).ToString("0.0") + " MB" : Math.Max(1, b / 1024) + " KB";

        ContextMenuStrip PhotoMenu(Dictionary<string, object> it)
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Mở", null, (s, e) => OpenPhoto(it, null));
            m.Items.Add("Lưu thành…", null, (s, e) =>
            {
                using (var d = new SaveFileDialog { FileName = it.S("name"), Title = "Lưu ảnh" })
                    if (d.ShowDialog(this) == DialogResult.OK) OpenPhoto(it, d.FileName);
            });
            m.Items.Add("Chép ảnh vào clipboard", null, async (s, e) =>
            {
                var file = await DownloadPhoto(it, null);
                if (file == null) return;
                try { using (var img = Image.FromFile(file)) Clipboard.SetImage(new Bitmap(img)); Toast("Đã chép ảnh vào clipboard"); } catch { }
            });
            m.Items.Add("Mở thư mục ảnh", null, (s, e) => { Directory.CreateDirectory(PhotoDir); Process.Start("explorer.exe", "\"" + PhotoDir + "\""); });
            return m;
        }

        async System.Threading.Tasks.Task<string> DownloadPhoto(Dictionary<string, object> it, string target)
        {
            var c = client;
            if (c == null) return null;
            string name = it.S("name");
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            if (name == "") name = it.S("id") + ".jpg";
            string file = target ?? Path.Combine(PhotoDir, name);
            if (target == null && File.Exists(file) && new FileInfo(file).Length == it.L("size")) return file;
            try
            {
                Toast("Đang tải " + name + "…");
                var data = await c.GetBytes("/photo", "id", it.S("id"));
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, data);
                return file;
            }
            catch (Exception ex) { Toast("Không tải được ảnh: " + ex.Message); return null; }
        }

        async void OpenPhoto(Dictionary<string, object> it, string target)
        {
            var file = await DownloadPhoto(it, target);
            if (file == null) return;
            if (target != null) { Toast("Đã lưu " + file); return; }
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch (Exception ex) { Toast(ex.Message); }
        }

        // ------------------------------------------------------------------ bieu tuong ung dung
        readonly Dictionary<string, Image> appIcons = new Dictionary<string, Image>();
        readonly HashSet<string> iconPending = new HashSet<string>();

        Control AppIconBox(string pkg)
        {
            var pb = new PictureBox { Size = Theme.Sz(18, 18), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0), BackColor = Theme.Panel, Tag = pkg };
            if (appIcons.TryGetValue(pkg, out var img)) pb.Image = img;
            else LoadIcon(pkg);
            return pb;
        }

        async void LoadIcon(string pkg)
        {
            var c = client;
            if (c == null || pkg == "" || iconPending.Contains(pkg)) return;
            iconPending.Add(pkg);
            try
            {
                var b = await c.GetBytes("/icon", "pkg", pkg);
                Image img;
                using (var ms = new MemoryStream(b)) img = new Bitmap(Image.FromStream(ms));
                appIcons[pkg] = img;
                foreach (var pb in AllControls(notifFlow).OfType<PictureBox>()) if (pb.Tag as string == pkg) pb.Image = img;
                OnAppIcon(pkg, img);
            }
            catch { appIcons[pkg] = null; }
        }

        static IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var x in AllControls(c)) yield return x;
            }
        }

        // ------------------------------------------------------------------ clipboard tu dong
        [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        const int WM_CLIPBOARDUPDATE = 0x031D;
        string lastClipFromPhone, lastClipSent;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { AddClipboardFormatListener(Handle); } catch { }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { RemoveClipboardFormatListener(Handle); } catch { }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE) BeginInvoke((Action)SyncClipboard);
            base.WndProc(ref m);
        }

        void SyncClipboard()
        {
            if (!Cfg.ClipSync || client == null || !client.IsOpen) return;
            try
            {
                if (!Clipboard.ContainsText()) return;
                string t = Clipboard.GetText();
                if (t == "" || t == lastClipFromPhone || t == lastClipSent || t.Length > 100000) return;
                lastClipSent = t;
                client.Send("t", "clip", "s", t, "quiet", true);
            }
            catch { }
        }

        // ------------------------------------------------------------------ ghim cua so
        RoundButton pinBtn;
        void ApplyTopMost()
        {
            TopMost = Cfg.TopMost;
            if (pinBtn != null) { pinBtn.Toggled = Cfg.TopMost; pinBtn.Invalidate(); }
        }
    }

    /// <summary>O anh vuong, cat giua, bo goc.</summary>
    public class Thumb : Control
    {
        Image img;
        bool hover;
        public Image Image { get => img; set { img = value; Invalidate(); } }

        public Thumb()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = Theme.Round(r, Theme.S(8)))
            {
                using (var b = new SolidBrush(Theme.Panel2)) g.FillPath(b, p);
                if (img != null)
                {
                    float s = Math.Max((float)Width / img.Width, (float)Height / img.Height);
                    float sw = Width / s, sh = Height / s;
                    var src = new RectangleF((img.Width - sw) / 2, (img.Height - sh) / 2, sw, sh);
                    g.SetClip(p);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, new RectangleF(0, 0, Width, Height), src, GraphicsUnit.Pixel);
                    g.ResetClip();
                }
                if (hover)
                    using (var pen = new Pen(Theme.Accent, 2)) g.DrawPath(pen, p);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) img?.Dispose();
            base.Dispose(disposing);
        }
    }
}
