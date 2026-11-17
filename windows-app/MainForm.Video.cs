using System;
using System.Collections.Concurrent;
using System.Threading;

namespace LinkDesk
{
    // Video H.264 (thu nghiem): giai ma bang Media Foundation cua Windows, loi thi tu quay ve JPEG.
    public partial class MainForm
    {
        readonly BlockingCollection<(PhoneClient c, byte[] p)> videoQ = new BlockingCollection<(PhoneClient, byte[])>();
        Thread videoThread;
        volatile bool h264Failed;
        H264Decoder decoder;
        bool needKey = true;
        int packetsNoOutput;
        // Tu sua loi giai ma ngay tren may tinh truoc khi phai quay ve che do anh
        int decodeFails, framesSinceFail;
        bool decoderLowLatency = true;
        // Canh dai toi da xin dien thoai ma hoa. Bo giai ma cua Windows chi chiu toi 2304 diem anh.
        volatile int h264Cap = 2304;

        public bool H264Active => client != null && LastStatus.S("codec") == "h264";

        public void SetH264(bool on)
        {
            Cfg.H264 = on; Cfg.Save();
            h264Failed = false; h264Retries = 0;
            ResetDecodeRecovery();
            RequestCodec();
        }

        void RequestCodec()
        {
            needKey = true;
            if (!PhoneAtLeast(0, 10)) return;
            lastCodecAsk = DateTime.Now;
            client?.Send("t", "codec", "h264", Cfg.H264 && !h264Failed, "retry", true, "max", h264Cap);
        }

        DateTime lastCodecAsk;
        int h264Retries;

        /// <summary>Goi moi lan nhan trang thai: bat H.264 ma dien thoai dang gui anh (do loi tam) thi thu lai sau vai phut.</summary>
        void CheckCodec()
        {
            if (!Cfg.H264 || h264Failed || client == null || !client.IsOpen || !PhoneAtLeast(0, 10)) return;
            if (LastStatus.S("codec") == "h264" || !LastStatus.B("mirror")) return;
            if ((DateTime.Now - lastCodecAsk).TotalSeconds < 150) return;
            RequestCodec();
        }

        void OnVideoPacket(PhoneClient c, byte[] p)
        {
            if (client != c || h264Failed) return;
            if (videoThread == null)
            {
                videoThread = new Thread(VideoLoop) { IsBackground = true, Name = "h264" };
                videoThread.Start();
            }
            if (videoQ.Count > 45)
            {
                // May tinh giai ma khong kip: bo bot, cho khung khoa moi
                while (videoQ.TryTake(out _)) { }
                needKey = true;
                c.Send("t", "keyframe");
            }
            videoQ.Add((c, p));
        }

        void VideoLoop()
        {
            foreach (var (c, p) in videoQ.GetConsumingEnumerable())
            {
                if (client != c || h264Failed || p.Length < 7) continue;
                bool key = (p[1] & 1) != 0;
                int w = (p[2] << 8) | p[3], h = (p[4] << 8) | p[5];
                if (needKey && !key) continue;
                try
                {
                    if (decoder == null || decoder.Width != w || decoder.Height != h)
                    {
                        if (!key) { needKey = true; continue; }
                        decoder?.Dispose();
                        decoder = null;
                        decoder = new H264Decoder(w, h, decoderLowLatency);
                        packetsNoOutput = 0;
                    }
                    needKey = false;
                    int before = decoder.FramesOut;
                    decoder.Decode(p, 6, p.Length - 6, (bmp, fw, fh) =>
                    {
                        c.FramesReceived++;
                        if (client != c || IsDisposed) { bmp.Dispose(); return; }
                        try { BeginInvoke(new Action(() => { if (client == c) { RecordFrame(bmp, fw, fh); screen.SetFrame(bmp, fw, fh); } else bmp.Dispose(); })); } catch { bmp.Dispose(); }
                    });
                    if (decoder.FramesOut > before && ++framesSinceFail >= 60) decodeFails = 0;
                    if (decoder.FramesOut == 0 && ++packetsNoOutput > 90) throw new Exception("không ra hình sau 90 khung");
                }
                catch (Exception ex)
                {
                    RecoverDecode(c, ex.Message, Math.Max(w, h));
                }
            }
        }

