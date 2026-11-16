using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LinkDesk
{
    public enum ConnState { Connecting, Connected, Disconnected, BadPin, Locked, BadCert }

    /// <summary>Ket noi toi app LinkDesk tren dien thoai (WebSocket + HTTP).</summary>
    public sealed class PhoneClient : IDisposable
    {
        /// <summary>Dia chi hien tai (co the doi neu dien thoai doi IP, tim lai qua mang LAN).</summary>
        public volatile string Host;
        /// <summary>Ma dinh danh dien thoai (de tim lai khi doi IP).</summary>
        public string PhoneId = "";
        public readonly int Port;
        public readonly string Pin;
        public readonly string CertSha;
        /// <summary>Dang dung ket noi ma hoa (HTTPS/WSS).</summary>
        public volatile bool Secure;
        public string ObservedSha;

        public event Action<Bitmap, int, int> Frame;               // anh, rong hop le, cao hop le
        public event Action<Dictionary<string, object>> Message;
        public event Action<byte[]> Audio;
        public event Action<byte[]> Video;                          // [4][co][rong 2][cao 2][H.264 Annex-B]                          // [3][PCM 16-bit 48 kHz stereo]
        public event Action<ConnState, string> StateChanged;
        public event Action<string> HostChanged;

        // ---- so lieu cho trang Chan doan
        public int Reconnects;
        public string LastError = "";
        public int LatencyMs = -1;
        public DateTime ConnectedSince;
        long lastRx;
        TaskCompletionSource<bool> kick = new TaskCompletionSource<bool>();

        /// <summary>Thu ket noi lai ngay (vd. vua co mang lai, vua thuc day sau khi ngu).</summary>
        public void Kick() { kick.TrySetResult(true); }

        public long BytesReceived;
        public int FramesReceived;

        WsClient ws;
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        static readonly HttpClient http = new HttpClient(Tls.Handler()) { Timeout = TimeSpan.FromSeconds(5) };
        static readonly HttpClient upHttp = new HttpClient(Tls.Handler()) { Timeout = TimeSpan.FromHours(2) };

        public PhoneClient(string host, int port, string pin, string certSha = "")
        {
            Host = host; Port = port; Pin = pin; CertSha = certSha ?? "";
        }

        string Base => Secure ? "https://" + Host + ":" + (Port + Tls.PortOffset) : "http://" + Host + ":" + Port;

        public const string CertChangedMsg = "Mã bảo mật của điện thoại đã thay đổi (có thể do cài lại app LinkDesk, hoặc có thiết bị lạ đang giả mạo). Hãy kiểm tra rồi nhập lại mã PIN để ghép đôi lại.";

        public bool IsOpen => ws != null && ws.IsOpen;

        /// <summary>Kiem tra PIN. Tra ve null neu dung, hoac thong bao loi.</summary>
        public static async Task<(ConnState state, string msg)> Check(string host, int port, string pin)
        {
            var (st, msg, _, _) = await CheckSecure(host, port, pin, "").ConfigureAwait(false);
            return (st, msg);
        }

        /// <summary>
        /// Thu ket noi ma hoa truoc. Neu dien thoai da ghim chung chi (certSha) thi bat buoc ma hoa;
        /// neu chua (app dien thoai ban cu) thi moi dung ket noi thuong.
        /// </summary>
        public static async Task<(ConnState state, string msg, bool secure, string sha)> CheckSecure(string host, int port, string pin, string certSha)
        {
            int tp = port + Tls.PortOffset;
            Tls.Expect(host, tp, certSha);
            try
            {
                var r = await http.GetAsync("https://" + host + ":" + tp + "/check?pin=" + Uri.EscapeDataString(pin ?? "")).ConfigureAwait(false);
                string sha = Tls.Observed(host, tp);
                if (r.IsSuccessStatusCode) return (ConnState.Connected, null, true, sha);
                string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if ((int)r.StatusCode == 429) return (ConnState.Locked, body, true, sha);
                return (ConnState.BadPin, "Sai mã PIN", true, sha);
            }
            catch (Exception)
            {
                if (Tls.Mismatch(host, tp)) return (ConnState.BadCert, CertChangedMsg, true, null);
                if (!string.IsNullOrEmpty(certSha))
                    return (ConnState.Disconnected, "Không kết nối được điện thoại. Kiểm tra app LinkDesk đã bấm \"Bắt đầu chia sẻ\" và hai máy dùng chung WiFi.", true, null);
            }
            var (st, msg) = await CheckPlain(host, port, pin).ConfigureAwait(false);
            return (st, msg, false, null);
        }

        static async Task<(ConnState state, string msg)> CheckPlain(string host, int port, string pin)
        {
            try
            {
                var r = await http.GetAsync("http://" + host + ":" + port + "/check?pin=" + Uri.EscapeDataString(pin ?? "")).ConfigureAwait(false);
                if (r.IsSuccessStatusCode) return (ConnState.Connected, null);
                string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if ((int)r.StatusCode == 429) return (ConnState.Locked, body);
                return (ConnState.BadPin, "Sai mã PIN");
            }
            catch (Exception)
            {
                return (ConnState.Disconnected, "Không kết nối được điện thoại. Kiểm tra app LinkDesk đã bấm \"Bắt đầu chia sẻ\" và hai máy dùng chung WiFi.");
            }
        }

        static readonly HttpClient mediaHttp = new HttpClient(Tls.Handler()) { Timeout = TimeSpan.FromMinutes(10) };

        /// <summary>GET mot duong dan co PIN (anh, bieu tuong...). Loi -> nem Exception voi noi dung tra ve.</summary>
        /// <summary>Tai mot duong dan ve tep (khong giu ca tep trong bo nho).</summary>
        public async Task DownloadTo(string path, string file, IProgress<double> progress, params string[] kv)
        {
            var sb = new StringBuilder(Base + path + "?pin=" + Uri.EscapeDataString(Pin));
            for (int i = 0; i + 1 < kv.Length; i += 2) sb.Append("&" + kv[i] + "=" + Uri.EscapeDataString(kv[i + 1]));
            using (var r = await upHttp.GetAsync(sb.ToString(), HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!r.IsSuccessStatusCode) throw new Exception(await r.Content.ReadAsStringAsync().ConfigureAwait(false));
                long total = r.Content.Headers.ContentLength ?? -1;
                string tmp = file + ".part";
                using (var src = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = File.Create(tmp))
                {
                    var buf = new byte[81920];
                    long done = 0; int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n).ConfigureAwait(false);
                        done += n;
                        if (total > 0) progress?.Report((double)done / total);
                    }
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
        }

        public async Task<byte[]> GetBytes(string path, params string[] kv)
        {
            var sb = new StringBuilder(Base + path + "?pin=" + Uri.EscapeDataString(Pin));
            for (int i = 0; i + 1 < kv.Length; i += 2) sb.Append("&" + kv[i] + "=" + Uri.EscapeDataString(kv[i + 1]));
            var r = await mediaHttp.GetAsync(sb.ToString()).ConfigureAwait(false);
            var body = await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (!r.IsSuccessStatusCode) throw new Exception(Encoding.UTF8.GetString(body));
            return body;
        }

        public void Start() => Task.Run(RunLoop);

        async Task RunLoop()
        {
            int retry = 0;
            while (!cts.IsCancellationRequested)
            {
                StateChanged?.Invoke(ConnState.Connecting, null);
                var (st, msg, secure, sha) = await CheckSecure(Host, Port, Pin, CertSha).ConfigureAwait(false);
                if (st == ConnState.BadPin || st == ConnState.BadCert) { StateChanged?.Invoke(st, msg); return; }
                Secure = secure; if (sha != null) ObservedSha = sha;
                if (st == ConnState.Connected)
                {
                    try
                    {
                        ws = new WsClient();
                        await ws.ConnectAsync(Host, Secure ? Port + Tls.PortOffset : Port, Secure, "/ws?pin=" + Uri.EscapeDataString(Pin), cts.Token).ConfigureAwait(false);
                        retry = 0;
                        ConnectedSince = DateTime.Now;
                        lastRx = Environment.TickCount;
                        StateChanged?.Invoke(ConnState.Connected, null);
                        var watch = Watchdog(ws);
                        await ReceiveLoop(ws).ConfigureAwait(false);
                        LastError = "Mất kết nối lúc " + DateTime.Now.ToString("HH:mm:ss");
                    }
                    catch (Exception ex) { LastError = DateTime.Now.ToString("HH:mm:ss") + " " + ex.Message; }
                    finally { try { ws?.Dispose(); } catch { } ws = null; }
                }
                if (cts.IsCancellationRequested) break;
                if (st != ConnState.Connected && !string.IsNullOrEmpty(msg)) LastError = DateTime.Now.ToString("HH:mm:ss") + " " + msg;
                StateChanged?.Invoke(st == ConnState.Locked ? ConnState.Locked : ConnState.Disconnected, msg);
                Reconnects++;
                retry = Math.Min(retry + 1, 8);
                // Dien thoai co the da doi dia chi IP (WiFi cap lai): tim lai bang ma dinh danh
                if (st == ConnState.Disconnected && PhoneId != "" && (retry == 2 || retry % 4 == 0))
                {
                    try
                    {
                        var found = (await Discovery.Scan(1500).ConfigureAwait(false)).FirstOrDefault(p => p.Id == PhoneId);
                        if (found != null && found.Host != Host) { Host = found.Host; HostChanged?.Invoke(found.Host); retry = 0; continue; }
                    }
                    catch { }
                }
                var k = kick = new TaskCompletionSource<bool>();
                try { await Task.WhenAny(Task.Delay(st == ConnState.Locked ? 30000 : 500 * retry, cts.Token), k.Task).ConfigureAwait(false); } catch { }
                if (cts.IsCancellationRequested) break;
                if (k.Task.IsCompleted) retry = 0;
            }
        }

        /// <summary>Hoi dien thoai moi 5 giay; 20 giay khong nghe gi thi coi nhu mat ket noi de noi lai ngay.</summary>
        async Task Watchdog(WsClient s)
        {
            while (s.IsOpen && !cts.IsCancellationRequested)
            {
                try { await Task.Delay(5000, cts.Token).ConfigureAwait(false); } catch { return; }
                if (!s.IsOpen) return;
                if (Environment.TickCount - lastRx > 20000)
                {
                    LastError = DateTime.Now.ToString("HH:mm:ss") + " Điện thoại không phản hồi 20 giây";
                    try { s.Dispose(); } catch { }
                    return;
                }
                Send("t", "ping", "ts", Environment.TickCount & 0xFFFFFFFFL);
            }
        }

        async Task ReceiveLoop(WsClient s)
        {
            while (s.IsOpen && !cts.IsCancellationRequested)
            {
                var r = await s.ReceiveAsync(cts.Token).ConfigureAwait(false);
                if (r == null) return;
                var data = r.Value.data;
                BytesReceived += data.Length;
                lastRx = Environment.TickCount;
                if (r.Value.text)
                {
                    Dictionary<string, object> m = null;
                    try { m = J.Parse(Encoding.UTF8.GetString(data)); } catch { }
                    if (m != null && m.S("t") == "pong" && m.Has("ts"))
                    {
                        long ts = m.L("ts");
                        LatencyMs = (int)Math.Max(0, (Environment.TickCount & 0xFFFFFFFFL) - ts);
                        continue;
                    }
                    if (m != null) Message?.Invoke(m);
                }
                else
                {
                    if (data.Length > 1 && data[0] == 3) { Audio?.Invoke(data); continue; }
                    if (data.Length > 6 && data[0] == 4) { Video?.Invoke(data); continue; }
                    if (data.Length < 6 || data[0] != 1) continue;
                    PostJpeg(data);
                }
            }
        }

        // ---- giai nen JPEG tren luong rieng, chi giu khung moi nhat (may cham thi bo khung cu)
        byte[] pendingJpeg;
        readonly AutoResetEvent jpegReady = new AutoResetEvent(false);
        Thread jpegThread;

        void PostJpeg(byte[] data)
        {
            Interlocked.Exchange(ref pendingJpeg, data);
            if (jpegThread == null)
            {
                jpegThread = new Thread(JpegLoop) { IsBackground = true, Name = "jpeg" };
                jpegThread.Start();
            }
            jpegReady.Set();
        }

        void JpegLoop()
        {
            while (!cts.IsCancellationRequested)
            {
                jpegReady.WaitOne(1000);
                var data = Interlocked.Exchange(ref pendingJpeg, null);
                if (data == null) continue;
                int w = (data[1] << 8) | data[2], h = (data[3] << 8) | data[4];
                try
                {
                    Bitmap img;
                    // Giai nen ngay tai day (GDI+ mac dinh de den luc ve moi giai nen, tren luong giao dien)
                    // va doi sang 32bppPArgb la dinh dang ve nhanh nhat.
                    using (var ms = new MemoryStream(data, 5, data.Length - 5))
                    using (var src = new Bitmap(ms))
                    {
                        img = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(img))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
                        }
                    }
                    FramesReceived++;
                    Frame?.Invoke(img, Math.Min(w, img.Width), Math.Min(h, img.Height));
                }
                catch { }
            }
        }

        public void Send(object o)
        {
            var s = ws;
            if (s == null || !s.IsOpen) return;
            var text = J.Ser(o);
            Task.Run(async () =>
            {
                await sendLock.WaitAsync().ConfigureAwait(false);
                try { await s.SendText(text).ConfigureAwait(false); }
                catch { }
                finally { sendLock.Release(); }
            });
        }

        public void Send(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            Send((object)d);
        }

        /// <summary>Gui tep sang dien thoai. Tra ve duong dan da luu, hoac nem loi.</summary>
        public async Task<string> Upload(string path, IProgress<double> progress)
        {
            string name = Path.GetFileName(path);
            using (var fs = File.OpenRead(path))
            {
                var content = new ProgressContent(fs, progress);
                var r = await upHttp.PostAsync(Base + "/upload?pin=" + Uri.EscapeDataString(Pin) + "&name=" + Uri.EscapeDataString(name), content).ConfigureAwait(false);
                string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!r.IsSuccessStatusCode) throw new Exception(body);
                return body;
            }
        }

        public async Task<string> PostBytes(string path, byte[] data, string type)
        {
            var content = new ByteArrayContent(data);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            var r = await upHttp.PostAsync(Base + path + "?pin=" + Uri.EscapeDataString(Pin), content).ConfigureAwait(false);
            string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!r.IsSuccessStatusCode) throw new Exception(body);
            return body;
        }

        public void Dispose()
        {
            cts.Cancel();
            try { ws?.Dispose(); } catch { }
        }

        sealed class ProgressContent : HttpContent
        {
            readonly Stream src; readonly IProgress<double> progress;
            public ProgressContent(Stream s, IProgress<double> p) { src = s; progress = p; Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream"); }
            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                var buf = new byte[81920]; long done = 0; int n;
                while ((n = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                {
                    await stream.WriteAsync(buf, 0, n).ConfigureAwait(false);
                    done += n;
                    progress?.Report(src.Length > 0 ? (double)done / src.Length : 1);
                }
            }
            protected override bool TryComputeLength(out long length) { length = src.Length; return true; }
        }
    }
}
