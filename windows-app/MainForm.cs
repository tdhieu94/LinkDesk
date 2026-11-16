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
        readonly Panel side = new Panel { Dock = DockStyle.Left, BackColor = Theme.Panel };
        readonly Panel main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        readonly Panel tabBar = new Panel { Dock = DockStyle.Top, BackColor = Theme.Bg };
        readonly TabStrip tabs = new TabStrip { BackColor = Theme.Bg };
        readonly Panel pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        readonly Panel center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        readonly ScreenView screen = new ScreenView { Dock = DockStyle.Fill };
        readonly Panel barHost = new Panel { Dock = DockStyle.Right, BackColor = Theme.Bg };
        readonly Card bar = new Card { FlowDirection = FlowDirection.TopDown, Fill = Theme.Panel, Radius = 16 };
        readonly PhoneThumb thumb = new PhoneThumb { BackColor = Theme.Panel };
        readonly Label nameLbl = new Label { AutoSize = false, AutoEllipsis = true, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Theme.Text, BackColor = Theme.Panel };
        readonly Label modelLbl = new Label { AutoSize = false, AutoEllipsis = true, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Panel };
        readonly Chips subLbl = new Chips { BackColor = Theme.Panel };
        readonly Chips connChip = new Chips { BackColor = Theme.Panel };
        readonly FlowLayoutPanel headerRight = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Bg };
        RoundButton awakeTile, clearAllBtn;
        RoundButton updateBtn, darkBtn;
        readonly Pill toastLabel = new Pill { Visible = false };
        readonly Timer toastTimer = new Timer { Interval = 4000 };
        readonly Timer statsTimer = new Timer { Interval = 1000 };
        readonly Timer updateTimer = new Timer { Interval = 6 * 60 * 60 * 1000 };
        readonly NotifyIcon tray = new NotifyIcon();

        // --- bang trai
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

        static FlowLayoutPanel NewPage() => Theme.Scroll(new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            BackColor = Theme.Bg, Padding = Theme.P(16, 4, 8, 16), Visible = false
        });

        public MainForm(bool minimized)
        {
            startHidden = minimized;
            Text = "LinkDesk";
            Icon = AppIcon;
            BackColor = Theme.Bg;
            Theme.StyleWindow(this, Theme.Panel);
            ForeColor = Theme.Text;
            Font = Theme.UI;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = Theme.Sz(760, 560);
            ClientSize = new Size(Theme.S(Cfg.WindowWidth > 0 ? Cfg.WindowWidth : 1220), Theme.S(Cfg.WindowHeight > 0 ? Cfg.WindowHeight : 880));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;

            side.Width = Theme.S(360);
            tabBar.Height = Theme.S(58);
            barHost.Width = Theme.S(80);

            center.Controls.Add(screen);
            center.Controls.Add(barHost);
            pages.Controls.Add(center);
            main.Controls.Add(pages);
            main.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Line });
            main.Controls.Add(tabBar);
            Controls.Add(main);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Line });
            Controls.Add(side);

            BuildHeader();
            BuildBar();
            BuildLeft();

            screen.SendCmd = o => client?.Send(o);
            screen.BannerClick = () => { if (!screenOn) Key("wake"); };
            screen.Controls.Add(toastLabel);
            screen.Resize += (s, e) => PlaceToast();


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
                else if (lastReceived != null) { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + lastReceived + "\""); } catch { } lastReceived = null; }
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
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { ShowPage(4); foreach (var f in files) Upload(f); }
        }

        // ------------------------------------------------------------------ dau trang
        void BuildHeader()
        {
            // --- cot trai: dien thoai
            var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(150), BackColor = Theme.Panel };
            thumb.Location = new Point(Theme.S(22), Theme.S(20));
            int x = thumb.Right + Theme.S(16), w = Theme.S(360) - x - Theme.S(16);
            nameLbl.SetBounds(x, Theme.S(22), w, Theme.S(26));
            modelLbl.SetBounds(x, Theme.S(48), w, Theme.S(18));
            subLbl.Location = new Point(x, Theme.S(72));
            connChip.Location = new Point(x, Theme.S(102));
            connChip.Height = Theme.S(30);
            var recon = new RoundButton("\uE72C", "", BtnStyle.Ghost) { Size = Theme.Sz(30, 30), Radius = 8 };
            new ToolTip().SetToolTip(recon, "Kết nối lại / chọn điện thoại khác");
            recon.Click += (s, e) => { if (client != null && client.IsOpen) ShowConnect(null); else if (current != null) Connect(current); else ShowConnect(null); };
            connChip.SizeChanged += (s, e) => recon.Location = new Point(connChip.Right + Theme.S(4), connChip.Top);
            nameLbl.Text = "LinkDesk";
            modelLbl.Text = "Chưa kết nối điện thoại";
            subLbl.Set();
            connChip.Set(("●", "Chưa kết nối", Theme.Muted));
            top.Controls.Add(thumb); top.Controls.Add(nameLbl); top.Controls.Add(modelLbl); top.Controls.Add(subLbl); top.Controls.Add(connChip); top.Controls.Add(recon);

            // --- o nut nhanh
            var tiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(56), WrapContents = false, Padding = Theme.P(22, 6, 16, 10), BackColor = Theme.Panel };
            RoundButton Tile(string glyph, string tip, Action a)
            {
                var b = new RoundButton(glyph, "", BtnStyle.Soft) { Size = Theme.Sz(72, 38), Radius = 8, Margin = Theme.P(0, 0, 8, 0) };
                new ToolTip().SetToolTip(b, tip);
                b.Click += (s, e) => a();
                tiles.Controls.Add(b);
                return b;
            }
            darkBtn = Tile("\uE708", "Làm tối màn hình điện thoại (máy tính vẫn thấy hình)", () => client?.Send("t", "dark", "on", !LastStatus.B("dark")));
            awakeTile = Tile("\uE706", "Không cho điện thoại tự tắt màn hình", () => client?.Send("t", "keepAwake", "on", !(LastStatus.Has("keepAwake") ? LastStatus.B("keepAwake") : true)));
            Tile("\uE72E", "Khoá màn hình điện thoại", () => Key("lock"));
            Tile("\uE722", "Chụp màn hình (lưu vào Ảnh\\LinkDesk)", Snapshot);

            var sep = new Panel { Dock = DockStyle.Top, Height = Theme.S(17), BackColor = Theme.Panel };
            sep.Controls.Add(new Panel { BackColor = Theme.Line, Bounds = new Rectangle(Theme.S(22), Theme.S(8), Theme.S(360 - 44), 1) });

            // --- tieu de thong bao
            var nh = new Panel { Dock = DockStyle.Top, Height = Theme.S(48), BackColor = Theme.Panel };
            var nt = new Label { Text = "Thông báo", Font = new Font("Segoe UI Semibold", 14f), ForeColor = Theme.Text, AutoSize = true, BackColor = Theme.Panel, Location = new Point(Theme.S(20), Theme.S(8)) };
            clearAllBtn = new RoundButton("", "Xoá tất cả", BtnStyle.Ghost) { Radius = 8 };
            clearAllBtn.Font = Theme.Small; clearAllBtn.AutoFit();
            clearAllBtn.Location = new Point(Theme.S(360) - clearAllBtn.Width - Theme.S(14), Theme.S(8));
            clearAllBtn.Click += (s, e) => { foreach (var n in notifs.Values.Where(n => n.B("clearable")).ToList()) client?.Send("t", "notifDismiss", "key", n.S("key")); };
            nh.Controls.Add(nt); nh.Controls.Add(clearAllBtn);

            pages.Controls.Add(smsPage); pages.Controls.Add(callsPage);
            BuildSms(); BuildCalls();
            notifFlow.Visible = true;
            notifFlow.BackColor = Theme.Panel;
            side.Controls.Add(notifFlow);
            side.Controls.Add(nh);
            side.Controls.Add(BuildMediaCard());
            side.Controls.Add(sep);
            side.Controls.Add(tiles);
            side.Controls.Add(top);

            // --- thanh tab ben phai
            tabs.Add("\uE8EA", "Màn hình");
            tabs.Add("\uE8BD", "Tin nhắn");
            tabs.Add("\uEB9F", "Ảnh");
            tabs.Add("\uE717", "Cuộc gọi");
            tabs.Add("\uE8B7", "Tệp");
            tabs.Add("\uE765", "Gõ chữ");
            tabs.Location = new Point(Theme.S(16), Theme.S(8));
            tabs.Changed += i =>
            {
                center.Visible = i == 0; smsPage.Visible = i == 1; photosFlow.Visible = i == 2;
                callsPage.Visible = i == 3; filesFlow.Visible = i == 4; typeFlow.Visible = i == 5;
                if (i == 1) OnSmsTab();
                if (i == 2) OnPhotosTab();
                if (i == 3) OnCallsTab();
                if (i == 4) OnFilesTab();
                if (i == 0) screen.Focus();
            };
            tabBar.Controls.Add(tabs);

            var gear = new RoundButton("\uE713", "", BtnStyle.Ghost);
            new ToolTip().SetToolTip(gear, "Cài đặt");
            gear.Click += (s, e) => OpenSettings();
            pinBtn = new RoundButton("\uE718", "", BtnStyle.Ghost);
            new ToolTip().SetToolTip(pinBtn, "Luôn nằm trên các cửa sổ khác");
            pinBtn.Click += (s, e) => { Cfg.TopMost = !Cfg.TopMost; Cfg.Save(); ApplyTopMost(); };
            updateBtn = new RoundButton("\uE896", "Cập nhật", BtnStyle.Primary) { Visible = false };
            updateBtn.Click += (s, e) => OfferUpdate(pendingUpdate, true);
            headerRight.Controls.Add(gear);
            headerRight.Controls.Add(pinBtn);
            headerRight.Controls.Add(updateBtn);
            tabBar.Controls.Add(headerRight);
            EventHandler place = (s, e) => { headerRight.Location = new Point(tabBar.Width - headerRight.Width - Theme.S(14), (tabBar.Height - headerRight.Height) / 2); headerRight.BringToFront(); };
            tabBar.Resize += place; headerRight.SizeChanged += place; tabBar.Layout += (s, e) => place(s, e);
        }

        /// <summary>Mo tab ben phai (0 man hinh, 1 tin nhan, 2 anh, 3 cuoc goi, 4 tep, 5 go chu).</summary>
        void ShowPage(int i) => tabs.Select(i);

        static void MakeRound(Control c, int r)
        {
            using (var p = Theme.Round(new Rectangle(0, 0, c.Width, c.Height), r)) c.Region = new Region(p);
        }

        // ------------------------------------------------------------------ thanh dieu khien duoi man hinh
        RoundButton BarBtn(string glyph, string tip, Action click)
        {
            var b = new RoundButton(glyph, "", BtnStyle.Ghost) { Margin = Theme.P(0, 2, 0, 2) };
            b.Size = Theme.Sz(42, 42);
            new ToolTip().SetToolTip(b, tip);
            b.Click += (s, e) => { click(); screen.Focus(); };
            bar.Controls.Add(b);
            return b;
        }

        void BarSep() => bar.Controls.Add(new Panel { Width = Theme.S(26), Height = 1, BackColor = Theme.Line, Margin = Theme.P(8, 6, 8, 6) });

        void Key(string k) => client?.Send("t", "key", "k", k);

        void BuildBar()
        {
            bar.Padding = Theme.P(6, 8, 6, 8);
            bar.Margin = new Padding(0);
            BarBtn("\uE72B", "Quay lại (Esc / chuột phải)", () => Key("back"));
            BarBtn("\uE80F", "Màn hình chính (chuột giữa)", () => Key("home"));
            BarBtn("\uE8A9", "Ứng dụng gần đây", () => Key("recents"));
            BarSep();
            BarBtn("\uEA8F", "Kéo thanh thông báo", () => Key("notifications"));
            BarBtn("\uE9E9", "Cài đặt nhanh", () => Key("quick"));
            BarSep();
            BarBtn("\uE706", "Bật sáng màn hình", () => Key("wake"));
            BarBtn("\uE72E", "Khoá màn hình", () => Key("lock"));
            BuildAudioButton();
            BarSep();
            BarBtn("\uE722", "Chụp màn hình (lưu vào Ảnh\\LinkDesk và chép vào clipboard)", Snapshot);
            BarBtn("\uE740", "Toàn màn hình (F11)", ToggleFullscreen);
            var more = new ContextMenuStrip();
            more.Items.Add("Tăng âm lượng", null, (s, e) => Key("volup"));
            more.Items.Add("Giảm âm lượng", null, (s, e) => Key("voldown"));
            more.Items.Add("Tắt/bật tiếng", null, (s, e) => Key("mute"));
            more.Items.Add(new ToolStripSeparator());
            more.Items.Add("Menu nguồn", null, (s, e) => Key("power"));
            more.Items.Add("Chia đôi màn hình", null, (s, e) => Key("split"));
            RoundButton moreBtn = null;
            moreBtn = BarBtn("\uE712", "Thêm: âm lượng, menu nguồn…", () => more.Show(moreBtn, new Point(moreBtn.Width, 0)));
            barHost.Controls.Add(bar);
            barHost.Resize += (s, e) => bar.Location = new Point(Math.Max(0, (barHost.Width - bar.Width) / 2 - Theme.S(8)), Math.Max(0, (barHost.Height - bar.Height) / 2));
            bar.SizeChanged += (s, e) => bar.Location = new Point(Math.Max(0, (barHost.Width - bar.Width) / 2 - Theme.S(8)), Math.Max(0, (barHost.Height - bar.Height) / 2));
        }

        // ------------------------------------------------------------------ bang trai
        void BuildLeft()
        {
            foreach (var pg in new[] { photosFlow, filesFlow, typeFlow })
            {
                pages.Controls.Add(pg);
                pg.Padding = Theme.P(28, 20, 28, 24);
                var page = pg;
                page.Resize += (s, e) => { int pad = Math.Max(Theme.S(28), (page.ClientSize.Width - Theme.S(560)) / 2); if (page != photosFlow && page.Padding.Left != pad) page.Padding = new Padding(pad, Theme.S(20), Theme.S(28), Theme.S(24)); };
            }
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
            BuildPhoneFiles();

            // --- go chu
            var t = PageCard(typeFlow, "\uE765", "Gõ chữ vào điện thoại");
            t.Controls.Add(Wrap("Bấm vào ô nhập trên điện thoại trước, rồi gõ ở đây. Enter để gửi, Shift+Enter để xuống dòng. Bạn cũng có thể bấm lên màn hình điện thoại rồi gõ thẳng bằng bàn phím."));
            var tbi = new RoundInput(null, true) { Width = Theme.S(486), Height = Theme.S(120), Margin = Theme.P(0, 10, 0, 0) };
            var tb = tbi.Box;
            var send = new RoundButton("\uE724", "Gửi sang điện thoại", BtnStyle.Primary) { Margin = Theme.P(0, 8, 0, 0) };
            Action go = () => { if (tb.Text.Length > 0) { client?.Send("t", "text", "s", tb.Text); tb.Text = ""; } };
            tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; go(); } };
            send.Click += (s, e) => go();
            t.Controls.Add(tbi);
            t.Controls.Add(send);

            var c = PageCard(typeFlow, "\uE77F", "Bộ nhớ tạm");
            c.Controls.Add(Wrap("Ctrl+V trên màn hình điện thoại: dán chữ từ máy tính.\nCtrl+C: chép chữ đang bôi đen trên điện thoại về máy tính."));
            var clipSend = new RoundButton("\uE8C8", "Gửi clipboard sang điện thoại", BtnStyle.Soft) { Margin = Theme.P(0, 10, 0, 0) };
            clipSend.Click += (s, e) => { try { if (Clipboard.ContainsText()) client?.Send("t", "clip", "s", Clipboard.GetText()); } catch { } };
            c.Controls.Add(clipSend);

            ShowPage(0);
        }

        Label Wrap(string text)
        {
            var l = Theme.Lbl(text, Theme.UI, Theme.Muted);
            l.MaximumSize = Theme.Sz(480, 0);
            return l;
        }

        Card PageCard(FlowLayoutPanel page, string glyph, string title)
        {
            var c = new Card();
            c.MinimumSize = c.MaximumSize = Theme.Sz(520, 0);
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
            StopPlayer(); playerFailed = false;
            current = e;
            phoneName = e.Name;
            nameLbl.Text = string.IsNullOrEmpty(e.Name) ? e.Host : e.Name;
            notifs.Clear(); RenderNotifs();
            photosLoaded = false; photoGen++; iconPending.Clear(); phoneFilesLoaded = false; filesGen++;
            smsLoaded = false; smsGen++; convGen++; convId = null; convAddr = null; callsLoaded = false;
            ClearRows(threadList); ClearRows(msgList); ClearRows(callList); convPanel.Visible = false; convEmpty.Visible = true;
            if (mediaCard != null) mediaCard.Parent.Visible = false;
            foreach (Control x in photoGrid.Controls.Cast<Control>().ToList()) { photoGrid.Controls.Remove(x); x.Dispose(); }
            photoInfo.Text = "";
            screen.Banner = null;
            screen.ClearFrame("Đang kết nối tới " + nameLbl.Text + "…");
            var c = new PhoneClient(e.Host, e.Port, e.Pin, e.CertSha);
            client = c;
            c.Frame += (bmp, w, h) =>
            {
                if (client != c || IsDisposed) { bmp.Dispose(); return; }
                try { BeginInvoke(new Action(() => { if (client == c) screen.SetFrame(bmp, w, h); else bmp.Dispose(); })); } catch { bmp.Dispose(); }
            };
            c.Audio += d => OnAudio(c, d);
            c.Video += d => OnVideoPacket(c, d);
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
                case ConnState.Connected:
                    connText = "Đã kết nối"; connColor = Theme.Green; tray.Text = "LinkDesk – đã kết nối";
                    // Dien thoai vua len ban co ma hoa: ghim chung chi o lan dau
                    if (client != null && client.Secure && string.IsNullOrEmpty(current?.CertSha) && !string.IsNullOrEmpty(client.ObservedSha))
                    { current.CertSha = client.ObservedSha; Cfg.Remember(current); }
                    break;
                case ConnState.BadCert:
                    connText = "Mã bảo mật thay đổi"; connColor = Theme.Red;
                    screen.ClearFrame("Mã bảo mật của điện thoại đã thay đổi.");
                    client?.Dispose(); client = null;
                    if (Visible) ShowConnect(msg);
                    break;
                case ConnState.Locked: connText = "Bị khoá tạm do nhập sai PIN"; connColor = Theme.Red; break;
                case ConnState.Disconnected:
                    StopPlayer();
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
                    var e = new PhoneEntry { Id = current.Id, Name = p.Name, Host = p.Host, Port = p.Port, Pin = current.Pin, CertSha = current.CertSha };
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
                    modelLbl.Text = phoneModel + (androidVer != "" ? " · Android " + androidVer : "");
                    LoadWallpaper();
                    if (Cfg.Audio) client?.Send("t", "audio", "on", true);
                    if (Cfg.H264 && !h264Failed) RequestCodec();
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
                            balloonIsUpdate = false; lastReceived = null;
                            tray.ShowBalloonTip(6000, title, text == "" ? " " : text, ToolTipIcon.None);
                        }
                    }
                    break;
                case "notifRemoved": notifs.Remove(m.S("key")); RenderNotifs(); break;
                case "media": OnMedia(m); break;
                case "incoming": OnIncoming(m); break;
                case "smsChanged": OnSmsChanged(); break;
                case "callsChanged": if (callsLoaded) LoadCalls(); break;
            }
        }

        void OnStatus(Dictionary<string, object> s)
        {
            foreach (var kv in s) LastStatus[kv.Key] = kv.Value;
            notifWarn.Visible = LastStatus.Has("notif") && !LastStatus.B("notif");
            darkBtn.Toggled = LastStatus.B("dark"); darkBtn.Invalidate();
            awakeTile.Toggled = !LastStatus.Has("keepAwake") || LastStatus.B("keepAwake"); awakeTile.Invalidate();
            if (LastStatus.Has("screenOn")) screenOn = LastStatus.B("screenOn");
            if (LastStatus.Has("mirror")) projecting = LastStatus.B("mirror");
            UpdateBanner();
            UpdateStats();
        }

        public string PhoneInfo =>
            client == null ? "Chưa kết nối" :
            phoneName + "\n" + phoneModel + " · Android " + androidVer + "\nĐịa chỉ: " + current?.Host +
            (LastStatus.Has("appVersion") ? "\nApp điện thoại: " + LastStatus.S("appVersion") : "") +
            (client.Secure ? "\nKết nối mã hoá · mã bảo mật " + Tls.ShortCode(client.ObservedSha ?? current?.CertSha) : "\nKết nối chưa mã hoá (cập nhật app điện thoại để bật)");

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
            connChip.Set(("●", connText, connColor));
            if (c != null && c.IsOpen)
            {
                var chips = new List<(string, string, Color)>();
                if (LastStatus.Has("battery"))
                {
                    long bat = LastStatus.L("battery");
                    bool chg = LastStatus.B("charging");
                    string g = ((char)((chg ? 0xEBA0 : 0xE850) + (int)Math.Max(0, Math.Min(10, bat / 10)))).ToString();
                    chips.Add((g, bat + "%", bat <= 15 && !chg ? Theme.Red : Theme.Text2));
                }
                long b = c.BytesReceived;
                int f = c.FramesReceived; c.FramesReceived = 0;
                chips.Add(("\uE714", f + "/" + LastStatus.L("fps", 30) + " khung/s", Theme.Text2));
                if (!c.Secure) chips.Add(("\uE7BA", "Chưa mã hoá", Theme.Orange));
                if (LastStatus.Has("control") && !LastStatus.B("control")) chips.Add(("\uE7B3", "Chỉ xem", Theme.Orange));
                subLbl.Set(chips.ToArray());
                lastBytes = b;
                if (++statusTick % 15 == 0) c.Send("t", "status");
            }
            else subLbl.Set();
        }

        /// <summary>Anh nho = hinh nen dien thoai (lay 1 lan khi ket noi), giong Phone Link.</summary>
        async void LoadWallpaper()
        {
            var c = client;
            if (c == null) return;
            try
            {
                var b = await c.GetBytes("/wallpaper");
                if (client != c) return;
                using (var ms = new MemoryStream(b)) thumb.Image = new Bitmap(Image.FromStream(ms));
            }
            catch { }
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
                foreach (var tb in AllControls(c).OfType<TextBox>())
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
                var card = new Card { Fill = Theme.Bg, Padding = Theme.P(14, 12, 14, 12), Margin = Theme.P(0, 0, 0, 10) };
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
                    var t = Theme.Lbl(text, Theme.UI, Theme.Text2); t.MaximumSize = new Size(tw, 0); t.Margin = Theme.P(0, 2, 0, 0); card.Controls.Add(t);
                }
                var acts = new FlowLayoutPanel { AutoSize = true, Margin = Theme.P(0, 8, 0, 0), WrapContents = false };
                if (n.B("canOpen")) { var b = new RoundButton("\uE8A7", "Mở", BtnStyle.Soft) { Margin = Theme.P(0, 0, 6, 0) }; b.Click += (s, e) => client?.Send("t", "notifOpen", "key", key); acts.Controls.Add(b); }
                if (n.B("clearable")) { var b = new RoundButton("\uE711", "Xoá", BtnStyle.Ghost) { Margin = Theme.P(0, 0, 6, 0) }; b.Click += (s, e) => client?.Send("t", "notifDismiss", "key", key); acts.Controls.Add(b); }
                if (acts.Controls.Count > 0) card.Controls.Add(acts);
                if (n.B("canReply"))
                {
                    var r = new FlowLayoutPanel { AutoSize = true, Margin = Theme.P(0, 8, 0, 0), WrapContents = false };
                    var tbi = new RoundInput("Trả lời…") { Width = tw - Theme.S(48), Margin = Theme.P(0, 0, 6, 0) };
                    var tb = tbi.Box; tb.Tag = key;
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
                    r.Controls.Add(tbi); r.Controls.Add(send);
                    card.Controls.Add(r);
                }
                notifFlow.Controls.Add(card);
            }
            notifEmpty.Visible = list.Count == 0;
            notifFlow.ResumeLayout();
            tabs.Invalidate(); clearAllBtn.Visible = list.Any(n => n.B("clearable"));
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
                prevState = WindowState; prevLeft = side.Visible;
                FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Normal; WindowState = FormWindowState.Maximized;
                side.Visible = false; tabBar.Visible = false;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable; WindowState = prevState;
                side.Visible = prevLeft; tabBar.Visible = true;
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
            tabBar.PerformLayout();
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

        /// <summary>Thoat va mo lai (vd. sau khi doi giao dien sang/toi).</summary>
        public void Restart()
        {
            try
            {
                string args = "--restart " + System.Diagnostics.Process.GetCurrentProcess().Id;
                if (Environment.GetCommandLineArgs().Contains("--portable")) args += " --portable";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = false });
            }
            catch { }
            ExitApp();
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
            StopPlayer();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosing(e);
        }
    }
}
