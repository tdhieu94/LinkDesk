package vn.linkdesk;

import android.content.Context;
import android.util.Log;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Mot ket noi WebSocket tu trinh duyet tren may tinh. */
public class WsConn {
    private static final String TAG = "LinkDesk.Ws";
    private final Context ctx;
    private final Socket socket;
    private final InputStream in;
    private final OutputStream out;
    private final Object writeLock = new Object();
    private volatile boolean open = true;
    /** May tinh nay muon nghe am thanh dien thoai. */
    public volatile boolean wantAudio;
    /** May tinh nay giai ma duoc H.264. */
    public volatile boolean wantH264;
    /** May tinh dang hien man hinh; false = khong gui hinh de tiet kiem pin va mang. */
    public volatile boolean viewing = true;
    private final java.util.ArrayDeque<byte[]> videoQ = new java.util.ArrayDeque<byte[]>();
    private int videoBytes;
    private boolean needKey = true;
    // Hang doi am thanh nho: neu mang cham thi bo khung cu, giu do tre thap
    private final java.util.concurrent.ArrayBlockingQueue<byte[]> audioQ = new java.util.concurrent.ArrayBlockingQueue<byte[]>(12);
    // Gui tin nhan chu tren luong rieng (khong bao gio ghi socket tren luong chinh cua Android)
    private final ExecutorService textOut = Executors.newSingleThreadExecutor();

    public WsConn(Context ctx, Socket s, InputStream in, OutputStream out) {
        this.ctx = ctx;
        this.socket = s;
        this.in = in;
        this.out = out;
    }

    public void run() {
        Hub.addClient(this);
        Thread sender = new Thread(new Runnable() {
            @Override public void run() { frameLoop(); }
        }, "ws-frames");
        sender.setDaemon(true);
        sender.start();
        Thread audio = new Thread(new Runnable() {
            @Override public void run() { audioLoop(); }
        }, "ws-audio");
        audio.setDaemon(true);
        audio.start();
        Thread video = new Thread(new Runnable() {
            @Override public void run() { videoLoop(); }
        }, "ws-video");
        video.setDaemon(true);
        video.start();
        try {
            Commands.sendHello(ctx, this);
            readLoop();
        } catch (Exception e) {
            Log.d(TAG, "closed: " + e);
        } finally {
            close();
            Hub.removeClient(this);
        }
    }

    public boolean isOpen() { return open; }

    public void close() {
        open = false;
        textOut.shutdownNow();
        try { socket.close(); } catch (IOException ignored) {}
    }

    private void frameLoop() {
        long seq = 0;
        try {
            while (open) {
                Object[] f = Hub.waitFrame(seq, 1000);
                if (f == null) continue;
                seq = (Long) f[1];
                if (viewing && !wantH264Active()) sendFrame(2, (byte[]) f[0]);
            }
        } catch (Exception e) {
            close();
        }
    }

    /** Khung H.264 theo dung thu tu; mang cham thi bo het va doi khung khoa moi. */
    public void sendVideo(byte[] p, boolean key) {
        if (!open) return;
        synchronized (videoQ) {
            if (!viewing) { needKey = true; return; }
            if (needKey && !key) { Hub.requestKeyFrame(); return; }
            if (videoBytes + p.length > 4 * 1024 * 1024 || videoQ.size() > 90) {
                videoQ.clear(); videoBytes = 0;
                if (!key) { needKey = true; Hub.requestKeyFrame(); return; }
            }
            needKey = false;
            videoQ.add(p); videoBytes += p.length;
            videoQ.notifyAll();
        }
    }

    private boolean wantH264Active() {
        MirrorService m = Hub.mirror;
        return wantH264 && m != null && m.isH264();
    }

    public void resetVideo() {
        synchronized (videoQ) { videoQ.clear(); videoBytes = 0; needKey = true; }
    }

