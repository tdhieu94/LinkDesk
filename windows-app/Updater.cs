using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkDesk
{
    /// <summary>Kiem tra va cai ban moi tu repo GitHub tdhieu94/LinkDesk (thu muc releases/).</summary>
    public static class Updater
    {
        public const string VersionUrl = "https://raw.githubusercontent.com/tdhieu94/LinkDesk/main/releases/version.json";
        public const string ReleasesPage = "https://github.com/tdhieu94/LinkDesk/tree/main/releases";

        public class Info
        {
            public Version Version;
            public string Url, Notes, AndroidVersion;
        }

        static readonly HttpClient http;
        static Updater()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LinkDesk");
        }

        public static Version Current => new Version(Installer.Version);

        /// <summary>Tra ve thong tin ban moi hon, hoac null.</summary>
        public static async Task<Info> Check()
        {
            try
            {
                string json = await http.GetStringAsync(VersionUrl).ConfigureAwait(false);
                var d = J.Parse(json);
                var w = d != null && d.TryGetValue("windows", out var o) ? o as System.Collections.Generic.Dictionary<string, object> : null;
                if (w == null) return null;
                var info = new Info { Version = new Version(w.S("version")), Url = w.S("url"), Notes = d.S("notes") };
                if (d.TryGetValue("android", out var a) && a is System.Collections.Generic.Dictionary<string, object> ad) info.AndroidVersion = ad.S("versionName");
                return info.Version > Current ? info : null;
            }
            catch { return null; }
        }

        /// <summary>Tai ban moi roi chay no voi --apply-update; ban moi tu thay the exe hien tai va khoi dong lai.</summary>
        public static async Task Download(Info info, IProgress<double> progress)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "LinkDesk-" + info.Version + ".exe");
            using (var r = await http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                r.EnsureSuccessStatusCode();
                long total = r.Content.Headers.ContentLength ?? -1;
                using (var src = await r.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = File.Create(tmp))
                {
                    var buf = new byte[81920]; long done = 0; int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n).ConfigureAwait(false);
                        done += n;
                        if (total > 0) progress?.Report((double)done / total);
                    }
                }
            }
            string target = Installer.IsInstalled ? Installer.InstalledExe : Installer.CurrentExe;
            Process.Start(new ProcessStartInfo(tmp, "--apply-update \"" + target + "\" " + Process.GetCurrentProcess().Id) { UseShellExecute = false });
        }

        /// <summary>Chay trong ban moi (tu thu muc Temp): doi ban cu thoat, chep de, mo lai.</summary>
        public static void Apply(string target, int oldPid)
        {
            try { Process.GetProcessById(oldPid).WaitForExit(15000); } catch { }
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    File.Copy(Installer.CurrentExe, target, true);
                    Installer.RefreshUninstallEntry();
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    return;
                }
                catch (IOException) { Thread.Sleep(500); }
                catch (UnauthorizedAccessException) { Thread.Sleep(500); }
            }
            MessageBox.Show("Không cập nhật được vì LinkDesk cũ vẫn đang chạy. Hãy thoát LinkDesk rồi thử lại.", "LinkDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
