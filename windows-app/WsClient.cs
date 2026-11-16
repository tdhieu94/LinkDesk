using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LinkDesk
{
    /// <summary>
    /// WebSocket client toi gian (ws:// va wss://) de tu kiem soat chung chi TLS cua dien thoai.
    /// Dien thoai gui khung khong che (unmasked); may tinh gui khung co mat na theo chuan.
    /// </summary>
    public sealed class WsClient : IDisposable
    {
        TcpClient tcp;
        Stream st;
        volatile bool open;
        readonly SemaphoreSlim wlock = new SemaphoreSlim(1, 1);
        readonly RandomNumberGenerator rng = RandomNumberGenerator.Create();
        Timer keep;

        public bool IsOpen => open;

        public async Task ConnectAsync(string host, int port, bool secure, string pathAndQuery, CancellationToken ct)
        {
            tcp = new TcpClient { NoDelay = true };
            using (ct.Register(() => { try { tcp.Close(); } catch { } }))
            {
                var connect = tcp.ConnectAsync(host, port);
                if (await Task.WhenAny(connect, Task.Delay(8000, ct)).ConfigureAwait(false) != connect) throw new TimeoutException();
                await connect.ConfigureAwait(false);
                Stream s = tcp.GetStream();
                if (secure)
                {
                    var ssl = new SslStream(s, false, (o, cert, chain, err) => Tls.CheckHost(host, port, cert, err));
                    await ssl.AuthenticateAsClientAsync(host, null, SslProtocols.None, false).ConfigureAwait(false);
                    s = ssl;
                }
                st = s;
                var keyBytes = new byte[16]; rng.GetBytes(keyBytes);
                string key = Convert.ToBase64String(keyBytes);
                string req = "GET " + pathAndQuery + " HTTP/1.1\r\nHost: " + host + ":" + port + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n";
                var rb = Encoding.ASCII.GetBytes(req);
                await st.WriteAsync(rb, 0, rb.Length, ct).ConfigureAwait(false);
                await st.FlushAsync(ct).ConfigureAwait(false);
                string head = await ReadHead().ConfigureAwait(false);
                if (!head.StartsWith("HTTP/1.1 101")) throw new IOException("Upgrade refused: " + head.Split('\n')[0]);
                string want;
                using (var sha = SHA1.Create()) want = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                if (head.IndexOf(want, StringComparison.Ordinal) < 0) throw new IOException("Bad accept key");
            }
            open = true;
            // Gui pong dinh ky de dien thoai khong ngat vi im lang (giong KeepAliveInterval)
            keep = new Timer(_ => { if (open) SendFrame(10, new byte[0]).ContinueWith(t => { var x = t.Exception; }); }, null, 15000, 15000);
        }

        async Task<string> ReadHead()
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            while (sb.Length < 16384)
            {
                int n = await st.ReadAsync(one, 0, 1).ConfigureAwait(false);
                if (n <= 0) throw new IOException("eof");
                sb.Append((char)one[0]);
                if (sb.Length >= 4 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\r' && sb[sb.Length - 3] == '\n' && sb[sb.Length - 4] == '\r') return sb.ToString();
            }
            throw new IOException("header too long");
        }

        async Task ReadExact(byte[] b, int len, CancellationToken ct)
        {
            int off = 0;
            while (off < len)
            {
                int n = await st.ReadAsync(b, off, len - off, ct).ConfigureAwait(false);
                if (n <= 0) throw new IOException("eof");
                off += n;
            }
        }

        /// <summary>Nhan mot tin nhan tron ven. Tra ve null khi ket noi dong.</summary>
        public async Task<(bool text, byte[] data)?> ReceiveAsync(CancellationToken ct)
        {
            var hdr = new byte[8];
            MemoryStream msg = null;
            bool text = false;
            while (open)
            {
                await ReadExact(hdr, 2, ct).ConfigureAwait(false);
                bool fin = (hdr[0] & 0x80) != 0;
                int op = hdr[0] & 0x0F;
                bool masked = (hdr[1] & 0x80) != 0;
                long len = hdr[1] & 0x7F;
                if (len == 126) { await ReadExact(hdr, 2, ct).ConfigureAwait(false); len = (hdr[0] << 8) | hdr[1]; }
                else if (len == 127) { await ReadExact(hdr, 8, ct).ConfigureAwait(false); len = 0; for (int i = 0; i < 8; i++) len = (len << 8) | hdr[i]; }
                if (len > 64L * 1024 * 1024) throw new IOException("frame too big");
                var mask = new byte[4];
                if (masked) await ReadExact(mask, 4, ct).ConfigureAwait(false);
                var payload = new byte[len];
                await ReadExact(payload, (int)len, ct).ConfigureAwait(false);
                if (masked) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];
                if (op == 8) { open = false; return null; }
                if (op == 9) { await SendFrame(10, payload).ConfigureAwait(false); continue; }
                if (op == 10) continue;
                if (op != 0) { text = op == 1; msg = new MemoryStream(); }
                if (msg == null) continue;
                msg.Write(payload, 0, payload.Length);
                if (fin) return (text, msg.ToArray());
            }
            return null;
        }

        public Task SendText(string s) => SendFrame(1, Encoding.UTF8.GetBytes(s));

        async Task SendFrame(int op, byte[] data)
        {
            var s = st;
            if (s == null) return;
            int len = data.Length;
            var h = new MemoryStream();
            h.WriteByte((byte)(0x80 | op));
            if (len < 126) h.WriteByte((byte)(0x80 | len));
            else if (len < 65536) { h.WriteByte(0x80 | 126); h.WriteByte((byte)(len >> 8)); h.WriteByte((byte)len); }
            else { h.WriteByte(0x80 | 127); for (int i = 7; i >= 0; i--) h.WriteByte((byte)((long)len >> (8 * i))); }
            var mask = new byte[4]; rng.GetBytes(mask);
            h.Write(mask, 0, 4);
            var body = new byte[len];
            for (int i = 0; i < len; i++) body[i] = (byte)(data[i] ^ mask[i & 3]);
            h.Write(body, 0, len);
            var frame = h.ToArray();
            await wlock.WaitAsync().ConfigureAwait(false);
            try { await s.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false); await s.FlushAsync().ConfigureAwait(false); }
            catch { open = false; throw; }
            finally { wlock.Release(); }
        }

        public void Dispose()
        {
            open = false;
            try { keep?.Dispose(); } catch { }
            try { st?.Dispose(); } catch { }
            try { tcp?.Close(); } catch { }
        }
    }
}
