package vn.linkdesk;

import android.Manifest;
import android.content.ContentResolver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.OpenableColumns;
import android.provider.Settings;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.Comparator;
import java.util.HashMap;
import java.util.Map;

/** Duyet va tai tep tu dien thoai; tep chia se "Gui sang may tinh". */
public final class FilesApi {
    private FilesApi() {}

    public static boolean handles(String p) { return p.equals("/files") || p.equals("/file") || p.equals("/shared"); }

    public static boolean canReadAll(Context c) {
        if (Build.VERSION.SDK_INT >= 30) return Environment.isExternalStorageManager();
        return c.checkSelfPermission(Manifest.permission.READ_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED;
    }

    public static Intent permissionIntent(Context c) {
        if (Build.VERSION.SDK_INT >= 30) {
            try {
                return new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, Uri.parse("package:" + c.getPackageName()));
            } catch (Exception e) { return new Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION); }
        }
        return null;
    }

    static File root() { return Environment.getExternalStorageDirectory(); }

    /** Duong dan tuong doi -> File nam trong bo nho trong; null neu ra ngoai. */
    static File resolve(String rel) throws IOException {
        File r = root().getCanonicalFile();
        File f = new File(r, rel == null ? "" : rel).getCanonicalFile();
        if (!f.getPath().equals(r.getPath()) && !f.getPath().startsWith(r.getPath() + File.separator)) return null;
        return f;
    }

    public static void serve(Context c, String path, Map<String, String> q, OutputStream out) throws IOException {
        try {
            if (path.equals("/shared")) { shared(c, q.get("id"), out); return; }
            if (!canReadAll(c)) {
                MediaApi.send(out, 403, "text/plain; charset=utf-8", "Điện thoại chưa cho phép truy cập tệp. Mở app LinkDesk → mục Quyền → \"Cho phép truy cập tệp\".".getBytes(StandardCharsets.UTF_8), false);
                return;
            }
            File f = resolve(q.get("path"));
            if (f == null || !f.exists()) { MediaApi.send(out, 404, "text/plain; charset=utf-8", "Không tìm thấy".getBytes(StandardCharsets.UTF_8), false); return; }
            if (path.equals("/files")) list(f, out);
            else stream(new FileInputStream(f), f.getName(), f.length(), out);
        } catch (Exception e) {
            MediaApi.send(out, 500, "text/plain; charset=utf-8", String.valueOf(e.getMessage()).getBytes(StandardCharsets.UTF_8), false);
        }
    }

    private static void list(File dir, OutputStream out) throws Exception {
        File[] fs = dir.listFiles();
        if (fs == null) fs = new File[0];
        Arrays.sort(fs, new Comparator<File>() {
            @Override public int compare(File a, File b) {
                if (a.isDirectory() != b.isDirectory()) return a.isDirectory() ? -1 : 1;
                return a.getName().compareToIgnoreCase(b.getName());
            }
        });
        String rootPath = root().getCanonicalPath();
        String rel = dir.getCanonicalPath().substring(rootPath.length());
        if (rel.startsWith("/")) rel = rel.substring(1);
        JSONArray arr = new JSONArray();
        for (File f : fs) {
            if (f.getName().startsWith(".")) continue;
            arr.put(new JSONObject().put("name", f.getName()).put("dir", f.isDirectory())
                    .put("size", f.isDirectory() ? 0 : f.length()).put("time", f.lastModified()));
        }
        JSONObject o = new JSONObject().put("path", rel).put("items", arr);
        MediaApi.send(out, 200, "application/json; charset=utf-8", o.toString().getBytes(StandardCharsets.UTF_8), false);
    }

    static void stream(InputStream in, String name, long len, OutputStream out) throws IOException {
        StringBuilder h = new StringBuilder("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream");
        if (len >= 0) h.append("\r\nContent-Length: ").append(len);
        h.append("\r\nX-File-Name: ").append(URLEncoder.encode(name, "UTF-8"));
        h.append("\r\nConnection: close\r\n\r\n");
        out.write(h.toString().getBytes(StandardCharsets.US_ASCII));
        byte[] buf = new byte[65536];
        int n;
        try { while ((n = in.read(buf)) > 0) out.write(buf, 0, n); } finally { in.close(); }
        out.flush();
    }

    // ---- tep duoc chia se tu menu "Chia se" cua dien thoai
    private static final Map<String, Uri> sharedUris = new HashMap<>();
    private static int nextId = 1;

    public static String addShared(Uri u) {
        synchronized (sharedUris) { String id = String.valueOf(nextId++); sharedUris.put(id, u); return id; }
    }

    public static String displayName(Context c, Uri u) {
        String name = null;
        long size = -1;
        try (Cursor cur = c.getContentResolver().query(u, new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE}, null, null, null)) {
            if (cur != null && cur.moveToFirst()) { name = cur.getString(0); }
        } catch (Exception ignored) {}
        if (name == null) name = u.getLastPathSegment();
        return name == null ? "tep" : name;
    }

    public static long size(Context c, Uri u) {
        try (Cursor cur = c.getContentResolver().query(u, new String[]{OpenableColumns.SIZE}, null, null, null)) {
            if (cur != null && cur.moveToFirst() && !cur.isNull(0)) return cur.getLong(0);
        } catch (Exception ignored) {}
        return -1;
    }

    private static void shared(Context c, String id, OutputStream out) throws IOException {
        Uri u;
        synchronized (sharedUris) { u = sharedUris.get(id); }
        if (u == null) { MediaApi.send(out, 404, "text/plain", new byte[0], false); return; }
        ContentResolver cr = c.getContentResolver();
        InputStream in = cr.openInputStream(u);
        if (in == null) { MediaApi.send(out, 404, "text/plain", new byte[0], false); return; }
        stream(in, displayName(c, u), size(c, u), out);
    }
}
