package vn.linkdesk;

import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.MediaStore;
import android.util.Base64;
import android.util.Log;

import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;

/** May chu HTTP + WebSocket toi gian, khong dung thu vien ngoai. */
public class WebServer {
    private static final String TAG = "LinkDesk.Web";
    private static final String WS_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private static final int MAX_FAILS = 5;
    private static final long LOCK_MS = 60_000;

    private final Context ctx;
    private ServerSocket server;
    private volatile boolean running;
    private final Map<String, long[]> fails = new HashMap<>(); // ip -> {count, lockUntil}

    public WebServer(Context ctx) {
        this.ctx = ctx.getApplicationContext();
    }

    public void start() throws IOException {
        server = new ServerSocket();
        server.setReuseAddress(true);
        server.bind(new InetSocketAddress(Hub.PORT));
        running = true;
        Thread t = new Thread(new Runnable() {
            @Override public void run() { acceptLoop(); }
        }, "web-accept");
        t.setDaemon(true);
        t.start();
    }

    public void stop() {
        running = false;
        try { if (server != null) server.close(); } catch (IOException ignored) {}
        Hub.closeAllClients();
    }

    private void acceptLoop() {
        while (running) {
            try {
                final Socket s = server.accept();
                Thread t = new Thread(new Runnable() {
                    @Override public void run() { handle(s); }
                }, "web-conn");
                t.setDaemon(true);
                t.start();
            } catch (IOException e) {
                if (running) Log.w(TAG, "accept", e);
            }
        }
    }

    // ---------------------------------------------------------------- HTTP

    private static String readLine(InputStream in) throws IOException {
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        int c;
        while ((c = in.read()) != -1) {
            if (c == '\n') break;
            if (c != '\r') b.write(c);
            if (b.size() > 8192) throw new IOException("line too long");
        }
        if (c == -1 && b.size() == 0) return null;
        return new String(b.toByteArray(), StandardCharsets.UTF_8);
    }

    private static Map<String, String> parseQuery(String q) {
        Map<String, String> m = new HashMap<>();
        if (q == null) return m;
        for (String part : q.split("&")) {
            int i = part.indexOf('=');
            try {
                if (i < 0) m.put(URLDecoder.decode(part, "UTF-8"), "");
                else m.put(URLDecoder.decode(part.substring(0, i), "UTF-8"),
                        URLDecoder.decode(part.substring(i + 1), "UTF-8"));
            } catch (Exception ignored) {}
        }
        return m;
    }

    private void handle(Socket s) {
        String ip = s.getInetAddress().getHostAddress();
        try {
            s.setTcpNoDelay(true);
            s.setSoTimeout(30_000);
            InputStream in = new BufferedInputStream(s.getInputStream(), 16384);
            OutputStream out = s.getOutputStream();

            String reqLine = readLine(in);
            if (reqLine == null) { s.close(); return; }
            String[] parts = reqLine.split(" ");
            if (parts.length < 2) { s.close(); return; }
            String method = parts[0];
            String target = parts[1];
            Map<String, String> headers = new HashMap<>();
            String line;
            int count = 0;
            while ((line = readLine(in)) != null && !line.isEmpty()) {
                int i = line.indexOf(':');
                if (i > 0) headers.put(line.substring(0, i).trim().toLowerCase(Locale.ROOT), line.substring(i + 1).trim());
                if (++count > 100) throw new IOException("too many headers");
            }
            String path = target;
            String query = null;
            int qi = target.indexOf('?');
            if (qi >= 0) { path = target.substring(0, qi); query = target.substring(qi + 1); }
            Map<String, String> q = parseQuery(query);

            if (method.equals("GET") && (path.equals("/") || path.equals("/index.html"))) {
                sendAsset(out, "index.html", "text/html; charset=utf-8");
            } else if (method.equals("GET") && path.equals("/check")) {
                int r = checkPin(ip, q.get("pin"));
                if (r == 0) sendText(out, 200, "OK");
                else if (r == 2) sendText(out, 429, "Nhập sai quá nhiều lần, thử lại sau 1 phút");
                else sendText(out, 401, "Sai mã PIN");
            } else if (method.equals("GET") && path.equals("/ws")) {
                int r = checkPin(ip, q.get("pin"));
                if (r != 0) { sendText(out, r == 2 ? 429 : 401, "Unauthorized"); return; }
                String key = headers.get("sec-websocket-key");
                if (key == null) { sendText(out, 400, "Bad request"); return; }
                String accept = Base64.encodeToString(
                        MessageDigest.getInstance("SHA-1").digest((key + WS_GUID).getBytes(StandardCharsets.US_ASCII)),
                        Base64.NO_WRAP);
                String resp = "HTTP/1.1 101 Switching Protocols\r\n"
                        + "Upgrade: websocket\r\nConnection: Upgrade\r\n"
                        + "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
                out.write(resp.getBytes(StandardCharsets.US_ASCII));
                out.flush();
                s.setSoTimeout(0);
                new WsConn(ctx, s, in, out).run(); // chay den khi dong
                return;
            } else if (method.equals("POST") && path.equals("/upload")) {
                int r = checkPin(ip, q.get("pin"));
                if (r != 0) { drain(in, headers); sendText(out, 401, "Sai mã PIN"); return; }
                handleUpload(in, out, headers, q.get("name"));
            } else if (method.equals("GET") && FilesApi.handles(path)) {
                int r = checkPin(ip, q.get("pin"));
                if (r != 0) { sendText(out, r == 2 ? 429 : 401, "Sai mã PIN"); s.close(); return; }
                s.setSoTimeout(300_000);
                FilesApi.serve(ctx, path, q, out);
            } else if (method.equals("GET") && MediaApi.handles(path)) {
                int r = checkPin(ip, q.get("pin"));
                if (r != 0) { sendText(out, r == 2 ? 429 : 401, "Sai mã PIN"); s.close(); return; }
                s.setSoTimeout(120_000);
                MediaApi.serve(ctx, path, q, out);
            } else if (method.equals("GET") && path.equals("/favicon.ico")) {
                sendText(out, 404, "");
            } else {
                sendText(out, 404, "Not found");
            }
            s.close();
        } catch (Exception e) {
            Log.w(TAG, "handle", e);
            try { s.close(); } catch (IOException ignored) {}
        }
    }

