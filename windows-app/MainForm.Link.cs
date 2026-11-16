using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkDesk
{
    /// <summary>Mo link tren dien thoai, chep anh qua lai, keo tep tu dien thoai ra may tinh.</summary>
    public partial class MainForm
    {
        // ------------------------------------------------------------------ mo link
        void BuildLinkCard(FlowLayoutPanel page)
        {
            var c = PageCard(page, "", "Mở link trên điện thoại");
            c.Controls.Add(Wrap("Dán một địa chỉ web rồi bấm Mở, điện thoại sẽ mở bằng trình duyệt hoặc app phù hợp (YouTube, Maps…). Ngược lại, chia sẻ một link từ điện thoại bằng \"Gửi sang máy tính\" sẽ mở nó trên máy tính."));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.P(0, 10, 0, 0) };
            var input = new RoundInput("https://…") { Width = Theme.S(360), Height = Theme.S(34), Margin = Theme.P(0, 0, 8, 0) };
            var go = new RoundButton("", "Mở", BtnStyle.Primary) { Margin = new Padding(0) };
            Action open = () =>
            {
                string u = input.Box.Text.Trim();
                if (u == "") return;
                if (!PhoneAtLeast(0, 13)) { Toast("Cần cập nhật LinkDesk trên điện thoại lên bản 0.13 trở lên."); return; }
                client?.Send("t", "openUrl", "url", u);
                Toast("Đã gửi link sang điện thoại");
            };
            input.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; open(); } };
            input.Box.GotFocus += (s, e) =>
            {
                try
                {
                    if (input.Box.Text == "" && Clipboard.ContainsText())
                    {
                        string t = Clipboard.GetText().Trim();
                        if (LooksLikeUrl(t)) { input.Box.Text = t; input.Box.SelectAll(); }
                    }
                }
                catch { }
            };
            go.Click += (s, e) => open();
            row.Controls.Add(input);
            row.Controls.Add(go);
            c.Controls.Add(row);
        }

        static bool LooksLikeUrl(string t) =>
            t.Length < 4000 && t.IndexOfAny(new[] { ' ', '\n', '\r', '\t' }) < 0 &&
            (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        /// <summary>Tray: mo link dang co trong clipboard tren dien thoai.</summary>
        void OpenClipboardLinkOnPhone()
        {
            try
            {
                string t = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                if (!LooksLikeUrl(t)) { Toast("Clipboard không có link nào."); return; }
                if (client == null || !client.IsOpen) return;
                client.Send("t", "openUrl", "url", t);
                Toast("Đã gửi link sang điện thoại");
            }
            catch { }
        }

        void OnLink(Dictionary<string, object> m)
        {
            string u = m.S("url");
            if (!LooksLikeUrl(u)) return;
            try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); Toast("Đã mở link từ điện thoại"); }
            catch (Exception ex) { Toast("Không mở được link: " + ex.Message); }
        }

        // ------------------------------------------------------------------ anh trong clipboard
        string lastClipImageHash;
        DateTime ignoreClipUntil;

        static string Hash(byte[] b) { using (var h = SHA1.Create()) return Convert.ToBase64String(h.ComputeHash(b)); }

        /// <summary>Lay anh trong clipboard thanh PNG (null neu khong co).</summary>
        static byte[] ClipboardPng()
        {
            if (!Clipboard.ContainsImage()) return null;
            using (var img = Clipboard.GetImage())
            {
                if (img == null) return null;
                using (var ms = new MemoryStream()) { img.Save(ms, ImageFormat.Png); return ms.ToArray(); }
            }
        }

        /// <summary>Gui anh clipboard sang dien thoai. quiet = dong bo tu dong.</summary>
        async void SendClipImage(bool quiet)
        {
            var c = client;
            if (c == null || !c.IsOpen) return;
            if (!PhoneAtLeast(0, 13)) { if (!quiet) Toast("Cần cập nhật LinkDesk trên điện thoại lên bản 0.13 để chép ảnh."); return; }
            byte[] png;
            try { png = ClipboardPng(); } catch { return; }
            if (png == null || png.Length > 30 * 1024 * 1024) { if (!quiet && png != null) Toast("Ảnh quá lớn để chép (tối đa 30 MB)."); return; }
            string h = Hash(png);
            if (quiet && h == lastClipImageHash) return;
            lastClipImageHash = h;
            try
            {
                await c.PostBytes("/clipimage", png, "image/png");
                Toast("Đã chép ảnh vào bộ nhớ tạm của điện thoại");
            }
            catch (Exception ex) { if (!quiet) Toast("Không chép được ảnh: " + ex.Message); }
        }

        /// <summary>Dat anh nhan tu dien thoai vao clipboard ma khong gui nguoc lai.</summary>
        void SetClipboardImageFromPhone(string file)
        {
            try
            {
                using (var img = Image.FromFile(file))
                using (var bmp = new Bitmap(img))
                {
                    ignoreClipUntil = DateTime.UtcNow.AddSeconds(2);
                    Clipboard.SetImage(bmp);
                    try { using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); lastClipImageHash = Hash(ms.ToArray()); } } catch { }
                }
                Toast("Đã chép ảnh từ điện thoại vào clipboard (Ctrl+V để dán)");
            }
            catch (Exception ex) { Toast("Đã nhận ảnh nhưng không chép được vào clipboard: " + ex.Message); }
        }

        // ------------------------------------------------------------------ keo ra may tinh
        Control dragged;
        bool draggingOut;

        /// <summary>Cho phep keo mot o tu dien thoai ra Explorer/Desktop/app khac. fetch tai tep ve va tra ve duong dan.</summary>
        void EnableDragOut(Control ctl, Func<Task<string>> fetch)
        {
            Point down = Point.Empty;
            bool armed = false, busy = false;
            ctl.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { down = e.Location; armed = true; dragged = null; } };
            ctl.MouseUp += (s, e) => armed = false;
            ctl.MouseMove += async (s, e) =>
            {
                if (!armed || busy || e.Button != MouseButtons.Left) return;
                var ds = SystemInformation.DragSize;
                if (Math.Abs(e.X - down.X) < ds.Width && Math.Abs(e.Y - down.Y) < ds.Height) return;
                armed = false; busy = true; dragged = ctl;
                try
                {
                    string file = await fetch();
                    if (file == null || !File.Exists(file)) return;
                    if ((Control.MouseButtons & MouseButtons.Left) == 0) { Toast("Đã tải xong, kéo lại để thả vào nơi bạn muốn."); return; }
                    var data = new DataObject(DataFormats.FileDrop, new[] { file });
                    draggingOut = true;
                    try { ctl.DoDragDrop(data, DragDropEffects.Copy); } finally { draggingOut = false; }
                }
                catch (Exception ex) { Toast("Không kéo được: " + ex.Message); }
                finally { busy = false; }
            };
        }

        /// <summary>Click sau khi vua keo thi bo qua.</summary>
        bool WasDragged(Control ctl)
        {
            if (dragged != ctl) return false;
            dragged = null;
            return true;
        }

        static string DragDir => Path.Combine(Path.GetTempPath(), "LinkDesk");

        async Task<string> FetchForDrag(string rel, string name, long size)
        {
            var c = client;
            if (c == null) return null;
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            string file = Path.Combine(DragDir, name);
            if (File.Exists(file) && new FileInfo(file).Length == size) return file;
            Directory.CreateDirectory(DragDir);
            Toast("Đang tải " + name + " để kéo ra…");
            await c.DownloadTo("/file", file, null, "path", rel);
            return file;
        }
    }
}