    private void videoLoop() {
        try {
            while (open) {
                byte[] p;
                synchronized (videoQ) {
                    if (videoQ.isEmpty()) videoQ.wait(1000);
                    p = videoQ.poll();
                    if (p != null) videoBytes -= p.length;
                }
                if (p != null) sendFrame(2, p);
            }
        } catch (Exception e) {
            close();
        }
    }

    public void sendAudio(byte[] f) {
        if (!open) return;
        while (!audioQ.offer(f)) audioQ.poll();
    }

    private void audioLoop() {
        try {
            while (open) {
                byte[] f = audioQ.poll(1, java.util.concurrent.TimeUnit.SECONDS);
                if (f != null) sendFrame(2, f);
            }
        } catch (Exception e) {
            close();
        }
    }

    public void sendText(String s) {
        if (!open) return;
        final byte[] data = s.getBytes(StandardCharsets.UTF_8);
        try {
            textOut.execute(new Runnable() {
                @Override public void run() {
                    try {
                        sendFrame(1, data);
                    } catch (Exception e) {
                        close();
                    }
                }
            });
        } catch (Exception ignored) {
            // da dong
        }
    }

    private void sendFrame(int opcode, byte[] data) throws IOException {
        if (!open) throw new IOException("closed");
        int len = data.length;
        byte[] h;
        if (len < 126) {
            h = new byte[]{(byte) (0x80 | opcode), (byte) len};
        } else if (len < 65536) {
            h = new byte[]{(byte) (0x80 | opcode), 126, (byte) (len >> 8), (byte) len};
        } else {
            h = new byte[10];
            h[0] = (byte) (0x80 | opcode);
            h[1] = 127;
            long l = len;
            for (int i = 0; i < 8; i++) h[9 - i] = (byte) (l >> (8 * i));
        }
        synchronized (writeLock) {
            out.write(h);
            out.write(data);
            out.flush();
        }
    }

    private int readByte() throws IOException {
        int b = in.read();
        if (b < 0) throw new IOException("eof");
        return b;
    }

    private void readFully(byte[] b) throws IOException {
        int off = 0;
        while (off < b.length) {
            int n = in.read(b, off, b.length - off);
            if (n < 0) throw new IOException("eof");
            off += n;
        }
    }

    private void readLoop() throws IOException {
        ByteArrayOutputStream msg = new ByteArrayOutputStream();
        int msgOpcode = 0;
        while (open) {
            int b0 = readByte();
            int b1 = readByte();
            boolean fin = (b0 & 0x80) != 0;
            int opcode = b0 & 0x0f;
            boolean masked = (b1 & 0x80) != 0;
            long len = b1 & 0x7f;
            if (len == 126) {
                len = (readByte() << 8) | readByte();
            } else if (len == 127) {
                len = 0;
                for (int i = 0; i < 8; i++) len = (len << 8) | readByte();
            }
            if (len > 4 * 1024 * 1024) throw new IOException("frame too big");
            byte[] mask = new byte[4];
            if (masked) readFully(mask);
            byte[] payload = new byte[(int) len];
            readFully(payload);
            if (masked) for (int i = 0; i < payload.length; i++) payload[i] ^= mask[i & 3];

            if (opcode == 8) { // close
                try { sendFrame(8, new byte[0]); } catch (IOException ignored) {}
                return;
            } else if (opcode == 9) { // ping
                sendFrame(10, payload);
                continue;
            } else if (opcode == 10) {
                continue;
            }
            if (opcode != 0) { msgOpcode = opcode; msg.reset(); }
            msg.write(payload);
            if (!fin) continue;
            if (msgOpcode == 1) {
                String text = new String(msg.toByteArray(), StandardCharsets.UTF_8);
                try {
                    Commands.handle(ctx, this, new JSONObject(text));
                } catch (Exception e) {
                    Log.w(TAG, "command failed: " + text, e);
                }
            }
            msg.reset();
        }
    }
}
