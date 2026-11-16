package vn.linkdesk;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageInstaller;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.util.Log;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;

/**
 * Kiem tra ban moi tren repo GitHub tdhieu94/LinkDesk (thu muc releases/) va cai bang PackageInstaller.
 * Android luon hien hop thoai xac nhan "Cap nhat" cho app cai ngoai CH Play.
 */
public final class Updater {
    public static final String VERSION_URL = "https://raw.githubusercontent.com/tdhieu94/LinkDesk/main/releases/version.json";
    private static final String TAG = "LinkDesk.Upd";
    private static final String ACTION_INSTALL = "vn.linkdesk.INSTALL_RESULT";

    public static class Info {
        public long versionCode;
        public String versionName, url, notes;
    }

    public interface Listener {
        void onResult(Info newer);         // null neu khong co ban moi
    }

    public interface Progress {
        void onProgress(int percent);
        void onDone(String error);         // null = da gui sang trinh cai dat
    }

    private static final Handler main = new Handler(Looper.getMainLooper());

    private static HttpURLConnection open(String url) throws Exception {
        // Tu theo chuyen huong (github.com -> objects.githubusercontent.com)
        for (int i = 0; i < 6; i++) {
            HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
            c.setInstanceFollowRedirects(false);
            c.setConnectTimeout(15000);
            c.setReadTimeout(30000);
            c.setRequestProperty("User-Agent", "LinkDesk");
            int code = c.getResponseCode();
            if (code >= 300 && code < 400) {
                url = new URL(new URL(url), c.getHeaderField("Location")).toString();
                c.disconnect();
                continue;
            }
            if (code != 200) { c.disconnect(); throw new Exception("HTTP " + code); }
            return c;
        }
        throw new Exception("Qua nhieu chuyen huong");
    }

    public static void check(final Context ctx, final Listener l) {
        new Thread(new Runnable() {
            @Override public void run() {
                Info newer = null;
                try {
                    HttpURLConnection c = open(VERSION_URL);
                    InputStream in = c.getInputStream();
                    ByteArrayOutputStream b = new ByteArrayOutputStream();
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = in.read(buf)) > 0) b.write(buf, 0, n);
                    in.close();
                    JSONObject j = new JSONObject(b.toString("UTF-8"));
                    JSONObject a = j.getJSONObject("android");
                    Info info = new Info();
                    info.versionCode = a.getLong("versionCode");
                    info.versionName = a.optString("versionName");
                    info.url = a.getString("url");
                    info.notes = j.optString("notes");
                    if (info.versionCode > BuildInfo.versionCode(ctx)) newer = info;
                } catch (Exception e) {
                    Log.i(TAG, "check: " + e);
                }
                final Info r = newer;
                main.post(new Runnable() { @Override public void run() { l.onResult(r); } });
            }
        }, "update-check").start();
    }

    /** Android 8+: app phai duoc phep "Cai ung dung khong ro nguon". */
    public static boolean canInstall(Context ctx) {
        return ctx.getPackageManager().canRequestPackageInstalls();
    }

    public static void openInstallPermission(Activity a) {
        a.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + a.getPackageName())));
    }

    public static void install(final Activity a, final Info info, final Progress p) {
        final Context ctx = a.getApplicationContext();
        // Nhan ket qua tu trinh cai dat: can nguoi dung xac nhan -> mo hop thoai
        BroadcastReceiver r = new BroadcastReceiver() {
            @Override public void onReceive(Context c, Intent i) {
                int status = i.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
                if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
                    Intent confirm = Build.VERSION.SDK_INT >= 33
                            ? i.getParcelableExtra(Intent.EXTRA_INTENT, Intent.class)
                            : (Intent) i.getParcelableExtra(Intent.EXTRA_INTENT);
                    if (confirm != null) {
                        confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                        try { c.startActivity(confirm); } catch (Exception ignored) {}
                    }
                } else if (status != PackageInstaller.STATUS_SUCCESS) {
                    final String msg = i.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);
                    main.post(new Runnable() { @Override public void run() { p.onDone("Cài thất bại: " + msg); } });
                }
            }
        };
        IntentFilter f = new IntentFilter(ACTION_INSTALL);
        if (Build.VERSION.SDK_INT >= 33) ctx.registerReceiver(r, f, Context.RECEIVER_NOT_EXPORTED);
        else ctx.registerReceiver(r, f);

        new Thread(new Runnable() {
            @Override public void run() {
                String err = null;
                try {
                    HttpURLConnection c = open(info.url);
                    long total = c.getContentLengthLong();
                    PackageInstaller pi = ctx.getPackageManager().getPackageInstaller();
                    PackageInstaller.SessionParams sp = new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
                    sp.setAppPackageName(ctx.getPackageName());
                    if (Build.VERSION.SDK_INT >= 31) sp.setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED);
                    int id = pi.createSession(sp);
                    PackageInstaller.Session s = pi.openSession(id);
                    InputStream in = c.getInputStream();
                    OutputStream out = s.openWrite("LinkDesk.apk", 0, total > 0 ? total : -1);
                    byte[] buf = new byte[65536];
                    long done = 0;
                    int n, last = -1;
                    while ((n = in.read(buf)) > 0) {
                        out.write(buf, 0, n);
                        done += n;
                        if (total > 0) {
                            final int pct = (int) (done * 100 / total);
                            if (pct != last) {
                                last = pct;
                                main.post(new Runnable() { @Override public void run() { p.onProgress(pct); } });
                            }
                        }
                    }
                    s.fsync(out);
                    out.close();
                    in.close();
                    Intent cb = new Intent(ACTION_INSTALL).setPackage(ctx.getPackageName());
                    int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 31 ? PendingIntent.FLAG_MUTABLE : 0);
                    PendingIntent pend = PendingIntent.getBroadcast(ctx, id, cb, flags);
                    s.commit(pend.getIntentSender());
                    s.close();
                } catch (Exception e) {
                    Log.w(TAG, "install", e);
                    err = "Không tải được bản cập nhật: " + e.getMessage();
                }
                final String fe = err;
                main.post(new Runnable() { @Override public void run() { p.onDone(fe); } });
            }
        }, "update-install").start();
    }
}
