using System;
using System.Drawing;
using System.Windows.Forms;

namespace LinkDesk
{
    /// <summary>Cua so nho goc duoi phai bao cuoc goi den, giong Phone Link.</summary>
    public class CallPopup : Form
    {
        readonly Label who = new Label { AutoSize = false, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Theme.Text, BackColor = Theme.Panel };
        readonly Label sub = new Label { AutoSize = false, Font = Theme.UI, ForeColor = Theme.Muted, BackColor = Theme.Panel };
        readonly RoundButton answer, decline;
        readonly Action<string> send;

        public CallPopup(Action<string> send)
        {
            this.send = send;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Panel; ClientSize = Theme.Sz(340, 150); Font = Theme.UI;
            var avatar = new Label { Text = "", Font = new Font(Theme.Icons.FontFamily, 20f), ForeColor = Theme.AccentText, BackColor = Theme.AccentSoft, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(Theme.S(18), Theme.S(20), Theme.S(52), Theme.S(52)) };
            using (var p = Theme.Round(new Rectangle(0, 0, avatar.Width, avatar.Height), avatar.Width / 2)) avatar.Region = new Region(p);
            who.Bounds = new Rectangle(Theme.S(84), Theme.S(20), Theme.S(240), Theme.S(28));
            sub.Bounds = new Rectangle(Theme.S(84), Theme.S(48), Theme.S(240), Theme.S(22));
            answer = new RoundButton("", "Nghe", BtnStyle.Primary) { Radius = 8 };
            decline = new RoundButton("", "Từ chối", BtnStyle.Danger) { Radius = 8 };
            answer.Bounds = new Rectangle(Theme.S(18), Theme.S(94), Theme.S(146), Theme.S(38));
            decline.Bounds = new Rectangle(Theme.S(176), Theme.S(94), Theme.S(146), Theme.S(38));
            answer.Click += (s, e) => { send("answerCall"); };
            decline.Click += (s, e) => { send("endCall"); Hide(); };
            Controls.Add(avatar); Controls.Add(who); Controls.Add(sub); Controls.Add(answer); Controls.Add(decline);
            Paint += (s, e) => { using (var pen = new Pen(Theme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); };
        }

        protected override bool ShowWithoutActivation => true;

        public void Ringing(string name, string number)
        {
            who.Text = string.IsNullOrEmpty(name) ? (string.IsNullOrEmpty(number) ? "Số không xác định" : number) : name;
            sub.Text = "Cuộc gọi đến" + (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(number) ? " · " + number : "");
            answer.Visible = true;
            decline.SetText("Từ chối"); decline.Width = Theme.S(146);
            answer.Bounds = new Rectangle(Theme.S(18), Theme.S(94), Theme.S(146), Theme.S(38));
            decline.Bounds = new Rectangle(Theme.S(176), Theme.S(94), Theme.S(146), Theme.S(38));
            Place();
            if (!Visible) Show();
        }

        public void InCall()
        {
            if (!Visible) return;
            sub.Text = "Đang gọi · nói chuyện trên điện thoại";
            answer.Visible = false;
            decline.SetText("Kết thúc");
            decline.Bounds = new Rectangle(Theme.S(18), Theme.S(94), Theme.S(304), Theme.S(38));
        }

        void Place()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - Theme.S(16), wa.Bottom - Height - Theme.S(16));
        }
    }
}
