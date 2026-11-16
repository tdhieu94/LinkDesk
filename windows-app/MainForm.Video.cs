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

        public bool H264Active => client != null && LastStatus.S("codec") == "h264";

        public void SetH264(bool on)
        {
            Cfg.H264 = on; Cfg.Save();
            h264Failed = false;
            RequestCodec();
        }

        void RequestCodec()
        {
            needKey = true;
            client?.Send("t", "codec", "h264", Cfg.H264 && !h264Failed);
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
                        decoder = new H264Decoder(w, h);
                        packetsNoOutput = 0;
                    }
                    needKey = false;
                    int before = decoder.FramesOut;
                    decoder.Decode(p, 6, p.Length - 6, (bmp, fw, fh) =>
                    {
                        c.FramesReceived++;
                        if (client != c || IsDisposed) { bmp.Dispose(); return; }
                        try { BeginInvoke(new Action(() => { if (client == c) screen.SetFrame(bmp, fw, fh); else bmp.Dispose(); })); } catch { bmp.Dispose(); }
                    });
                    if (decoder.FramesOut == 0 && ++packetsNoOutput > 90) throw new Exception("không ra hình sau 90 khung");
                }
                catch (Exception ex)
                {
                    FailH264(c, ex.Message);
                }
            }
        }

        void FailH264(PhoneClient c, string why)
        {
            h264Failed = true;
            try { decoder?.Dispose(); } catch { }
            decoder = null;
            c.Send("t", "codec", "h264", false);
            try { BeginInvoke(new Action(() => Toast("Không giải mã được video H.264 trên máy này (" + why + "). Đã chuyển về chế độ ảnh JPEG."))); } catch { }
        }
    }
}