        public string LastDecodeError;
        public int H264Cap => h264Cap;

        void ResetDecodeRecovery() { decodeFails = 0; framesSinceFail = 0; decoderLowLatency = true; h264Cap = 2304; }

        /// <summary>
        /// Loi giai ma (thuong la 0x80004005): tao lai bo giai ma va xin khung khoa moi, roi lan luot
        /// tat che do do tre thap, giam kich thuoc video. Chi quay ve anh JPEG khi van loi.
        /// </summary>
        void RecoverDecode(PhoneClient c, string why, int longSide)
        {
            try { decoder?.Dispose(); } catch { }
            decoder = null;
            needKey = true;
            framesSinceFail = 0;
            int n = ++decodeFails;
            LastDecodeError = DateTime.Now.ToString("HH:mm:ss") + " lần " + n + ": " + why + " (khung " + longSide + " px)";
            if (n >= 6) { FailH264(c, why); return; }
            if (n >= 2) decoderLowLatency = false;
            int cap = h264Cap;
            if (n >= 3) cap = longSide > 1920 ? 1920 : longSide > 1280 ? 1280 : cap;
            if (n >= 5) cap = Math.Min(cap, 1280);
            if (cap != h264Cap && PhoneAtLeast(0, 16)) { h264Cap = cap; RequestCodec(); }
            else c.Send("t", "keyframe");
        }

        void FailH264(PhoneClient c, string why)
        {
            h264Failed = true;
            try { decoder?.Dispose(); } catch { }
            decoder = null;
            c.Send("t", "codec", "h264", false);
            bool retry = ++h264Retries <= 2;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (retry)
                    {
                        // Loi tam thoi (doi do net, xoay man hinh...): dung anh 10 giay roi thu lai
                        var t = new System.Windows.Forms.Timer { Interval = 10000 };
                        t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (client == c && Cfg.H264) { h264Failed = false; decodeFails = 0; RequestCodec(); } };
                        t.Start();
                    }
                    else Toast("Máy tính này không giải mã được video H.264 (" + why + "), đang dùng chế độ ảnh. Tắt rồi bật lại mục H.264 trong Cài đặt để thử lại.");
                }));
            }
            catch { }
        }
    }
}

namespace LinkDesk
{
    // Chi xin hinh khi dang nhin thay man hinh dien thoai (tiet kiem pin dien thoai va WiFi).
    public partial class MainForm
    {
        bool? lastView;
        System.Windows.Forms.Timer viewTimer;

        bool IsViewing => pip != null || recorder != null || Visible && WindowState != System.Windows.Forms.FormWindowState.Minimized && center.Visible;

        void UpdateViewing(bool force = false)
        {
            bool v = IsViewing;
            if (!force && lastView == v) return;
            lastView = v;
            if (v) needKey = true;
            // App dien thoai cu chua biet lenh nay (se bao "lenh khong ho tro")
            if (PhoneAtLeast(0, 11)) client?.Send("t", "view", "on", v);
        }

        bool PhoneAtLeast(int major, int minor)
        {
            Version v;
            return Version.TryParse(LastStatus.S("appVersion"), out v) && (v.Major > major || (v.Major == major && v.Minor >= minor));
        }

        void InitViewTracking()
        {
            Resize += (s, e) => UpdateViewing();
            VisibleChanged += (s, e) => UpdateViewing();
            center.VisibleChanged += (s, e) => UpdateViewing();
            // Phong khi bo sot su kien
            viewTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            viewTimer.Tick += (s, e) => UpdateViewing();
            viewTimer.Start();
        }
    }
}