    /** 0 = dung, 1 = sai, 2 = dang bi khoa tam. */
    private int checkPin(String ip, String pin) {
        synchronized (fails) {
            long now = System.currentTimeMillis();
            long[] f = fails.get(ip);
            if (f != null && f[1] > now) return 2;
            String real = Prefs.getPin(ctx);
            if (pin != null && MessageDigest.isEqual(pin.getBytes(StandardCharsets.UTF_8), real.getBytes(StandardCharsets.UTF_8))) {
                fails.remove(ip);
                return 0;
            }
            if (f == null) { f = new long[]{0, 0}; fails.put(ip, f); }
            f[0]++;
            if (f[0] >= MAX_FAILS) { f[0] = 0; f[1] = now + LOCK_MS; }
            return 1;
        }
    }

    private void sendAsset(OutputStream out, String name, String type) throws IOException {
        InputStream a = ctx.getAssets().open(name);
        ByteArrayOutputStream b = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = a.read(buf)) > 0) b.write(buf, 0, n);
        a.close();
        byte[] body = b.toByteArray();
        String h = "HTTP/1.1 200 OK\r\nContent-Type: " + type + "\r\nContent-Length: " + body.length
                + "\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n";
        out.write(h.getBytes(StandardCharsets.US_ASCII));
        out.write(body);
        out.flush();
    }

    private static void sendText(OutputStream out, int code, String msg) throws IOException {
        byte[] body = msg.getBytes(StandardCharsets.UTF_8);
        String status;
        switch (code) {
            case 200: status = "OK"; break;
            case 400: status = "Bad Request"; break;
            case 401: status = "Unauthorized"; break;
            case 404: status = "Not Found"; break;
            case 413: status = "Payload Too Large"; break;
            case 429: status = "Too Many Requests"; break;
            default: status = "Error";
        }
        String h = "HTTP/1.1 " + code + " " + status + "\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: "
                + body.length + "\r\nConnection: close\r\n\r\n";
        out.write(h.getBytes(StandardCharsets.US_ASCII));
        out.write(body);
        out.flush();
    }

    private static void drain(InputStream in, Map<String, String> headers) {
        try {
            long len = Long.parseLong(headers.containsKey("content-length") ? headers.get("content-length") : "0");
            byte[] buf = new byte[8192];
            while (len > 0) {
                int n = in.read(buf, 0, (int) Math.min(buf.length, len));
                if (n < 0) break;
                len -= n;
            }
        } catch (Exception ignored) {}
    }

    private static String safeName(String name) {
        if (name == null || name.trim().isEmpty()) name = "tep-" + System.currentTimeMillis();
        name = name.replaceAll("[\\\\/:*?\"<>|\\x00-\\x1f]", "_").trim();
        if (name.startsWith(".")) name = "_" + name;
        if (name.length() > 150) name = name.substring(name.length() - 150);
        return name;
    }

    private void handleUpload(InputStream in, OutputStream out, Map<String, String> headers, String rawName) throws IOException {
        String cl = headers.get("content-length");
        if (cl == null) { sendText(out, 400, "Thiếu Content-Length"); return; }
        long len = Long.parseLong(cl);
        String name = safeName(rawName);
        OutputStream fo;
        Uri uri = null;
        ContentResolver cr = ctx.getContentResolver();
        if (Build.VERSION.SDK_INT >= 29) {
            ContentValues v = new ContentValues();
            v.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
            v.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/LinkDesk");
            v.put(MediaStore.MediaColumns.IS_PENDING, 1);
            uri = cr.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, v);
            if (uri == null) { drain(in, headers); sendText(out, 500, "Không tạo được tệp"); return; }
            fo = cr.openOutputStream(uri);
        } else {
            File dir = new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "LinkDesk");
            //noinspection ResultOfMethodCallIgnored
            dir.mkdirs();
            fo = new FileOutputStream(new File(dir, name));
        }
        if (fo == null) { drain(in, headers); sendText(out, 500, "Không mở được tệp"); return; }
        byte[] buf = new byte[65536];
        long left = len;
        try {
            while (left > 0) {
                int n = in.read(buf, 0, (int) Math.min(buf.length, left));
                if (n < 0) throw new IOException("ket noi bi ngat");
                fo.write(buf, 0, n);
                left -= n;
            }
        } finally {
            fo.close();
        }
        if (uri != null) {
            ContentValues v = new ContentValues();
            v.put(MediaStore.MediaColumns.IS_PENDING, 0);
            cr.update(uri, v, null, null);
        }
        sendText(out, 200, "Download/LinkDesk/" + name);
    }
}
