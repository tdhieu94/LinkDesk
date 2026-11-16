package vn.linkdesk;

import android.Manifest;
import android.content.ContentResolver;
import android.content.ContentUris;
import android.content.Context;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.drawable.BitmapDrawable;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import android.os.Build;
import android.provider.MediaStore;
import android.util.Size;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

/** Anh gan day (Phone Link "Anh") va bieu tuong ung dung cho may tinh. */
public final class MediaApi {
    private MediaApi() {}

    private static final Map<String, byte[]> iconCache = new HashMap<>();

    public static boolean handles(String path) {
        return path.equals("/photos") || path.equals("/thumb") || path.equals("/photo") || path.equals("/icon");
    }

    public static String photoPermission() {
        return Build.VERSION.SDK_INT >= 33 ? Manifest.permission.READ_MEDIA_IMAGES : Manifest.permission.READ_EXTERNAL_STORAGE;
    }

    public static boolean canReadPhotos(Context c) {
        return c.checkSelfPermission(photoPermission()) == PackageManager.PERMISSION_GRANTED
                || (Build.VERSION.SDK_INT >= 34 && c.checkSelfPermission("android.permission.READ_MEDIA_VISUAL_USER_SELECTED") == PackageManager.PERMISSION_GRANTED);
    }

    public static void serve(Context c, String path, Map<String, String> q, OutputStream out) throws IOException {
        try {
            if (path.equals("/icon")) { icon(c, q.get("pkg"), out); return; }
            if (!canReadPhotos(c)) { send(out, 403, "text/plain; charset=utf-8", "Chưa cho phép đọc ảnh".getBytes(StandardCharsets.UTF_8), false); return; }
            if (path.equals("/photos")) list(c, q, out);
            else if (path.equals("/thumb")) thumb(c, parseId(q.get("id")), out);
            else photo(c, parseId(q.get("id")), out);
        } catch (Exception e) {
            send(out, 500, "text/plain; charset=utf-8", String.valueOf(e.getMessage()).getBytes(StandardCharsets.UTF_8), false);
        }
    }

    private static long parseId(String s) {
        try { return Long.parseLong(s); } catch (Exception e) { return -1; }
    }

    private static void list(Context c, Map<String, String> q, OutputStream out) throws Exception {
        int limit = 60;
        try { limit = Math.max(1, Math.min(300, Integer.parseInt(q.get("limit")))); } catch (Exception ignored) {}
        String[] cols = {MediaStore.Images.Media._ID, MediaStore.Images.Media.DISPLAY_NAME, MediaStore.Images.Media.DATE_TAKEN,
                MediaStore.Images.Media.DATE_ADDED, MediaStore.Images.Media.SIZE, MediaStore.Images.Media.WIDTH, MediaStore.Images.Media.HEIGHT,
                MediaStore.Images.Media.MIME_TYPE};
        JSONArray arr = new JSONArray();
        Cursor cur = c.getContentResolver().query(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, cols, null, null,
                MediaStore.Images.Media.DATE_ADDED + " DESC");
        if (cur != null) {
            try {
                while (cur.moveToNext() && arr.length() < limit) {
                    long taken = cur.getLong(2);
                    if (taken == 0) taken = cur.getLong(3) * 1000L;
                    arr.put(new JSONObject()
                            .put("id", cur.getLong(0))
                            .put("name", cur.getString(1) == null ? "" : cur.getString(1))
                            .put("time", taken)
                            .put("size", cur.getLong(4))
                            .put("w", cur.getInt(5))
                            .put("h", cur.getInt(6))
                            .put("mime", cur.getString(7) == null ? "image/jpeg" : cur.getString(7)));
                }
            } finally { cur.close(); }
        }
        send(out, 200, "application/json; charset=utf-8", arr.toString().getBytes(StandardCharsets.UTF_8), false);
    }

    private static Uri uri(long id) {
        return ContentUris.withAppendedId(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, id);
    }

    private static void thumb(Context c, long id, OutputStream out) throws Exception {
        Bitmap b;
        if (Build.VERSION.SDK_INT >= 29) {
            b = c.getContentResolver().loadThumbnail(uri(id), new Size(320, 320), null);
        } else {
            BitmapFactory.Options o = new BitmapFactory.Options();
            o.inSampleSize = 8;
            InputStream in = c.getContentResolver().openInputStream(uri(id));
            b = BitmapFactory.decodeStream(in, null, o);
            if (in != null) in.close();
        }
        if (b == null) { send(out, 404, "text/plain", new byte[0], false); return; }
        ByteArrayOutputStream bo = new ByteArrayOutputStream();
        b.compress(Bitmap.CompressFormat.JPEG, 80, bo);
        send(out, 200, "image/jpeg", bo.toByteArray(), true);
    }

    private static void photo(Context c, long id, OutputStream out) throws Exception {
        ContentResolver cr = c.getContentResolver();
        String mime = cr.getType(uri(id));
        InputStream in = cr.openInputStream(uri(id));
        if (in == null) { send(out, 404, "text/plain", new byte[0], false); return; }
        long len = -1;
        Cursor cur = cr.query(uri(id), new String[]{MediaStore.Images.Media.SIZE}, null, null, null);
        if (cur != null) { if (cur.moveToFirst()) len = cur.getLong(0); cur.close(); }
        StringBuilder h = new StringBuilder("HTTP/1.1 200 OK\r\nContent-Type: ").append(mime == null ? "image/jpeg" : mime);
        if (len > 0) h.append("\r\nContent-Length: ").append(len);
        h.append("\r\nConnection: close\r\n\r\n");
        out.write(h.toString().getBytes(StandardCharsets.US_ASCII));
        byte[] buf = new byte[65536];
        int n;
        try { while ((n = in.read(buf)) > 0) out.write(buf, 0, n); } finally { in.close(); }
        out.flush();
    }

    private static void icon(Context c, String pkg, OutputStream out) throws IOException {
        if (pkg == null || pkg.isEmpty()) { send(out, 404, "text/plain", new byte[0], false); return; }
        byte[] png;
        synchronized (iconCache) { png = iconCache.get(pkg); }
        if (png == null) {
            try {
                Drawable d = c.getPackageManager().getApplicationIcon(pkg);
                Bitmap b;
                if (d instanceof BitmapDrawable && ((BitmapDrawable) d).getBitmap() != null) {
                    b = Bitmap.createScaledBitmap(((BitmapDrawable) d).getBitmap(), 96, 96, true);
                } else {
                    b = Bitmap.createBitmap(96, 96, Bitmap.Config.ARGB_8888);
                    Canvas cv = new Canvas(b);
                    d.setBounds(0, 0, 96, 96);
                    d.draw(cv);
                }
                ByteArrayOutputStream bo = new ByteArrayOutputStream();
                b.compress(Bitmap.CompressFormat.PNG, 100, bo);
                png = bo.toByteArray();
                synchronized (iconCache) { iconCache.put(pkg, png); }
            } catch (Exception e) {
                send(out, 404, "text/plain", new byte[0], false);
                return;
            }
        }
        send(out, 200, "image/png", png, true);
    }

    static void send(OutputStream out, int code, String type, byte[] body, boolean cache) throws IOException {
        String status = code == 200 ? "OK" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : "Error";
        String h = "HTTP/1.1 " + code + " " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.length
                + (cache ? "\r\nCache-Control: max-age=86400" : "\r\nCache-Control: no-cache")
                + "\r\nConnection: close\r\n\r\n";
        out.write(h.getBytes(StandardCharsets.US_ASCII));
        out.write(body);
        out.flush();
    }
}
