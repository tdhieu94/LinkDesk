package vn.linkdesk;

import android.content.Context;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.provider.Settings;
import android.util.Log;

import org.json.JSONObject;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;

/** Tra loi goi tin UDP "LINKDESK?" de app Windows tu tim thay dien thoai trong WiFi. */
public class Discovery {
    public static final int PORT = 8687;
    private static final String TAG = "LinkDesk.Disc";
    private final Context ctx;
    private DatagramSocket socket;
    private WifiManager.MulticastLock lock;
    private volatile boolean running;

    public Discovery(Context ctx) { this.ctx = ctx.getApplicationContext(); }

    public static String deviceName(Context c) {
        String n = null;
        try { n = Settings.Global.getString(c.getContentResolver(), "device_name"); } catch (Exception ignored) {}
        if (n == null || n.trim().isEmpty()) n = Build.MANUFACTURER + " " + Build.MODEL;
        return n;
    }

    public void start() {
        try {
            WifiManager wm = (WifiManager) ctx.getSystemService(Context.WIFI_SERVICE);
            if (wm != null) {
                lock = wm.createMulticastLock("LinkDesk");
                lock.setReferenceCounted(false);
                lock.acquire();
            }
        } catch (Exception e) { Log.w(TAG, "multicast lock", e); }
        running = true;
        Thread t = new Thread(new Runnable() {
            @Override public void run() { loop(); }
        }, "discovery");
        t.setDaemon(true);
        t.start();
    }

    private void loop() {
        try {
            socket = new DatagramSocket(null);
            socket.setReuseAddress(true);
            socket.setBroadcast(true);
            socket.bind(new InetSocketAddress(PORT));
            byte[] buf = new byte[512];
            while (running) {
                DatagramPacket p = new DatagramPacket(buf, buf.length);
                socket.receive(p);
                String msg = new String(p.getData(), 0, p.getLength(), StandardCharsets.UTF_8);
                if (!msg.startsWith("LINKDESK?")) continue;
                String reply = "LINKDESK!" + new JSONObject()
                        .put("name", deviceName(ctx))
                        .put("model", Build.MANUFACTURER + " " + Build.MODEL)
                        .put("port", Hub.PORT)
                        .put("tls", Hub.TLS_PORT)
                        .put("id", Settings.Secure.getString(ctx.getContentResolver(), Settings.Secure.ANDROID_ID))
                        .toString();
                byte[] out = reply.getBytes(StandardCharsets.UTF_8);
                socket.send(new DatagramPacket(out, out.length, p.getAddress(), p.getPort()));
            }
        } catch (Exception e) {
            if (running) Log.w(TAG, "loop", e);
        }
    }

    public void stop() {
        running = false;
        try { if (socket != null) socket.close(); } catch (Exception ignored) {}
        try { if (lock != null && lock.isHeld()) lock.release(); } catch (Exception ignored) {}
    }
}
