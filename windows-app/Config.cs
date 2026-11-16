using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace LinkDesk
{
    public class PhoneEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 8686;
        public string Pin { get; set; } = "";
        /// <summary>Dau van tay SHA-256 chung chi TLS cua dien thoai (rong = chua ghep doi ma hoa).</summary>
        public string CertSha { get; set; } = "";
    }

    public class Config
    {
        public List<PhoneEntry> Phones { get; set; } = new List<PhoneEntry>();
        public string LastHost { get; set; } = "";
        public bool MinimizeToTray { get; set; } = true;
        public bool ShowPhoneNotifications { get; set; } = true;
        public bool TrayHintShown { get; set; }
        public bool SidePanel { get; set; } = true;
        public bool AutoUpdate { get; set; } = true;
        public bool ClipSync { get; set; } = true;
        public bool Audio { get; set; } = false;
        public int AudioVolume { get; set; } = 100;
        /// <summary>Video H.264 (thu nghiem).</summary>
        public bool H264 { get; set; } = false;
        public bool TopMost { get; set; }
        public string Theme { get; set; } = "dark";
        public string SkippedVersion { get; set; } = "";
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }

        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LinkDesk");
        static string FilePath => Path.Combine(Dir, "config.json");

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new Config();
            }
            catch { }
            return new Config();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
            }
            catch { }
        }

        public PhoneEntry Last => Phones.FirstOrDefault(p => p.Host == LastHost) ?? Phones.FirstOrDefault();

        public void Remember(PhoneEntry e)
        {
            PhoneEntry old = null;
            if (!string.IsNullOrEmpty(e.Id)) old = Phones.FirstOrDefault(p => p.Id == e.Id);
            if (old == null) old = Phones.FirstOrDefault(p => p.Host == e.Host);
            if (old != null) Phones.Remove(old);
            Phones.Insert(0, e);
            LastHost = e.Host;
            Save();
        }
    }

    /// <summary>Doc gia tri tu Dictionary do JavaScriptSerializer tra ve.</summary>
    public static class J
    {
        static readonly JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public static string Ser(object o) => ser.Serialize(o);
        public static Dictionary<string, object> Parse(string s) => ser.DeserializeObject(s) as Dictionary<string, object>;
        public static string S(this Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v) : "";
        public static bool B(this Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is bool b && b;
        public static long L(this Dictionary<string, object> d, string k, long def = 0)
        {
            if (d == null || !d.TryGetValue(k, out var v) || v == null) return def;
            try { return Convert.ToInt64(v); } catch { return def; }
        }
        public static bool Has(this Dictionary<string, object> d, string k) => d != null && d.ContainsKey(k) && d[k] != null;
    }
}
