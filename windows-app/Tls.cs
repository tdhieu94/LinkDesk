using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LinkDesk
{
    /// <summary>
    /// Ghim chung chi TLS tu ky cua dien thoai: lan ghep doi dau tien ghi nho dau van tay SHA-256,
    /// cac lan sau chi chap nhan dung chung chi do (chong nghe len / gia mao trong cung WiFi).
    /// </summary>
    public static class Tls
    {
        public const int PortOffset = 2; // 8686 -> 8688
        static readonly object gate = new object();
        static readonly Dictionary<string, string> expect = new Dictionary<string, string>();   // host:port -> sha ("" = chap nhan lan dau)
        static readonly Dictionary<string, string> observed = new Dictionary<string, string>();
        static readonly HashSet<string> mismatch = new HashSet<string>();

        public static void Init()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = Validate;
        }

        static string Key(string host, int port) => host + ":" + port;

        public static void Expect(string host, int port, string sha)
        {
            lock (gate) { expect[Key(host, port)] = (sha ?? "").ToUpperInvariant(); mismatch.Remove(Key(host, port)); }
        }

        public static string Observed(string host, int port)
        {
            lock (gate) return observed.TryGetValue(Key(host, port), out var s) ? s : null;
        }

        public static bool Mismatch(string host, int port)
        {
            lock (gate) return mismatch.Contains(Key(host, port));
        }

        public static string Sha256(X509Certificate cert)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(cert.GetRawCertData())).Replace("-", "");
        }

        /// <summary>Ma ngan de so voi dien thoai, vd "3F9A 1C07".</summary>
        public static string ShortCode(string sha) => string.IsNullOrEmpty(sha) || sha.Length < 8 ? "" : sha.Substring(0, 4) + " " + sha.Substring(4, 4);

        /// <summary>HttpClientHandler kiem tra chung chi theo dia chi cua tung yeu cau.</summary>
        public static System.Net.Http.HttpClientHandler Handler()
        {
            var h = new System.Net.Http.HttpClientHandler { UseProxy = false };
            try { h.ServerCertificateCustomValidationCallback = (req, cert, chain, err) => Check(req?.RequestUri, cert, err); } catch { }
            return h;
        }

        static bool Validate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
            => Check((sender as HttpWebRequest)?.RequestUri, cert, errors);

        public static bool CheckHost(string host, int port, X509Certificate cert, SslPolicyErrors errors)
            => Check(new Uri("https://" + host + ":" + port + "/"), cert, errors);

        static bool Check(Uri uri, X509Certificate cert, SslPolicyErrors errors)
        {
            if (cert == null) return false;
            string sha = Sha256(cert);
            lock (gate)
            {
                if (uri != null)
                {
                    string k = Key(uri.Host, uri.Port);
                    if (!expect.TryGetValue(k, out var want))
                        return errors == SslPolicyErrors.None; // khong phai dien thoai: kiem tra binh thuong (vd GitHub)
                    observed[k] = sha;
                    if (want == "" || want == sha) return true;
                    mismatch.Add(k);
                    return false;
                }
                // Khong biet dia chi: chi nhan chung chi hop le hoac da ghim
                if (errors == SslPolicyErrors.None) return true;
                foreach (var w in expect.Values) if (w == sha) return true;
                return false;
            }
        }
    }
}
