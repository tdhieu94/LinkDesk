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
        return path.equals("/photos") || path.equals("/thumb") || path.equals("/photo") || path.equals("/icon") || path.equals("/wallpaper") || path.equals("/mediaart") || path.equals("/apps");
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
            if (path.equals("/apps")) { apps(c, out); return; }
            if (path.equals("/wallpaper")) { wallpaper(c, out); return; }
            if (path.equals("/mediaart")) { byte[] a = MediaCtl.art(); if (a == null) send(out, 404, "text/plain", new byte[0], false); else send(out, 200, "image/jpeg", a, true); return; }
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

    /** Cac app co bieu tuong tren man hinh chinh: [{pkg, name}], xep theo ten. */
    private static void apps(Context c, OutputStream out) throws Exception {
        android.content.pm.PackageManager pm = c.getPackageManager();
        android.content.Intent i = new android.content.Intent(android.content.Intent.ACTION_MAIN).addCategory(android.content.Intent.CATEGORY_LAUNCHER);
        java.util.List<android.content.pm.ResolveInfo> l = pm.queryIntentActivities(i, 0);
        java.util.TreeMap<String, String> sorted = new java.util.TreeMap<String, String>(String.CASE_INSENSITIVE_ORDER);
        java.util.HashSet<String> seen = new java.util.HashSet<String>();
        for (android.content.pm.ResolveInfo r : l) {
            String pkg = r.activityInfo.packageName;
            if (pkg.equals(c.getPackageName()) || !seen.add(pkg)) continue;
            String name = String.valueOf(r.loadLabel(pm));
            sorted.put(name + "\u0000" + pkg, pkg);
        }
        org.json.JSONArray a = new org.json.JSONArray();
        for (java.util.Map.Entry<String, String> e : sorted.entrySet()) {
            String k = e.getKey();
            a.put(new org.json.JSONObject().put("pkg", e.getValue()).put("name", k.substring(0, k.indexOf('\u0000'))));
        }
        send(out, 200, "application/json; charset=utf-8", a.toString().getBytes(StandardCharsets.UTF_8), false);
    }

    // ---- bao may tinh khi co anh moi (de tu sao luu)
    private static android.database.ContentObserver photoObserver;

    public static void watchPhotos(Context c) {
        if (photoObserver != null) return;
        final android.os.Handler h = new android.os.Handler(android.os.Looper.getMainLooper());
        final Runnable fire = new Runnable() {
            @Override public void run() {
                if (Hub.clientCount() == 0) return;
                try { Hub.broadcast(new JSONObject().put("t", "photosChanged")); } catch (Exception ignored) {}
            }
        };
        photoObserver = new android.database.ContentObserver(h) {
            @Override public void onChange(boolean self) { h.removeCallbacks(fire); h.postDelayed(fire, 4000); }
        };
        try { c.getContentResolver().registerContentObserver(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, true, photoObserver); }
        catch (Exception e) { photoObserver = null; }
    }

    /** Mo app tren dien thoai. Tra ve loi hoac null. */
    public static String launch(Context c, String pkg) {
        try {
            android.content.Intent i = c.getPackageManager().getLaunchIntentForPackage(pkg);
            if (i == null) return "Không mở được ứng dụng này.";
            i.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK | android.content.Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED);
            // Mo tu dich vu Tro nang de Android cho phep mo app khi LinkDesk chay nen
            Context starter = Hub.control != null ? Hub.control : c;
            starter.startActivity(i);
            return null;
        } catch (Exception e) {
            return "Không mở được: " + e.getMessage();
        }
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

    private static byte[] wallCache;

    /** Hinh nen dien thoai (anh nho). Android 13+ thuong khong cho doc -> ve nen chuyen mau theo mau hinh nen. */
    private static void wallpaper(Context c, OutputStream out) throws IOException {
        byte[] body = wallCache;
        if (body == null) {
            int w = 360, h = 780;
            Bitmap b = null;
            android.app.WallpaperManager wm = android.app.WallpaperManager.getInstance(c);
            try {
                Drawable d = wm.getDrawable();
                if (d != null) {
                    int iw = Math.max(1, d.getIntrinsicWidth()), ih = Math.max(1, d.getIntrinsicHeight());
                    float sc = Math.max((float) w / iw, (float) h / ih);
                    b = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
                    Canvas cv = new Canvas(b);
                    int dw = (int) (iw * sc), dh = (int) (ih * sc);
                    d.setBounds((w - dw) / 2, (h - dh) / 2, (w - dw) / 2 + dw, (h - dh) / 2 + dh);
                    d.draw(cv);
                }
            } catch (Throwable ignored) { b = null; }
            if (b == null) {
                int c1 = 0xFF3B4A6B, c2 = 0xFF151A24;
                try {
                    if (Build.VERSION.SDK_INT >= 27) {
                        android.app.WallpaperColors wc = wm.getWallpaperColors(android.app.WallpaperManager.FLAG_SYSTEM);
                        if (wc != null) {
                            c1 = wc.getPrimaryColor().toArgb();
                            c2 = wc.getSecondaryColor() != null ? wc.getSecondaryColor().toArgb() : darker(c1);
                        }
                    }
                } catch (Throwable ignored) {}
                b = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
                Canvas cv = new Canvas(b);
                android.graphics.Paint p = new android.graphics.Paint();
                p.setShader(new android.graphics.LinearGradient(0, 0, w, h, c1, c2, android.graphics.Shader.TileMode.CLAMP));
                cv.drawRect(0, 0, w, h, p);
            }
            ByteArrayOutputStream bo = new ByteArrayOutputStream();
            b.compress(Bitmap.CompressFormat.JPEG, 85, bo);
            body = bo.toByteArray();
            wallCache = body;
        }
        send(out, 200, "image/jpeg", body, false);
    }

    private static int darker(int c) {
        return 0xFF000000 | (((c >> 16 & 0xFF) * 6 / 10) << 16) | (((c >> 8 & 0xFF) * 6 / 10) << 8) | ((c & 0xFF) * 6 / 10);
    }

    static void send(OutputStream out, int code, String type, byte[] body, boolean cache) throws IOException {
        String status = code == 200 ? "OK" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : code == 500 ? "Internal Server Error" : "Error";
        String h = "HTTP/1.1 " + code + " " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.length
                + (cache ? "\r\nCache-Control: max-age=86400" : "\r\nCache-Control: no-cache")
                + "\r\nConnection: close\r\n\r\n";
        out.write(h.getBytes(StandardCharsets.US_ASCII));
        out.write(body);
        out.flush();
    }
}
