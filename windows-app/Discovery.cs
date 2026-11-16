using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LinkDesk
{
    public class FoundPhone
    {
        public string Id, Name, Model, Host;
        public int Port;
        public override string ToString() => Name + "   (" + Host + ")";
    }

    /// <summary>Tim dien thoai dang chay LinkDesk trong cung WiFi (UDP broadcast cong 8687).</summary>
    public static class Discovery
    {
        public const int Port = 8687;

        static IEnumerable<IPAddress> BroadcastTargets()
        {
            yield return IPAddress.Broadcast;
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); } catch { yield break; }
            foreach (var ni in nics)
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    if (mask.All(b => b == 0)) continue;
                    var bc = new byte[4];
                    for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                    yield return new IPAddress(bc);
                }
            }
        }

        public static async Task<List<FoundPhone>> Scan(int timeoutMs = 1500)
        {
            var found = new Dictionary<string, FoundPhone>();
            using (var udp = new UdpClient(0))
            {
                udp.EnableBroadcast = true;
                var q = Encoding.UTF8.GetBytes("LINKDESK?");
                foreach (var t in BroadcastTargets().Distinct())
                {
                    try { await udp.SendAsync(q, q.Length, new IPEndPoint(t, Port)).ConfigureAwait(false); } catch { }
                }
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (true)
                {
                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) break;
                    var recv = udp.ReceiveAsync();
                    var done = await Task.WhenAny(recv, Task.Delay(left)).ConfigureAwait(false);
                    if (done != recv) { _ = recv.ContinueWith(x => { var _e = x.Exception; }); break; }
                    UdpReceiveResult r;
                    try { r = recv.Result; } catch { continue; }
                    var s = Encoding.UTF8.GetString(r.Buffer);
                    if (!s.StartsWith("LINKDESK!")) continue;
                    var d = J.Parse(s.Substring(9));
                    if (d == null) continue;
                    var p = new FoundPhone
                    {
                        Id = d.S("id"), Name = d.S("name"), Model = d.S("model"),
                        Host = r.RemoteEndPoint.Address.ToString(), Port = (int)d.L("port", 8686)
                    };
                    found[string.IsNullOrEmpty(p.Id) ? p.Host : p.Id] = p;
                }
            }
            return found.Values.ToList();
        }
    }
}
