using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LinkDesk
{
    /// <summary>Loc thong bao theo app, luu lich su thong bao, phim tat toan cuc, chuyen nhanh giua cac dien thoai.</summary>
    public partial class MainForm
    {
        // ------------------------------------------------------------------ loc theo app
        bool IsMuted(Dictionary<string, object> n) => Cfg.MutedApps.ContainsKey(n.S("pkg"));

        public void MuteApp(string pkg, string name, bool mute)
        {
            if (pkg == "") return;
            if (mute) Cfg.MutedApps[pkg] = name == "" ? pkg : name; else Cfg.MutedApps.Remove(pkg);
            Cfg.Save();
            RenderNotifs();
            if (mute) Toast("Đã ẩn thông báo của " + (name == "" ? pkg : name) + ". Bật lại trong Cài đặt > Thông báo.");
        }

        ContextMenuStrip NotifMenu(Dictionary<string, object> n)
        {
            var m = new ContextMenuStrip();
            string app = n.S("app") == "" ? n.S("pkg") : n.S("app");
            m.Items.Add("Ẩn thông báo của " + app, null, (s, e) => MuteApp(n.S("pkg"), n.S("app"), true));
            m.Items.Add("Xem lịch sử thông báo…", null, (s, e) => ShowHistory(app));
            m.Items.Add("Chép nội dung", null, (s, e) => { try { Clipboard.SetText((n.S("title") + "\n" + n.S("text")).Trim()); } catch { } });
            return m;
        }

        // ------------------------------------------------------------------ lich su
        readonly NotifHistory history = new NotifHistory();

        void RecordNotif(Dictionary<string, object> n)
        {
            if (n.B("ongoing")) return;
            if (n.S("title") == "" && n.S("text") == "") return;
            history.Add(phoneName ?? "", n);
        }

        public void ShowHistory(string query = "")
        {
            using (var f = new HistoryForm(this, history, query)) f.ShowDialog(this);
        }

        // ------------------------------------------------------------------ phim tat toan cuc
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        const int WM_HOTKEY = 0x0312, HotkeyId = 0x4C44;
        public static readonly string[] HotkeyChoices = { "", "Ctrl+Alt+P", "Ctrl+Shift+P", "Ctrl+Alt+L", "Win+Shift+P" };
        bool hotkeyOn;

        public bool ApplyHotkey()
        {
            if (!IsHandleCreated) return false;
            try { if (hotkeyOn) UnregisterHotKey(Handle, HotkeyId); } catch { }
            hotkeyOn = false;
            string k = Cfg.Hotkey ?? "";
            if (k == "") return true;
            uint mods = 0x4000; // MOD_NOREPEAT
            uint vk = 0;
            foreach (var part in k.Split('+'))
            {
                switch (part)
                {
                    case "Ctrl": mods |= 2; break;
                    case "Alt": mods |= 1; break;
                    case "Shift": mods |= 4; break;
                    case "Win": mods |= 8; break;
                    case "Space": vk = 0x20; break;
                    default: if (part.Length == 1) vk = part[0]; break;
                }
            }
            try { hotkeyOn = RegisterHotKey(Handle, HotkeyId, mods, vk); } catch { hotkeyOn = false; }
            return hotkeyOn;
        }

        void OnHotkey()
        {
            if (Visible && WindowState != FormWindowState.Minimized && ContainsFocus)
            {
                if (Cfg.MinimizeToTray) Hide(); else WindowState = FormWindowState.Minimized;
            }
            else
            {
                ShowFromTray();
                Activate();
                screen.Focus();
            }
        }

        // ------------------------------------------------------------------ nhieu dien thoai
        void ShowPhoneMenu(Control anchor)
        {
            var m = new ContextMenuStrip();
            FillPhoneMenu(m.Items);
            m.Show(anchor, new Point(0, anchor.Height));
        }

        void FillPhoneMenu(ToolStripItemCollection items)
        {
            items.Clear();
            foreach (var p in Cfg.Phones.ToList())
            {
                var pe = p;
                string label = (pe.Name == "" ? pe.Host : pe.Name) + "  (" + pe.Host + ")";
                bool cur = current != null && pe.Host == current.Host && pe.Port == current.Port;
                var it = new ToolStripMenuItem(label) { Checked = cur };
                it.Click += (s, e) =>
                {
                    if (cur && client != null && client.IsOpen) return;
                    Cfg.Remember(pe);
                    Connect(pe);
                };
                items.Add(it);
            }
            if (Cfg.Phones.Count > 0) items.Add(new ToolStripSeparator());
            if (current != null) items.Add("Kết nối lại", null, (s, e) => Connect(current));
            items.Add("Thêm / kết nối điện thoại khác…", null, (s, e) => { ShowFromTray(); ShowConnect(null); });
            if (current != null && Cfg.Phones.Count > 1)
            {
                var cp = current;
                items.Add("Quên điện thoại này", null, (s, e) =>
                {
                    Cfg.Phones.RemoveAll(x => x.Host == cp.Host && x.Port == cp.Port);
                    var next = Cfg.Phones.FirstOrDefault();
                    if (next != null) { Cfg.Remember(next); Connect(next); } else Cfg.Save();
                });
            }
        }
    }

    /// <summary>Lich su thong bao luu tren may tinh (toi da 2000 muc).</summary>
    public class NotifHistory
    {
        public class Entry
        {
            public long Time { get; set; }
            public string Phone { get; set; } = "";
            public string Pkg { get; set; } = "";
            public string App { get; set; } = "";
            public string Title { get; set; } = "";
            public string Text { get; set; } = "";
        }

        const int Max = 2000;
        static string FilePath => Path.Combine(Config.Dir, "notif-history.json");
        List<Entry> items;
        readonly Timer saveTimer = new Timer { Interval = 3000 };

        public NotifHistory() { saveTimer.Tick += (s, e) => { saveTimer.Stop(); Save(); }; }

        public List<Entry> Items
        {
            get
            {
                if (items == null)
                {
                    try { items = File.Exists(FilePath) ? new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) : null; } catch { }
                    if (items == null) items = new List<Entry>();
                }
                return items;
            }
        }

        public void Add(string phone, Dictionary<string, object> n)
        {
            var e = new Entry { Time = n.L("time"), Phone = phone, Pkg = n.S("pkg"), App = n.S("app"), Title = n.S("title"), Text = n.S("text") };
            if (e.Time == 0) e.Time = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (e.Text.Length > 2000) e.Text = e.Text.Substring(0, 2000);
            var list = Items;
            if (seen == null) { seen = new HashSet<string>(); foreach (var o in list) seen.Add(Key(o)); }
            if (!seen.Add(Key(e))) return;
            // Cung app, cung tieu de va noi dung trong vong 10 phut -> coi la mot (thong bao cap nhat lai)
            for (int i = list.Count - 1; i >= 0 && i >= list.Count - 50; i--)
            {
                var o = list[i];
                if (o.Pkg == e.Pkg && o.Title == e.Title && o.Text == e.Text && Math.Abs(o.Time - e.Time) < 600000) return;
            }
            list.Add(e);
            if (list.Count > Max) list.RemoveRange(0, list.Count - Max);
            saveTimer.Stop(); saveTimer.Start();
        }

        HashSet<string> seen;
        static string Key(Entry e) => e.Pkg + "|" + e.Time + "|" + e.Title + "|" + e.Text;

        public void Clear() { Items.Clear(); seen = null; Save(); }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                File.WriteAllText(FilePath, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(Items), System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>Cua so xem va tim trong lich su thong bao.</summary>
    public class HistoryForm : Form
    {
        readonly NotifHistory history;
        readonly FlowLayoutPanel list;
        readonly Label info = Theme.Lbl("", Theme.Small, Theme.Muted);
        readonly RoundInput search = new RoundInput("Tìm theo app, người gửi, nội dung… (không cần dấu)");
        readonly Timer debounce = new Timer { Interval = 200 };

        public HistoryForm(MainForm main, NotifHistory history, string query)
        {
            this.history = history;
            Text = "Lịch sử thông báo – LinkDesk";
            Icon = MainForm.AppIcon;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.UI;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Theme.Sz(620, 720);
            MinimumSize = Theme.Sz(480, 400);
            ShowInTaskbar = false;
            Theme.StyleWindow(this, Theme.Bg);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Theme.P(20, 16, 20, 8), BackColor = Theme.Bg, WrapContents = false };
            search.Width = Theme.S(440); search.Height = Theme.S(34); search.Margin = Theme.P(0, 0, 8, 0);
            search.Box.Text = query ?? "";
            var clear = new RoundButton("", "Xoá lịch sử", BtnStyle.Ghost) { Margin = new Padding(0) };
            clear.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Xoá toàn bộ lịch sử thông báo đã lưu trên máy tính này?", "LinkDesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                history.Clear(); Render();
            };
            top.Controls.Add(search); top.Controls.Add(clear);
            info.Dock = DockStyle.Top; info.AutoSize = false; info.Height = Theme.S(24); info.Padding = Theme.P(22, 0, 0, 0);

            list = Theme.Scroll(new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = Theme.P(20, 4, 20, 16), BackColor = Theme.Bg });
            Controls.Add(list); Controls.Add(info); Controls.Add(top);

            debounce.Tick += (s, e) => { debounce.Stop(); Render(); };
            search.Box.TextChanged += (s, e) => { debounce.Stop(); debounce.Start(); };
            Resize += (s, e) => { debounce.Stop(); debounce.Start(); };
            Shown += (s, e) => { Render(); search.Box.Focus(); };
        }

        static string Fold(string s)
        {
            var n = (s ?? "").ToLowerInvariant().Replace('đ', 'd').Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(n.Length);
            foreach (var ch in n) if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return sb.ToString();
        }

        void Render()
        {
            var words = Fold(search.Box.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var all = history.Items;
            var hits = new List<NotifHistory.Entry>();
            int total = 0;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var e = all[i];
                string hay = Fold(e.App + " " + e.Title + " " + e.Text + " " + e.Phone);
                if (words.All(w => hay.Contains(w))) { total++; if (hits.Count < 300) hits.Add(e); }
            }
            info.Text = all.Count == 0 ? "Chưa có thông báo nào được lưu. LinkDesk lưu lại các thông báo nhận được khi đang kết nối."
                : words.Length == 0 ? all.Count + " thông báo đã lưu" + (all.Count > hits.Count ? " (hiện " + hits.Count + " mới nhất)" : "")
                : total + " kết quả" + (total > hits.Count ? " (hiện " + hits.Count + ")" : "");

            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) { list.Controls.Remove(c); c.Dispose(); }
            int w = Math.Max(Theme.S(300), list.ClientSize.Width - Theme.S(48));
            string lastDay = null;
            foreach (var e in hits)
            {
                var t = DateTimeOffset.FromUnixTimeMilliseconds(e.Time).LocalDateTime;
                string day = t.Date == DateTime.Today ? "Hôm nay" : t.Date == DateTime.Today.AddDays(-1) ? "Hôm qua" : t.ToString("dd/MM/yyyy");
                if (day != lastDay)
                {
                    var d = Theme.Lbl(day, Theme.UIBold, Theme.Muted); d.Margin = Theme.P(2, lastDay == null ? 0 : 10, 0, 6);
                    list.Controls.Add(d); lastDay = day;
                }
                var card = new Card { Fill = Theme.Panel, Padding = Theme.P(14, 10, 14, 10), Margin = Theme.P(0, 0, 0, 8) };
                card.MinimumSize = card.MaximumSize = new Size(w, 0);
                var head = Theme.Lbl(e.App + " · " + t.ToString("HH:mm") + (string.IsNullOrEmpty(e.Phone) ? "" : " · " + e.Phone), Theme.Small, Theme.Muted);
                card.Controls.Add(head);
                if (e.Title != "") { var l = Theme.Lbl(e.Title, Theme.UIBold); l.MaximumSize = new Size(w - Theme.S(30), 0); l.Margin = Theme.P(0, 2, 0, 0); card.Controls.Add(l); }
                if (e.Text != "")
                {
                    string tx = e.Text.Length > 500 ? e.Text.Substring(0, 500) + "…" : e.Text;
                    var l = Theme.Lbl(tx, Theme.UI, Theme.Text2); l.MaximumSize = new Size(w - Theme.S(30), 0); l.Margin = Theme.P(0, 2, 0, 0); card.Controls.Add(l);
                }
                var ent = e;
                var menu = new ContextMenuStrip();
                menu.Items.Add("Chép nội dung", null, (s, ev) => { try { Clipboard.SetText((ent.Title + "\n" + ent.Text).Trim()); } catch { } });
                card.ContextMenuStrip = menu;
                foreach (Control c in card.Controls) c.ContextMenuStrip = menu;
                list.Controls.Add(card);
            }
            list.ResumeLayout();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) debounce.Dispose();
            base.Dispose(disposing);
        }
    }
}
