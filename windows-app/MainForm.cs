using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LinkDesk
{
    public partial class MainForm : Form
    {
        public static readonly Icon AppIcon = LoadIcon();
        static Icon LoadIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { return SystemIcons.Application; }
        }

        public readonly Config Cfg = Config.Load();
        public Dictionary<string, object> LastStatus = new Dictionary<string, object>();
        PhoneClient client;
        PhoneEntry current;
        bool startHidden, allowExit, connecting;
        string phoneName = "", phoneModel = "", androidVer = "";

        // --- khung chinh
        readonly Panel header = new Panel { Dock = DockStyle.Top, BackColor = Theme.Panel };
        readonly Panel left = new Panel { Dock = DockStyle.Left, BackColor = Theme.Bg };
        readonly Panel center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        readonly ScreenView screen = new ScreenView { Dock = DockStyle.Fill };
        readonly Panel barHost = new Panel { Dock = DockStyle.Bottom, BackColor = Theme.Bg };
        readonly Card bar = new Card { FlowDirection = FlowDirection.LeftToRight, Fill = Theme.Panel, Radius = 16 };
        readonly Label nameLbl = new Label { AutoSize = true, Font = Theme.Big, ForeColor = Theme.Text, BackColor = Theme.Panel };
        readonly Label subLbl = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Panel };
        readonly Label avatar = new Label { Text = "\uE8EA", Font = Theme.Icons, ForeColor = Theme.AccentText, BackColor = Theme.AccentSoft, TextAlign = ContentAlignment.MiddleCenter };
        readonly FlowLayoutPanel headerRight = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Panel };
        RoundButton updateBtn, darkBtn, paneBtn;
        readonly Label toastLabel = new Label { AutoSize = true, Visible = false, BackColor = Color.FromArgb(44, 50, 60), ForeColor = Theme.Text, Font = Theme.UI };
        readonly Timer toastTimer = new Timer { Interval = 4000 };
        readonly Timer statsTimer = new Timer { Interval = 1000 };
        readonly Timer updateTimer = new Timer { Interval = 6 * 60 * 60 * 1000 };
        readonly NotifyIcon tray = new NotifyIcon();

        // --- bang trai
        Segmented tabs;
        readonly FlowLayoutPanel notifFlow = NewPage();
        readonly FlowLayoutPanel filesFlow = NewPage();
        readonly FlowLayoutPanel typeFlow = NewPage();
        readonly Label notifEmpty = Theme.Lbl("Chưa có thông báo nào.", Theme.UI, Theme.Muted);
        readonly Label notifWarn = Theme.Lbl("Chưa bật quyền đọc thông báo cho LinkDesk trên điện thoại.", Theme.UI, Theme.Orange);
        readonly FlowLayoutPanel uploadList = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Panel };

        readonly Dictionary<string, Dictionary<string, object>> notifs = new Dictionary<string, Dictionary<string, object>>();
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        Updater.Info pendingUpdate;
        bool screenOn = true, projecting = true;

        static FlowLayoutPanel NewPage() => new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            BackColor = Theme.Bg, Padding = Theme.P(16, 4, 8, 16), Visible = false
        };

        public MainForm(bool minimized)
        {
            startHidden = minimized;
            Text = "LinkDesk";
            Icon = AppIcon;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = Theme.Sz(760, 560);
            ClientSize = new Size(Theme.S(Cfg.WindowWidth > 0 ? Cfg.WindowWidth : 1220), Theme.S(Cfg.WindowHeight > 0 ? Cfg.WindowHeight : 880));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;

            header.Height = Theme.S(68);
            left.Width = Theme.S(390);
            barHost.Height = Theme.S(76);

            center.Controls.Add(screen);
            center.Controls.Add(barHost);
            Controls.Add(center);
            Controls.Add(left);
            Controls.Add(header);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Line });
            Controls.SetChildIndex(header, Controls.Count - 1);

            BuildHeader();
            BuildBar();
            BuildLeft();
            left.Visible = Cfg.SidePanel;
            paneBtn.Toggled = left.Visible;

            screen.SendCmd = o => client?.Send(o);
            screen.BannerClick = () => { if (!screenOn) Key("wake"); };
            screen.Controls.Add(toastLabel);
            screen.Resize += (s, e) => PlaceToast();
            toastLabel.Padding = Theme.P(14, 8, 14, 8);

            tray.Icon = AppIcon;
            tray.Text = "LinkDesk";
            tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Mở LinkDesk", null, (s, e) => ShowFromTray());
            menu.Items.Add("Kết nối điện thoại khác…", null, (s, e) => { ShowFromTray(); ShowConnect(null); });
            menu.Items.Add("Chế độ ngân hàng (tắt hết trên điện thoại)", null, (s, e) => BankMode());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Thoát", null, (s, e) => ExitApp());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ShowFromTray();
            tray.BalloonTipClicked += (s, e) =>
            {
                ShowFromTray();
                if (balloonIsUpdate) OfferUpdate(pendingUpdate, true);
                else { left.Visible = true; tabs.Select(0); }
            };

            toastTimer.Tick += (s, e) => { toastTimer.Stop(); toastLabel.Visible = false; };
            statsTimer.Tick += (s, e) => UpdateStats();
            statsTimer.Start();
            updateTimer.Tick += async (s, e) => await AutoCheckUpdate();
            updateTimer.Start();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            screen.AllowDrop = true;
            screen.DragEnter += OnDragEnter;
            screen.DragDrop += OnDragDrop;

            var _ = Handle; // tao handle som de BeginInvoke dung duoc ca khi chay an
        }

        void OnDragEnter(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
        void OnDragDrop(object s, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { left.Visible = true; tabs.Select(2); foreach (var f in files) Upload(f); }
        }

        // ------------------------------------------------------------------ dau trang
        void BuildHeader()
        {
            int h = header.Height;
            avatar.Size = Theme.Sz(40, 40);
            avatar.Location = new Point(Theme.S(18), (h - avatar.Height) / 2);
            avatar.Paint += (s, e) => { };
            MakeRound(avatar, Theme.S(20));
            nameLbl.Location = new Point(Theme.S(70), Theme.S(12));
            subLbl.Location = new Point(Theme.S(71), Theme.S(40));
            nameLbl.Text = "LinkDesk";
            subLbl.Text = "Chưa kết nối";
            header.Controls.Add(avatar);
            header.Controls.Add(nameLbl);
            header.Controls.Add(subLbl);

            var gear = new RoundButton("\uE713", "", BtnStyle.Ghost);
            new ToolTip().SetToolTip(gear, "Cài đặt");
            gear.Click += (s, e) => OpenSettings();
            updateBtn = new RoundButton("\uE896", "Cập nhật", BtnStyle.Primary) { Visible = false };
            updateBtn.Click += (s, e) => OfferUpdate(pendingUpdate, true);
            headerRight.Controls.Add(gear);
            pinBtn = new RoundButton("\uE718", "", BtnStyle.Ghost);
            new ToolTip().SetToolTip(pinBtn, "Luôn nằm trên các cửa sổ khác");
            pinBtn.Click += (s, e) => { Cfg.TopMost = !Cfg.TopMost; Cfg.Save(); ApplyTopMost(); };
            headerRight.Controls.Add(pinBtn);
            headerRight.Controls.Add(updateBtn);
            header.Controls.Add(headerRight);
            EventHandler place = (s, e) => { headerRight.Location = new Point(header.Width - headerRight.Width - Theme.S(14), (header.Height - headerRight.Height) / 2); headerRight.BringToFront(); };
            header.Resize += place; headerRight.SizeChanged += place; header.Layout += (s, e) => place(s, e);
        }

        static void MakeRound(Control c, int r)
        {
            using (var p = Theme.Round(new Rectangle(0, 0, c.Width, c.Height), r)) c.Region = new Region(p);
        }

        // ------------------------------------------------------------------ thanh dieu khien duoi man hinh
        RoundButton BarBtn(string glyph, string tip, Action click)
        {
            var b = new RoundButton(glyph, "", BtnStyle.Ghost) { Margin = Theme.P(2, 0, 2, 0) };
            b.Size = Theme.Sz(42, 42);
            new ToolTip().SetToolTip(b, tip);
            b.Click += (s, e) => { click(); screen.Focus(); };
            bar.Controls.Add(b);
            return b;
        }

        void BarSep() => bar.Controls.Add(new Panel { Width = 1, Height = Theme.S(26), BackColor = Theme.Line, Margin = Theme.P(6, 8, 6, 8) });

        void Key(string k) => client?.Send("t", "key", "k", k);

        void BuildBar()
        {
            bar.Padding = Theme.P(8, 6, 8, 6);
            bar.Margin = new Padding(0);
            BarBtn("\uE72B", "Quay lại (Esc / chuột phải)", () => Key("back"));
            BarBtn("\uE80F", "Màn hình chính (chuột giữa)", () => Key("home"));
            BarBtn("\uE8A9", "Ứng dụng gần đây", () => Key("recents"));
            BarSep();
            BarBtn("\uEA8F", "Kéo thanh thông báo", () => Key("notifications"));
            BarBtn("\uE9E9", "Cài đặt nhanh", () => Key("quick"));
            BarSep();
            BarBtn("\uE993", "Giảm âm lượng", () => Key("voldown"));
            BarBtn("\uE995", "Tăng âm lượng", () => Key("volup"));
            BarSep();
            darkBtn = BarBtn("\uE708", "Làm tối màn hình điện thoại (máy tính vẫn thấy hình)", () => client?.Send("t", "dark", "on", !LastStatus.B("dark")));
            BarBtn("\uE706", "Bật sáng màn hình", () => Key("wake"));
            BarBtn("\uE72E", "Khoá màn hình", () => Key("lock"));
            BarBtn("\uE7E8", "Menu nguồn", () => Key("power"));
            BarSep();
            BarBtn("\uE722", "Chụp màn hình (lưu vào Ảnh\\LinkDesk và chép vào clipboard)", Snapshot);
            BarBtn("\uE740", "Toàn màn hình (F11)", ToggleFullscreen);
            paneBtn = BarBtn("\uE8A0", "Hiện/ẩn bảng thông báo và tệp", () =>
            {
                left.Visible = !left.Visible; paneBtn.Toggled = left.Visible; paneBtn.Invalidate();
                Cfg.SidePanel = left.Visible; Cfg.Save();
            });
            barHost.Controls.Add(bar);
            barHost.Resize += (s, e) => bar.Location = new Point(Math.Max(0, (barHost.Width - bar.Width) / 2), Theme.S(4));
        }

        // ------------------------------------------------------------------ bang trai
        void BuildLeft()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(60), BackColor = Theme.Bg };
            tabs = new Segmented("Thông báo", "Ảnh", "Tệp", "Gõ chữ");
            tabs.Location = new Point(Theme.S(16), Theme.S(14));
            top.Controls.Add(tabs);
            var pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            pages.Controls.Add(notifFlow);
            pages.Controls.Add(photosFlow);
            pages.Controls.Add(filesFlow);
            pages.Controls.Add(typeFlow);
            left.Controls.Add(pages);
            left.Controls.Add(top);
            left.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Theme.Line });
            tabs.Changed += i => { notifFlow.Visible = i == 0; photosFlow.Visible = i == 1; filesFlow.Visible = i == 2; typeFlow.Visible = i == 3; if (i == 1) OnPhotosTab(); };
            notifFlow.Visible = true;

            // --- thong bao
            notifWarn.MaximumSize = Theme.Sz(340, 0);
            notifWarn.Visible = false;
            notifWarn.Margin = Theme.P(0, 0, 0, 10);
            notifFlow.Controls.Add(notifWarn);
            notifEmpty.Margin = Theme.P(2, 6, 0, 0);
            notifFlow.Controls.Add(notifEmpty);
            notifFlow.Resize += (s, e) => { foreach (Control c in notifFlow.Controls) if (c is Card) c.MinimumSize = c.MaximumSize = new Size(CardWidth(), 0); };

            BuildPhotos();

            // --- tep
            var f = PageCard(filesFlow, "\uE898", "Gửi tệp sang điện thoại");
            f.Controls.Add(Wrap("Kéo thả tệp vào cửa sổ LinkDesk (hoặc thả lên màn hình điện thoại). Tệp được lưu trong thư mục Download/LinkDesk của điện thoại."));
            var pick = new RoundButton("\uE8E5", "Chọn tệp…", BtnStyle.Primary) { Margin = Theme.P(0, 10, 0, 4) };
            pick.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Multiselect = true, Title = "Chọn tệp gửi sang điện thoại" })
                    if (d.ShowDialog(this) == DialogResult.OK) foreach (var x in d.FileNames) Upload(x);
            };
            f.Controls.Add(pick);
            f.Controls.Add(uploadList);

            // --- go chu
            var t = PageCard(typeFlow, "\uE765", "Gõ chữ vào điện thoại");
            t.Controls.Add(Wrap("Bấm vào ô nhập trên điện thoại trước, rồi gõ ở đây. Enter để gửi, Shift+Enter để xuống dòng. Bạn cũng có thể bấm lên màn hình điện thoại rồi gõ thẳng bằng bàn phím."));
            var tb = new TextBox { Multiline = true, Height = Theme.S(90), Width = Theme.S(320), BackColor = Theme.Panel2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.UI, Margin = Theme.P(0, 10, 0, 0) };
            var send = new RoundButton("\uE724", "Gửi sang điện thoại", BtnStyle.Primary) { Margin = Theme.P(0, 8, 0, 0) };
            Action go = () => { if (tb.Text.Length > 0) { client?.Send("t", "text", "s", tb.Text); tb.Text = ""; } };
            tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; go(); } };
            send.Click += (s, e) => go();
            t.Controls.Add(tb);
            t.Controls.Add(send);

            var c = PageCard(typeFlow, "\uE77F", "Bộ nhớ tạm");
            c.Controls.Add(Wrap("Ctrl+V trên màn hình điện thoại: dán chữ từ máy tính.\nCtrl+C: chép chữ đang bôi đen trên điện thoại về máy tính."));
            var clipSend = new RoundButton("\uE8C8", "Gửi clipboard sang điện thoại", BtnStyle.Soft) { Margin = Theme.P(0, 10, 0, 0) };
            clipSend.Click += (s, e) => { try { if (Clipboard.ContainsText()) client?.Send("t", "clip", "s", Clipboard.GetText()); } catch { } };
            c.Controls.Add(clipSend);

            tabs.Select(0);
        }

        Label Wrap(string text)
        {
            var l = Theme.Lbl(text, Theme.UI, Theme.Muted);
            l.MaximumSize = Theme.Sz(320, 0);
            return l;
        }

        Card PageCard(FlowLayoutPanel page, string glyph, string title)
        {
            var c = new Card();
            c.MinimumSize = c.MaximumSize = Theme.Sz(352, 0);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.P(0, 0, 0, 8) };
            row.Controls.Add(new Label { Text = glyph, Font = Theme.IconsSmall, ForeColor = Theme.AccentText, AutoSize = true, Margin = Theme.P(0, 4, 6, 0) });
            row.Controls.Add(new Label { Text = title, Font = Theme.UIBold, ForeColor = Theme.Text, AutoSize = true, Margin = Theme.P(0, 2, 0, 0) });
            c.Controls.Add(row);
            row.BackColor = c.Fill;
            foreach (Control x in row.Controls) x.BackColor = c.Fill;
            page.Controls.Add(c);
            return c;
        }

        void Toast(string msg)
        {
            toastLabel.Text = msg;
            toastLabel.MaximumSize = new Size(Math.Max(Theme.S(200), screen.Width - Theme.S(60)), 0);
            toastLabel.Visible = true;
            toastLabel.BringToFront();
            PlaceToast();
            toastTimer.Stop(); toastTimer.Start();
        }

        void PlaceToast() => toastLabel.Location = new Point((screen.Width - toastLabel.Width) / 2, screen.Height - toastLabel.Height - Theme.S(18));

        void SetSub(string text, Color color)
        {
            subLbl.Text = text;
            subLbl.ForeColor = color;
        }

        // ------------------------------------------------------------------ ket noi
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            screen.Focus();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyTopMost();
            var last = Cfg.Last;
            if (last != null) Connect(last);
            else if (!startHidden) BeginInvoke(new Action(() => ShowConnect(null)));
            var t = new Timer { Interval = 4000 };
            t.Tick += async (s, ev) => { t.Stop(); t.Dispose(); await AutoCheckUpdate(); };
            t.Start();
        }

        public void ShowConnect(string message)
        {
            if (connecting) return;
            connecting = true;
            try
            {
                using (var f = new ConnectForm(Cfg, message))
                {
                    if (f.ShowDialog(Visible ? this : null) == DialogResult.OK && f.Result != null)
                    {
                        Cfg.Remember(f.Result);
                        Connect(f.Result);
                    }
                    else if (client == null)
                    {
                        screen.ClearFrame("Chưa kết nối điện thoại.\n\nBấm nút Cài đặt (bánh răng) → \"Kết nối điện thoại khác…\".");
                    }
                }
            }
            finally { connecting = false; }
        }

        void Connect(PhoneEntry e)
        {
            client?.Dispose();
            current = e;
            phoneName = e.Name;
            nameLbl.Text = string.IsNullOrEmpty(e.Name) ? e.Host : e.Name;
            notifs.Clear(); RenderNotifs();
            photosLoaded = false; photoGen++; iconPending.Clear();
            foreach (Control x in photoGrid.Controls.Cast<Control>().ToList()) { photoGrid.Controls.Remove(x); x.Dispose(); }
            photoInfo.Text = "";
            screen.Banner = null;
            screen.ClearFrame("Đang kết nối tới " + nameLbl.Text + "…");
            var c = new PhoneClient(e.Host, e.Port, e.Pin);
            client = c;
            c.Frame += (bmp, w, h) =>
            {
                if (client != c || IsDisposed) { bmp.Dispose(); return; }
                try { BeginInvoke(new Action(() => { if (client == c) screen.SetFrame(bmp, w, h); else bmp.Dispose(); })); } catch { bmp.Dispose(); }
            };
            c.Message += m => { try { BeginInvoke(new Action(() => { if (client == c) OnMessage(m); })); } catch { } };
            c.StateChanged += (st, msg) => { try { BeginInvoke(new Action(() => { if (client == c) OnState(st, msg); })); } catch { } };
            c.Start();
        }

        string connText = "Chưa kết nối";
        Color connColor = Theme.Muted;

        void OnState(ConnState st, string msg)
        {
            switch (st)
            {
                case ConnState.Connecting: connText = "Đang kết nối…"; connColor = Theme.Muted; break;
                case ConnState.Connected: connText = "Đã kết nối"; connColor = Theme.Green; tray.Text = "LinkDesk – đã kết nối"; break;
                case ConnState.Locked: connText = "Bị khoá tạm do nhập sai PIN"; connColor = Theme.Red; break;
                case ConnState.Disconnected:
                    connText = "Mất kết nối – đang thử lại"; connColor = Theme.Red;
                    tray.Text = "LinkDesk – mất kết nối";
                    screen.Banner = null;
                    screen.ClearFrame("Không thấy điện thoại.\n\nTrên điện thoại: mở LinkDesk → \"Bắt đầu\".\nLinkDesk sẽ tự kết nối lại.");
                    TryRediscover();
                    break;
                case ConnState.BadPin:
                    connText = "Sai mã PIN"; connColor = Theme.Red;
                    screen.ClearFrame("Mã PIN đã thay đổi.");
                    client?.Dispose(); client = null;
                    if (Visible) ShowConnect("Mã PIN đã thay đổi, hãy nhập mã mới đang hiện trong app.");
                    break;
            }
            UpdateStats();
        }

        DateTime lastRediscover;
        /// <summary>Dien thoai co the doi IP: tim lai theo Id da luu.</summary>
        async void TryRediscover()
        {
            if (current == null || string.IsNullOrEmpty(current.Id) || (DateTime.Now - lastRediscover).TotalSeconds < 10) return;
            lastRediscover = DateTime.Now;
            try
            {
                var found = await Discovery.Scan(1200);
                var p = found.FirstOrDefault(f => f.Id == current.Id);
                if (p != null && (p.Host != current.Host || p.Port != current.Port))
                {
                    var e = new PhoneEntry { Id = current.Id, Name = p.Name, Host = p.Host, Port = p.Port, Pin = current.Pin };
                    Cfg.Remember(e);
                    Connect(e);
                }
            }
            catch { }
        }

        void OnMessage(Dictionary<string, object> m)
        {
            switch (m.S("t"))
            {
                case "hello":
                    phoneName = m.S("name") != "" ? m.S("name") : m.S("model");
                    phoneModel = m.S("model"); androidVer = m.S("android");
                    nameLbl.Text = phoneName;
                    Text = "LinkDesk – " + phoneName;
                    if (current != null && current.Name != phoneName) { current.Name = phoneName; Cfg.Save(); }
                    if (!screen.HasFrame) screen.ClearFrame("Đã kết nối. Đang chờ hình từ điện thoại…");
                    OnStatus(m);
                    break;
                case "status": OnStatus(m); break;
                case "toast": Toast(m.S("msg")); break;
                case "copied":
                    try { lastClipFromPhone = m.S("s"); Clipboard.SetText(m.S("s")); Toast("Đã chép vào clipboard máy tính"); } catch { }
                    break;
                case "notifs":
                    notifs.Clear();
                    if (m.TryGetValue("list", out var l) && l is object[] arr)
                        foreach (var o in arr.OfType<Dictionary<string, object>>()) notifs[o.S("key")] = o;
                    RenderNotifs();
                    break;
                case "notif":
                    if (m.TryGetValue("n", out var n) && n is Dictionary<string, object> nd)
                    {
                        bool isNew = !notifs.ContainsKey(nd.S("key"));
                        notifs[nd.S("key")] = nd;
                        RenderNotifs();
                        if (isNew && Cfg.ShowPhoneNotifications && !nd.B("ongoing") && (!Visible || !ContainsFocus || WindowState == FormWindowState.Minimized))
                        {
                            string title = nd.S("app") + (nd.S("title") != "" ? ": " + nd.S("title") : "");
                            string text = nd.S("text"); if (text.Length > 200) text = text.Substring(0, 200) + "…";
                            balloonIsUpdate = false;
                            tray.ShowBalloonTip(6000, title, text == "" ? " " : text, ToolTipIcon.None);
                        }
                    }
                    break;
                case "notifRemoved": notifs.Remove(m.S("key")); RenderNotifs(); break;
            }
        }

        void OnStatus(Dictionary<string, object> s)
        {
            foreach (var kv in s) LastStatus[kv.Key] = kv.Value;
            notifWarn.Visible = LastStatus.Has("notif") && !LastStatus.B("notif");
            darkBtn.Toggled = LastStatus.B("dark"); darkBtn.Invalidate();
            if (LastStatus.Has("screenOn")) screenOn = LastStatus.B("screenOn");
            if (LastStatus.Has("mirror")) projecting = LastStatus.B("mirror");
            UpdateBanner();
            UpdateStats();
        }

        public string PhoneInfo =>
            client == null ? "Chưa kết nối" :
            phoneName + "\n" + phoneModel + " · Android " + androidVer + "\nĐịa chỉ: " + current?.Host +
            (LastStatus.Has("appVersion") ? "\nApp điện thoại: " + LastStatus.S("appVersion") : "");

        public void Send(params object[] kv) => client?.Send(kv);

        void UpdateBanner()
        {
            if (client == null || !client.IsOpen) { screen.Banner = null; return; }
            if (!screenOn)
                screen.Banner = "Màn hình điện thoại đang tắt.\n\nBấm vào đây để bật màn hình.\n\n(Mẹo: dùng nút \"Làm tối màn hình\" thay vì tắt hẳn, để điều khiển không bị gián đoạn.)";
            else if (!projecting)
                screen.Banner = "Điện thoại đã dừng chia sẻ hình (thường do khoá máy).\n\nKết nối vẫn được giữ, thông báo vẫn nhận được.\nHãy mở khoá điện thoại, vào app LinkDesk và bấm \"Chia sẻ lại màn hình\".";
            else
                screen.Banner = null;
        }

        long lastBytes;
        int statusTick;
        void UpdateStats()
        {
            var c = client;
            var parts = new List<string> { "● " + connText };
            if (c != null && c.IsOpen)
            {
                if (LastStatus.Has("battery")) parts.Add("Pin " + LastStatus.L("battery") + "%" + (LastStatus.B("charging") ? " ⚡" : ""));
                if (LastStatus.Has("control") && !LastStatus.B("control")) parts.Add("chỉ xem (chưa bật Trợ năng)");
                long b = c.BytesReceived;
                int f = c.FramesReceived; c.FramesReceived = 0;
                parts.Add(f + "/" + LastStatus.L("fps", 30) + " khung/s · " + ((b - lastBytes) * 8 / 1000) + " kbps");
                lastBytes = b;
                if (++statusTick % 15 == 0) c.Send("t", "status");
            }
            SetSub(string.Join("   ·   ", parts), connColor);
        }

        // ------------------------------------------------------------------ thong bao
        int CardWidth() => Math.Max(Theme.S(240), notifFlow.ClientSize.Width - notifFlow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);

        static string TimeStr(long ms)
        {
            var d = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            return d.Date == DateTime.Today ? d.ToString("HH:mm") : d.ToString("d/M HH:mm");
        }

        void RenderNotifs()
        {
            foreach (var c in notifFlow.Controls.OfType<Card>().ToList())
            {
                foreach (var tb in c.Controls.OfType<FlowLayoutPanel>().SelectMany(f => f.Controls.OfType<TextBox>()))
                    if (tb.Tag is string k) { if (tb.Text != "") drafts[k] = tb.Text; else drafts.Remove(k); }
                notifFlow.Controls.Remove(c);
                c.Dispose();
            }
            notifFlow.SuspendLayout();
            var list = notifs.Values.OrderByDescending(n => n.L("time")).ToList();
            int w = CardWidth();
            foreach (var n in list)
            {
                string key = n.S("key");
                var card = new Card { Padding = Theme.P(14, 12, 14, 12), Margin = Theme.P(0, 0, 0, 10) };
                card.MinimumSize = card.MaximumSize = new Size(w, 0);
                int tw = w - Theme.S(30);
                var head = new Panel { Width = tw, Height = Theme.S(20), BackColor = card.Fill, Margin = new Padding(0) };
                var ico = AppIconBox(n.S("pkg")); ico.BackColor = card.Fill; ico.Location = new Point(0, Theme.S(1));
                var app = new Label { AutoSize = false, Location = new Point(Theme.S(24), 0), Width = tw - Theme.S(90), Height = Theme.S(20), Font = Theme.Small, ForeColor = Theme.Muted, Text = n.S("app"), TextAlign = ContentAlignment.MiddleLeft, BackColor = card.Fill, AutoEllipsis = true };
                var time = new Label { AutoSize = false, Location = new Point(tw - Theme.S(66), 0), Width = Theme.S(66), Height = Theme.S(20), Font = Theme.Small, ForeColor = Theme.Muted, Text = TimeStr(n.L("time")), TextAlign = ContentAlignment.MiddleRight, BackColor = card.Fill };
                head.Controls.Add(ico); head.Controls.Add(app); head.Controls.Add(time);
                card.Controls.Add(head);
                if (n.S("title") != "") { var t = Theme.Lbl(n.S("title"), Theme.UIBold); t.MaximumSize = new Size(tw, 0); t.Margin = Theme.P(0, 2, 0, 0); card.Controls.Add(t); }
                if (n.S("text") != "")
                {
                    string text = n.S("text"); if (text.Length > 600) text = text.Substring(0, 600) + "…";
                    var t = Theme.Lbl(text, Theme.UI, Color.FromArgb(205, 210, 218)); t.MaximumSize = new Size(tw, 0); t.Margin = Theme.P(0, 2, 0, 0); card.Controls.Add(t);
                }
                var acts = new FlowLayoutPanel { AutoSize = true, Margin = Theme.P(0, 8, 0, 0), WrapContents = false };
                if (n.B("canOpen")) { var b = new RoundButton("\uE8A7", "Mở", BtnStyle.Soft) { Margin = Theme.P(0, 0, 6, 0) }; b.Click += (s, e) => client?.Send("t", "notifOpen", "key", key); acts.Controls.Add(b); }
                if (n.B("clearable")) { var b = new RoundButton("\uE711", "Xoá", BtnStyle.Ghost) { Margin = Theme.P(0, 0, 6, 0) }; b.Click += (s, e) => client?.Send("t", "notifDismiss", "key", key); acts.Controls.Add(b); }
                if (acts.Controls.Count > 0) card.Controls.Add(acts);
                if (n.B("canReply"))
                {
                    var r = new FlowLayoutPanel { AutoSize = true, Margin = Theme.P(0, 8, 0, 0), WrapContents = false };
                    var tb = new TextBox { BackColor = Theme.Panel2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.UI, Tag = key, Width = tw - Theme.S(56), Margin = Theme.P(0, 4, 6, 0) };
                    Theme.Cue(tb, "Trả lời…");
                    if (drafts.TryGetValue(key, out var dr)) tb.Text = dr;
                    var send = new RoundButton("\uE724", "", BtnStyle.Primary) { Margin = new Padding(0) };
                    Action go = () =>
                    {
                        if (tb.Text.Trim() == "") return;
                        client?.Send("t", "notifReply", "key", key, "s", tb.Text);
                        drafts.Remove(key); tb.Text = "";
                    };
                    tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; go(); } };
                    send.Click += (s, e) => go();
                    r.Controls.Add(tb); r.Controls.Add(send);
                    card.Controls.Add(r);
                }
                notifFlow.Controls.Add(card);
            }
            notifEmpty.Visible = list.Count == 0;
            notifFlow.ResumeLayout();
            tabs.SetLabel(0, list.Count > 0 ? "Thông báo (" + list.Count + ")" : "Thông báo");
        }

        // ------------------------------------------------------------------ hanh dong
        void Upload(string path)
        {
            if (client == null || !File.Exists(path)) return;
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Theme.P(0, 10, 0, 0), BackColor = Theme.Panel };
            var name = Theme.Lbl(Path.GetFileName(path), Theme.UI); name.MaximumSize = Theme.Sz(320, 0);
            var bar = new ProgressBar { Width = Theme.S(320), Height = Theme.S(6), Maximum = 1000 };
            var info = Theme.Lbl("đang gửi…", Theme.Small, Theme.Muted); info.MaximumSize = Theme.Sz(320, 0);
            row.Controls.Add(name); row.Controls.Add(bar); row.Controls.Add(info);
            uploadList.Controls.Add(row);
            uploadList.Controls.SetChildIndex(row, 0);
            var prog = new Progress<double>(v => bar.Value = Math.Max(0, Math.Min(1000, (int)(v * 1000))));
            var c = client;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    string saved = await c.Upload(path, prog);
                    BeginInvoke(new Action(() => { bar.Value = 1000; info.Text = "✓ đã lưu " + saved; Toast("Đã gửi " + Path.GetFileName(path)); }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() => { info.ForeColor = Theme.Red; info.Text = "✗ lỗi: " + ex.Message; }));
                }
            });
        }

        void Snapshot()
        {
            using (var b = screen.Snapshot())
            {
                if (b == null) return;
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LinkDesk");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "dien-thoai-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                b.Save(file, ImageFormat.Png);
                try { Clipboard.SetImage(b); } catch { }
                Toast("Đã lưu " + file + " và chép vào clipboard");
            }
        }

        bool fullscreen;
        FormWindowState prevState;
        bool prevLeft;
        void ToggleFullscreen()
        {
            fullscreen = !fullscreen;
            if (fullscreen)
            {
                prevState = WindowState; prevLeft = left.Visible;
                FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Normal; WindowState = FormWindowState.Maximized;
                left.Visible = false; header.Visible = false;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable; WindowState = prevState;
                left.Visible = prevLeft; header.Visible = true;
            }
        }

        public void BankMode()
        {
            if (client == null) return;
            if (MessageBox.Show(this, "Ngắt kết nối, dừng chia sẻ màn hình và tắt Trợ năng của LinkDesk trên điện thoại?", "Chế độ ngân hàng",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            client.Send("t", "bankMode");
        }

        void OpenSettings()
        {
            using (var f = new SettingsForm(this)) f.ShowDialog(this);
            screen.Focus();
        }

        // ------------------------------------------------------------------ cap nhat
        bool balloonIsUpdate;

        async System.Threading.Tasks.Task AutoCheckUpdate()
        {
            if (!Cfg.AutoUpdate) return;
            var i = await Updater.Check();
            if (i == null) return;
            bool firstTime = pendingUpdate == null;
            pendingUpdate = i;
            updateBtn.SetText("Cập nhật " + i.Version.ToString(3));
            updateBtn.Visible = true;
            header.PerformLayout();
            headerRight.Location = new Point(header.Width - headerRight.Width - Theme.S(14), (header.Height - headerRight.Height) / 2);
            if (firstTime && Cfg.SkippedVersion != i.Version.ToString())
            {
                balloonIsUpdate = true;
                tray.ShowBalloonTip(8000, "Có bản LinkDesk mới " + i.Version.ToString(3), "Bấm để cập nhật." + (string.IsNullOrEmpty(i.Notes) ? "" : "\n" + i.Notes), ToolTipIcon.Info);
            }
        }

        public async void OfferUpdate(Updater.Info i, bool ask)
        {
            if (i == null) return;
            string phone = !string.IsNullOrEmpty(i.AndroidVersion) && LastStatus.S("appVersion") != "" && LastStatus.S("appVersion") != i.AndroidVersion
                ? "\n\nĐiện thoại cũng có bản mới (" + i.AndroidVersion + "): mở app LinkDesk trên điện thoại để cập nhật." : "";
            if (ask && MessageBox.Show(this, "Cập nhật LinkDesk lên bản " + i.Version.ToString(3) + "?" +
                    (string.IsNullOrEmpty(i.Notes) ? "" : "\n\n" + i.Notes) + phone,
                    "Cập nhật LinkDesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                Cfg.SkippedVersion = i.Version.ToString(); Cfg.Save();
                return;
            }
            updateBtn.Enabled = false;
            updateBtn.SetText("Đang tải… 0%");
            try
            {
                await Updater.Download(i, new Progress<double>(v => updateBtn.SetText("Đang tải… " + (int)(v * 100) + "%")));
                updateBtn.SetText("Đang cài…");
                // Ban moi se cho ban nay thoat roi tu mo lai
                ExitApp();
            }
            catch (Exception ex)
            {
                updateBtn.Enabled = true;
                updateBtn.SetText("Cập nhật " + i.Version.ToString(3));
                MessageBox.Show(this, "Không tải được bản cập nhật: " + ex.Message + "\n\nBạn có thể tải tay tại:\n" + Updater.ReleasesPage, "LinkDesk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11) { ToggleFullscreen(); return true; }
            if (keyData == Keys.Escape && fullscreen && !screen.Focused) { ToggleFullscreen(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------------------ khay he thong
        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !IsHandleCreated) { CreateHandle(); value = false; }
            else if (startHidden) { value = false; }
            base.SetVisibleCore(value);
        }

        public void ShowFromTray()
        {
            startHidden = false;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            if (client == null && Cfg.Last == null) ShowConnect(null);
        }

        public void ExitApp()
        {
            allowExit = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason == CloseReason.UserClosing && Cfg.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                if (!Cfg.TrayHintShown)
                {
                    balloonIsUpdate = false;
                    tray.ShowBalloonTip(5000, "LinkDesk vẫn đang chạy", "Nhấp đúp biểu tượng ở khay để mở lại. Nhấp chuột phải → Thoát để tắt hẳn.", ToolTipIcon.Info);
                    Cfg.TrayHintShown = true; Cfg.Save();
                }
                return;
            }
            if (WindowState == FormWindowState.Normal && !fullscreen) { Cfg.WindowWidth = (int)(ClientSize.Width / Theme.Scale); Cfg.WindowHeight = (int)(ClientSize.Height / Theme.Scale); }
            Cfg.Save();
            client?.Dispose();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosing(e);
        }
    }
}
