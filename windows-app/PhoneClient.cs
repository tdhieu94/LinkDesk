using System;
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
    public enum ConnState { Connecting, Connected, Disconnected, BadPin, Locked }

    /// <summary>Ket noi toi app LinkDesk tren dien thoai (WebSocket + HTTP).</summary>
    public sealed class PhoneClient : IDisposable
    {
        public readonly string Host;
        public readonly int Port;
        public readonly string Pin;

        public event Action<Bitmap, int, int> Frame;               // anh, rong hop le, cao hop le
        public event Action<Dictionary<string, object>> Message;
        public event Action<byte[]> Audio;                          // [3][PCM 16-bit 48 kHz stereo]
        public event Action<ConnState, string> StateChanged;

        public long BytesReceived;
        public int FramesReceived;

        ClientWebSocket ws;
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        static readonly HttpClient http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        static readonly HttpClient upHttp = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromHours(2) };

        public PhoneClient(string host, int port, string pin)
        {
            Host = host; Port = port; Pin = pin;
        }

        string Base => "http://" + Host + ":" + Port;

        public bool IsOpen => ws != null && ws.State == WebSocketState.Open;

        /// <summary>Kiem tra PIN. Tra ve null neu dung, hoac thong bao loi.</summary>
        public static async Task<(ConnState state, string msg)> Check(string host, int port, string pin)
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

        static readonly HttpClient mediaHttp = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMinutes(10) };

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
                var (st, msg) = await Check(Host, Port, Pin).ConfigureAwait(false);
                if (st == ConnState.BadPin) { StateChanged?.Invoke(ConnState.BadPin, msg); return; }
                if (st == ConnState.Connected)
                {
                    try
                    {
                        ws = new ClientWebSocket();
                        ws.Options.Proxy = null;
                        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                        await ws.ConnectAsync(new Uri("ws://" + Host + ":" + Port + "/ws?pin=" + Uri.EscapeDataString(Pin)), cts.Token).ConfigureAwait(false);
                        retry = 0;
                        StateChanged?.Invoke(ConnState.Connected, null);
                        await ReceiveLoop(ws).ConfigureAwait(false);
                    }
                    catch (Exception) { }
                    finally { try { ws?.Dispose(); } catch { } ws = null; }
                }
                if (cts.IsCancellationRequested) break;
                StateChanged?.Invoke(st == ConnState.Locked ? ConnState.Locked : ConnState.Disconnected, msg);
                retry = Math.Min(retry + 1, 8);
                try { await Task.Delay(st == ConnState.Locked ? 30000 : 600 * retry, cts.Token).ConfigureAwait(false); } catch { break; }
            }
        }

        async Task ReceiveLoop(ClientWebSocket s)
        {
            var buf = new byte[256 * 1024];
            var ms = new MemoryStream();
            while (s.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await s.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);
                BytesReceived += ms.Length;
                if (r.MessageType == WebSocketMessageType.Text)
                {
                    Dictionary<string, object> m = null;
                    try { m = J.Parse(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length)); } catch { }
                    if (m != null) Message?.Invoke(m);
                }
                else
                {
                    var data = ms.ToArray();
                    if (data.Length > 1 && data[0] == 3) { Audio?.Invoke(data); continue; }
                    if (data.Length < 6 || data[0] != 1) continue;
                    int w = (data[1] << 8) | data[2], h = (data[3] << 8) | data[4];
                    try
                    {
                        // Khong dispose stream: GDI+ can stream ton tai cung anh
                        var img = new Bitmap(new MemoryStream(data, 5, data.Length - 5));
                        FramesReceived++;
                        Frame?.Invoke(img, Math.Min(w, img.Width), Math.Min(h, img.Height));
                    }
                    catch { }
                }
            }
        }

        public void Send(object o)
        {
            var s = ws;
            if (s == null || s.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(J.Ser(o));
            Task.Run(async () =>
            {
                await sendLock.WaitAsync().ConfigureAwait(false);
                try { await s.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false); }
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

        public void Dispose()
        {
            cts.Cancel();
            try { ws?.Abort(); } catch { }
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
