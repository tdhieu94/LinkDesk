using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LinkDesk
{
    // Tab "Tin nhan" va "Cuoc goi" (giong Phone Link).
    public partial class MainForm
    {
        readonly Panel smsPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
        readonly Panel callsPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };

        // ------------------------------------------------------------------ tin nhan
        readonly FlowLayoutPanel threadList = Theme.Scroll(new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = Theme.P(10, 0, 4, 10) });
        readonly FlowLayoutPanel msgList = Theme.Scroll(new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = Theme.P(16, 12, 8, 12) });
        readonly Label smsInfo = new Label { AutoSize = true, Font = Theme.UI, ForeColor = Theme.Muted, Margin = Theme.P(8, 10, 0, 0) };
        readonly Label convName = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Theme.Text, BackColor = Theme.Bg };
        readonly Label convNumber = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Bg };
        RoundInput smsBox;
        RoundButton smsSend;
        Panel convPanel;
        Label convEmpty;
        string convId, convAddr;
        bool smsLoaded;
        int smsGen, convGen;

        void BuildSms()
        {
            var listPane = new Panel { Dock = DockStyle.Left, Width = Theme.S(340), BackColor = Theme.Bg };
            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(60), BackColor = Theme.Bg };
            head.Controls.Add(new Label { Text = "Tin nhắn", Font = new Font("Segoe UI Semibold", 14f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(Theme.S(20), Theme.S(16)), BackColor = Theme.Bg });
            var nw = new RoundButton("", "Tin nhắn mới", BtnStyle.Soft) { Radius = 8 };
            nw.Location = new Point(Theme.S(340) - nw.Width - Theme.S(14), Theme.S(14));
            nw.Click += (s, e) => NewMessage();
            head.Controls.Add(nw);
            threadList.Controls.Add(smsInfo);
            listPane.Controls.Add(threadList);
            listPane.Controls.Add(head);

            convPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var ch = new Panel { Dock = DockStyle.Top, Height = Theme.S(60), BackColor = Theme.Bg };
            convName.Location = new Point(Theme.S(20), Theme.S(10));
            convNumber.Location = new Point(Theme.S(21), Theme.S(36));
            var callBtn = new RoundButton("", "", BtnStyle.Ghost);
            new ToolTip().SetToolTip(callBtn, "Gọi số này");
            callBtn.Click += (s, e) => { if (!string.IsNullOrEmpty(convAddr)) PlaceCall(convAddr); };
            ch.Resize += (s, e) => callBtn.Location = new Point(ch.Width - callBtn.Width - Theme.S(16), Theme.S(12));
            ch.Controls.Add(convName); ch.Controls.Add(convNumber); ch.Controls.Add(callBtn);
            var comp = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(64), BackColor = Theme.Bg, Padding = Theme.P(16, 10, 16, 12) };
            smsBox = new RoundInput("Nhập tin nhắn SMS…");
            smsSend = new RoundButton("", "", BtnStyle.Primary);
            comp.Resize += (s, e) =>
            {
                smsSend.Location = new Point(comp.Width - smsSend.Width - Theme.S(16), (comp.Height - smsSend.Height) / 2);
                smsBox.SetBounds(Theme.S(16), (comp.Height - smsBox.Height) / 2, smsSend.Left - Theme.S(24), smsBox.Height);
            };
            smsBox.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendSms(); } };
            smsSend.Click += (s, e) => SendSms();
            comp.Controls.Add(smsBox); comp.Controls.Add(smsSend);
            convPanel.Controls.Add(msgList);
            convPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Line });
            convPanel.Controls.Add(ch);
            convPanel.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Line });
            convPanel.Controls.Add(comp);
            convPanel.Visible = false;
            convEmpty = new Label { Dock = DockStyle.Fill, Text = "Chọn một cuộc trò chuyện ở bên trái.", ForeColor = Theme.Muted, Font = Theme.UI, TextAlign = ContentAlignment.MiddleCenter, BackColor = Theme.Bg };

            smsPage.Controls.Add(convPanel);
            smsPage.Controls.Add(convEmpty);
            smsPage.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Line });
            smsPage.Controls.Add(listPane);
            msgList.Resize += (s, e) => { foreach (var b in msgList.Controls.OfType<BubbleRow>()) b.FitWidth(MsgWidth()); };
            threadList.Resize += (s, e) => { foreach (var r in threadList.Controls.OfType<PersonRow>()) r.Width = ListWidth(threadList); };
        }

        static int ListWidth(FlowLayoutPanel f) => Math.Max(Theme.S(200), f.ClientSize.Width - f.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        int MsgWidth() => Math.Max(Theme.S(200), msgList.ClientSize.Width - msgList.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);

        void OnSmsTab() { if (!smsLoaded) LoadThreads(); }

        async void LoadThreads()
        {
            var c = client;
            int gen = ++smsGen;
            if (c == null || !c.IsOpen) { ClearRows(threadList); smsInfo.Text = "Chưa kết nối điện thoại."; return; }
            if (!smsLoaded) smsInfo.Text = "Đang tải…";
            try
            {
                var b = await c.GetBytes("/sms/threads");
                if (gen != smsGen) return;
                var list = (new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(System.Text.Encoding.UTF8.GetString(b)) as object[] ?? new object[0]).OfType<Dictionary<string, object>>().ToList();
                smsLoaded = true;
                ClearRows(threadList);
                smsInfo.Text = list.Count == 0 ? "Chưa có tin nhắn nào." : "";
                smsInfo.Visible = list.Count == 0;
                threadList.SuspendLayout();
                int w = ListWidth(threadList);
                foreach (var t in list)
                {
                    string title = t.S("name") != "" ? t.S("name") : t.S("address");
                    var row = new PersonRow(title, (t.B("out") ? "Bạn: " : "") + t.S("body").Replace("\n", " "), TimeStr(t.L("time")), (int)t.L("unread")) { Width = w, Tag = t.S("id") };
                    row.Selected = t.S("id") == convId;
                    var tt = t;
                    row.Click += (s, e) => OpenThread(tt.S("id"), tt.S("address"), tt.S("name"));
                    threadList.Controls.Add(row);
                }
                threadList.ResumeLayout();
            }
            catch (Exception ex)
            {
                if (gen != smsGen) return;
                ClearRows(threadList);
                smsInfo.Visible = true;
                smsInfo.MaximumSize = new Size(ListWidth(threadList), 0);
                smsInfo.Text = ex.Message;
            }
        }

        static string Thu(DateTime d) => d.DayOfWeek == DayOfWeek.Sunday ? "CN" : "T" + ((int)d.DayOfWeek + 1);

        static void ClearRows(FlowLayoutPanel f)
        {
            foreach (var x in f.Controls.OfType<Control>().Where(x => x is PersonRow || x is BubbleRow || (x is Label l && l.Tag as string == "row")).ToList()) { f.Controls.Remove(x); x.Dispose(); }
        }

        async void OpenThread(string id, string addr, string name)
        {
            convId = id; convAddr = addr;
            foreach (var r in threadList.Controls.OfType<PersonRow>()) { r.Selected = r.Tag as string == id; r.Invalidate(); }
            convPanel.Visible = true; convEmpty.Visible = false;
            convName.Text = name != "" ? name : addr;
            convNumber.Text = name != "" ? addr : "";
            ClearRows(msgList);
            if (id == null) { smsBox.Box.Focus(); return; }
            int gen = ++convGen;
            try
            {
                var b = await client.GetBytes("/sms/thread", "id", id);
                if (gen != convGen) return;
                var o = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(System.Text.Encoding.UTF8.GetString(b)) as Dictionary<string, object>;
                var msgs = (o != null && o.TryGetValue("messages", out var mv) ? mv as object[] : null)?.OfType<Dictionary<string, object>>().ToList() ?? new List<Dictionary<string, object>>();
                ClearRows(msgList);
                msgList.SuspendLayout();
                int w = MsgWidth();
                DateTime lastDay = DateTime.MinValue;
                foreach (var m in msgs)
                {
                    var d = DateTimeOffset.FromUnixTimeMilliseconds(m.L("time")).LocalDateTime;
                    if (d.Date != lastDay)
                    {
                        lastDay = d.Date;
                        msgList.Controls.Add(new Label { Tag = "row", AutoSize = false, Size = new Size(w, Theme.S(30)), Text = d.Date == DateTime.Today ? "Hôm nay" : d.ToString("dd/MM/yyyy"), TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(0) });
                    }
                    var row = new BubbleRow(m.S("body"), d.ToString("HH:mm") + (m.B("failed") ? " · Gửi lỗi" : m.B("pending") ? " · Đang gửi" : ""), m.B("out"));
                    row.FitWidth(w);
                    msgList.Controls.Add(row);
                }
                msgList.ResumeLayout();
                if (msgList.Controls.Count > 0) msgList.ScrollControlIntoView(msgList.Controls[msgList.Controls.Count - 1]);
                smsBox.Box.Focus();
            }
            catch (Exception ex) { Toast("Không tải được tin nhắn: " + ex.Message); }
        }

        void SendSms()
        {
            string t = smsBox.Text.Trim();
            if (t == "" || string.IsNullOrEmpty(convAddr) || client == null) return;
            client.Send("t", "smsSend", "to", convAddr, "s", t);
            smsBox.Text = "";
            var row = new BubbleRow(t, DateTime.Now.ToString("HH:mm") + " · Đang gửi", true);
            row.FitWidth(MsgWidth());
            msgList.Controls.Add(row);
            msgList.ScrollControlIntoView(row);
        }

        void NewMessage()
        {
            string num = Prompt("Tin nhắn mới", "Số điện thoại người nhận:");
            if (string.IsNullOrWhiteSpace(num)) return;
            num = num.Trim();
            var t = threadList.Controls.OfType<PersonRow>().FirstOrDefault(r => r.Title == num);
            OpenThread(null, num, "");
        }

        string Prompt(string title, string label)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.UI, AutoScaleMode = AutoScaleMode.None, ShowInTaskbar = false })
            {
                Theme.StyleWindow(f, Theme.Bg);
                f.ClientSize = Theme.Sz(360, 150);
                var l = new Label { Text = label, AutoSize = true, Location = new Point(Theme.S(20), Theme.S(18)), BackColor = Theme.Bg };
                var box = new RoundInput("Ví dụ 0912345678") { Location = new Point(Theme.S(20), Theme.S(44)), Width = Theme.S(320) };
                var ok = new RoundButton("", "Tiếp tục", BtnStyle.Primary) { Location = new Point(Theme.S(240), Theme.S(100)) };
                ok.AutoFit(Theme.S(100)); ok.Location = new Point(Theme.S(340) - ok.Width, Theme.S(100));
                ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; f.Close(); };
                box.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; f.DialogResult = DialogResult.OK; f.Close(); } };
                f.Controls.Add(l); f.Controls.Add(box); f.Controls.Add(ok);
                f.Shown += (s, e) => box.Box.Focus();
                return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
        }

        void OnSmsChanged()
        {
            if (!smsLoaded) return;
            LoadThreads();
            if (convId != null && smsPage.Visible) OpenThread(convId, convAddr, convName.Text == convAddr ? "" : convName.Text);
        }

        // ------------------------------------------------------------------ cuoc goi
        readonly FlowLayoutPanel callList = Theme.Scroll(new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = Theme.P(16, 0, 8, 12) });
        readonly Label callInfo = new Label { AutoSize = true, Font = Theme.UI, ForeColor = Theme.Muted, Margin = Theme.P(8, 10, 0, 0) };
        readonly Label dialNum = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 20f), ForeColor = Theme.Text, BackColor = Theme.Panel };
        RoundInput callSearch;
        List<Dictionary<string, object>> callData = new List<Dictionary<string, object>>(), contactData;
        bool callsLoaded;
        int callGen;

        void BuildCalls()
        {
            var pad = new Panel { Dock = DockStyle.Right, Width = Theme.S(360), BackColor = Theme.Panel };
            dialNum.SetBounds(Theme.S(20), Theme.S(40), Theme.S(320), Theme.S(50));
            pad.Controls.Add(dialNum);
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "*", "0", "#" };
            string[] subs = { "", "ABC", "DEF", "GHI", "JKL", "MNO", "PQRS", "TUV", "WXYZ", "", "+", "" };
            for (int i = 0; i < 12; i++)
            {
                var k = keys[i];
                var b = new DialKey(k, subs[i]) { Size = Theme.Sz(84, 60) };
                b.Location = new Point(Theme.S(30) + (i % 3) * Theme.S(102), Theme.S(110) + (i / 3) * Theme.S(70));
                b.Click += (s, e) => { dialNum.Text += k; };
                if (k == "0") b.MouseDown += (s, e) => { if (e.Button == MouseButtons.Right) dialNum.Text += "+"; };
                pad.Controls.Add(b);
            }
            var back = new RoundButton("", "", BtnStyle.Ghost) { Location = new Point(Theme.S(296), Theme.S(50)) };
            back.Click += (s, e) => { if (dialNum.Text.Length > 0) dialNum.Text = dialNum.Text.Substring(0, dialNum.Text.Length - 1); };
            pad.Controls.Add(back); back.BringToFront();
            var call = new RoundButton("", "Gọi", BtnStyle.Primary) { Radius = 24 };
            call.AutoFit(Theme.S(140)); call.Height = Theme.S(48);
            call.Location = new Point(Theme.S(30), Theme.S(400));
            call.Click += (s, e) => { if (dialNum.Text.Trim() != "") PlaceCall(dialNum.Text.Trim()); };
            var end = new RoundButton("", "Kết thúc", BtnStyle.Danger) { Radius = 24 };
            end.AutoFit(Theme.S(140)); end.Height = Theme.S(48);
            end.Location = new Point(Theme.S(190), Theme.S(400));
            end.Click += (s, e) => client?.Send("t", "endCall");
            pad.Controls.Add(call); pad.Controls.Add(end);
            var hint = new Label { Text = "Cuộc gọi diễn ra trên điện thoại: nghe và nói bằng điện thoại hoặc tai nghe nối với điện thoại.", AutoSize = false, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Panel, Bounds = new Rectangle(Theme.S(30), Theme.S(462), Theme.S(300), Theme.S(40)) };
            pad.Controls.Add(hint);

            var listPane = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(110), BackColor = Theme.Bg };
            head.Controls.Add(new Label { Text = "Cuộc gọi", Font = new Font("Segoe UI Semibold", 14f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(Theme.S(20), Theme.S(16)), BackColor = Theme.Bg });
            callSearch = new RoundInput("Tìm trong danh bạ") { Location = new Point(Theme.S(20), Theme.S(58)) };
            head.Resize += (s, e) => callSearch.Width = Math.Min(Theme.S(520), head.Width - Theme.S(40));
            callSearch.Box.TextChanged += (s, e) => RenderCalls();
            head.Controls.Add(callSearch);
            callList.Controls.Add(callInfo);
            listPane.Controls.Add(callList);
            listPane.Controls.Add(head);
            callsPage.Controls.Add(listPane);
            callsPage.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Theme.Line });
            callsPage.Controls.Add(pad);
            callList.Resize += (s, e) => { foreach (var r in callList.Controls.OfType<PersonRow>()) r.Width = ListWidth(callList); };
        }

        void PlaceCall(string number)
        {
            client?.Send("t", "call", "number", number);
        }

        void OnCallsTab() { if (!callsLoaded) LoadCalls(); }

        async void LoadCalls()
        {
            var c = client;
            int gen = ++callGen;
            if (c == null || !c.IsOpen) { callInfo.Text = "Chưa kết nối điện thoại."; return; }
            if (!callsLoaded) callInfo.Text = "Đang tải…";
            try
            {
                var b = await c.GetBytes("/calls");
                if (gen != callGen) return;
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                callData = (ser.DeserializeObject(System.Text.Encoding.UTF8.GetString(b)) as object[] ?? new object[0]).OfType<Dictionary<string, object>>().ToList();
                callsLoaded = true;
                try
                {
                    var cb = await c.GetBytes("/contacts");
                    contactData = (ser.DeserializeObject(System.Text.Encoding.UTF8.GetString(cb)) as object[] ?? new object[0]).OfType<Dictionary<string, object>>().ToList();
                }
                catch { contactData = null; }
                RenderCalls();
            }
            catch (Exception ex)
            {
                if (gen != callGen) return;
                ClearRows(callList);
                callInfo.Visible = true;
                callInfo.MaximumSize = new Size(ListWidth(callList), 0);
                callInfo.Text = ex.Message;
            }
        }

        void RenderCalls()
        {
            ClearRows(callList);
            string q = callSearch.Text.Trim().ToLowerInvariant();
            int w = ListWidth(callList);
            callList.SuspendLayout();
            if (q != "")
            {
                var found = (contactData ?? new List<Dictionary<string, object>>()).Where(x => x.S("name").ToLowerInvariant().Contains(q) || x.S("number").Replace(" ", "").Contains(q.Replace(" ", ""))).Take(100).ToList();
                callInfo.Visible = found.Count == 0;
                callInfo.Text = contactData == null ? "Chưa đọc được danh bạ." : "Không tìm thấy.";
                foreach (var ct in found) AddPersonCall(ct.S("name"), ct.S("number"), ct.S("number"), "", Theme.Muted, w);
            }
            else
            {
                callInfo.Visible = callData.Count == 0;
                callInfo.Text = "Chưa có cuộc gọi nào.";
                string lastGroup = null;
                foreach (var cl in callData)
                {
                    var d = DateTimeOffset.FromUnixTimeMilliseconds(cl.L("time")).LocalDateTime;
                    string g = d.Date == DateTime.Today ? "Hôm nay" : d.Date == DateTime.Today.AddDays(-1) ? "Hôm qua" : d.Date > DateTime.Today.AddDays(-7) ? "Tuần này" : "Trước đó";
                    if (g != lastGroup)
                    {
                        lastGroup = g;
                        callList.Controls.Add(new Label { Tag = "row", Text = g, AutoSize = false, Size = new Size(w, Theme.S(32)), TextAlign = ContentAlignment.BottomLeft, Font = Theme.UIBold, ForeColor = Theme.Text2, Margin = Theme.P(4, 4, 0, 4) });
                    }
                    string type = cl.S("type");
                    string sub = type == "in" ? " Gọi đến" : type == "out" ? " Gọi đi" : type == "missed" ? " Gọi nhỡ" : type == "rejected" ? " Đã từ chối" : "Cuộc gọi";
                    if ((type == "in" || type == "out") && cl.L("duration") > 0) sub += " · " + TimeSpan.FromSeconds(cl.L("duration")).ToString(cl.L("duration") >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
                    string when = d.Date == DateTime.Today ? d.ToString("HH:mm") : d.Date > DateTime.Today.AddDays(-7) ? Thu(d) + " " + d.ToString("HH:mm") : d.ToString("dd/MM");
                    AddPersonCall(cl.S("name") != "" ? cl.S("name") : cl.S("number"), sub, cl.S("number"), when, type == "missed" || type == "rejected" ? Theme.Red : Theme.Muted, w);
                }
            }
            callList.ResumeLayout();
        }

        void AddPersonCall(string title, string sub, string number, string time, Color subColor, int w)
        {
            var row = new PersonRow(title, sub, time, 0) { Width = w, SubColor = subColor };
            new ToolTip().SetToolTip(row, "Bấm để đưa số vào bàn phím, bấm đúp để gọi ngay\n" + number);
            row.Click += (s, e) => dialNum.Text = number;
            row.DoubleClick += (s, e) => PlaceCall(number);
            callList.Controls.Add(row);
        }
    }

    /// <summary>Dong nguoi: avatar chu cai, ten, dong phu, gio, so chua doc.</summary>
    public class PersonRow : Control
    {
        public readonly string Title, Sub, Time;
        public int Unread;
        public bool Selected;
        public Color SubColor = Theme.Muted;
        bool hover;
        static readonly Color[] Palette = { Color.FromArgb(124, 77, 255), Color.FromArgb(41, 121, 255), Color.FromArgb(0, 150, 136), Color.FromArgb(233, 30, 99), Color.FromArgb(255, 112, 67), Color.FromArgb(171, 71, 188), Color.FromArgb(67, 160, 71), Color.FromArgb(3, 155, 229) };

        public PersonRow(string title, string sub, string time, int unread)
        {
            Title = title; Sub = sub; Time = time; Unread = unread;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(64); Margin = Theme.P(0, 0, 0, 2); Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        public static string Initials(string s)
        {
            s = (s ?? "").Trim();
            if (s == "") return "?";
            if (!char.IsLetter(s[0])) return "#";
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? (char.ToUpper(parts[0][0]).ToString() + char.ToUpper(parts[parts.Length - 1][0])) : char.ToUpper(s[0]).ToString();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Selected || hover)
                using (var p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(8)))
                using (var b = new SolidBrush(Selected ? Theme.AccentSoft : Theme.Hover)) g.FillPath(b, p);
            int d = Theme.S(40);
            var av = new Rectangle(Theme.S(10), (Height - d) / 2, d, d);
            using (var b = new SolidBrush(Palette[Math.Abs((Title ?? "").GetHashCode()) % Palette.Length])) g.FillEllipse(b, av);
            TextRenderer.DrawText(g, Initials(Title), Theme.UIBold, av, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int x = av.Right + Theme.S(12);
            int tw = TextRenderer.MeasureText(Time ?? "", Theme.Small).Width;
            TextRenderer.DrawText(g, Time ?? "", Theme.Small, new Rectangle(Width - tw - Theme.S(12), Theme.S(12), tw, Theme.S(18)), Theme.Muted, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Title, Unread > 0 ? Theme.UIBold : Theme.UIBold, new Rectangle(x, Theme.S(11), Width - x - tw - Theme.S(20), Theme.S(20)), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            var subFont = Sub != null && Sub.Length > 0 && Sub[0] >= '' && Sub[0] <= '' ? null : Theme.Small;
            var sr = new Rectangle(x, Theme.S(34), Width - x - Theme.S(44), Theme.S(18));
            if (subFont == null)
            {
                TextRenderer.DrawText(g, Sub.Substring(0, 1), Theme.IconsSmall, new Rectangle(sr.X, sr.Y, Theme.S(18), sr.Height), SubColor, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, Sub.Substring(1).Trim(), Theme.Small, new Rectangle(sr.X + Theme.S(20), sr.Y, sr.Width - Theme.S(20), sr.Height), SubColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }
            else TextRenderer.DrawText(g, Sub ?? "", Theme.Small, sr, Unread > 0 ? Theme.Text : SubColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            if (Unread > 0)
            {
                string s = Unread > 99 ? "99+" : Unread.ToString();
                int bw = Math.Max(Theme.S(20), TextRenderer.MeasureText(s, Theme.Tiny).Width + Theme.S(8)), bh = Theme.S(20);
                var br = new Rectangle(Width - bw - Theme.S(12), Theme.S(34), bw, bh);
                using (var p = Theme.Round(br, bh / 2)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
                TextRenderer.DrawText(g, s, Theme.Tiny, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>Mot tin nhan dang bong bong (ben phai = minh gui).</summary>
    public class BubbleRow : Control
    {
        readonly string text, meta;
        readonly bool mine;
        Rectangle bubble;

        public BubbleRow(string text, string meta, bool mine)
        {
            this.text = text ?? ""; this.meta = meta ?? ""; this.mine = mine;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Margin = Theme.P(0, 2, 0, 6);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Chép nội dung", null, (s, e) => { try { Clipboard.SetText(this.text); } catch { } });
            ContextMenuStrip = menu;
        }

        public void FitWidth(int w)
        {
            int maxText = Math.Max(Theme.S(120), (int)(w * 0.68)) - Theme.S(28);
            var sz = TextRenderer.MeasureText(text == "" ? " " : text, Theme.UI, new Size(maxText, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            int bw = Math.Min(maxText, sz.Width) + Theme.S(28), bh = sz.Height + Theme.S(20);
            bubble = new Rectangle(mine ? w - bw - 1 : 0, 0, bw, bh);
            Size = new Size(w, bh + Theme.S(18));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Round(bubble, Theme.S(14)))
            using (var b = new SolidBrush(mine ? Theme.Accent : Theme.Panel2)) g.FillPath(b, p);
            TextRenderer.DrawText(g, text, Theme.UI, Rectangle.Inflate(bubble, -Theme.S(14), -Theme.S(10)), mine ? Color.White : Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
            var mr = new Rectangle(bubble.X + Theme.S(4), bubble.Bottom + Theme.S(2), bubble.Width - Theme.S(8), Theme.S(16));
            TextRenderer.DrawText(g, meta, Theme.Small, mr, Theme.Muted, (mine ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Phim so tren ban phim goi.</summary>
    public class DialKey : Control
    {
        readonly string digit, letters;
        bool hover, press;
        static readonly Font DigitFont = new Font("Segoe UI", 17f);

        public DialKey(string d, string l)
        {
            digit = d; letters = l;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; press = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { press = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { press = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = Theme.Round(r, Theme.S(10)))
            {
                using (var b = new SolidBrush(press ? Theme.Line : hover ? Theme.Hover : Theme.Panel2)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, digit, DigitFont, new Rectangle(0, Theme.S(4), Width, Height - Theme.S(22)), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (letters != "") TextRenderer.DrawText(g, letters, Theme.Tiny, new Rectangle(0, Height - Theme.S(22), Width, Theme.S(16)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }
}
