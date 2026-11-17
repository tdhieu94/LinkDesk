package vn.linkdesk;

import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/** Trang thai dung chung giua cac service (cung tien trinh). */
public final class Hub {
    private Hub() {}

    public static final int PORT = 8686;
    /** Cong HTTPS/WSS (ma hoa). */
    public static final int TLS_PORT = 8688;

    public static volatile ControlService control;
    public static volatile NotifService notif;
    public static volatile MirrorService mirror;

    private static final List<WsConn> clients = new ArrayList<>();

    // Khung hinh moi nhat (da ma hoa san, kem header)
    private static final Object frameLock = new Object();
    private static byte[] frame;
    private static long frameSeq;

    public static void addClient(WsConn c) {
        synchronized (clients) { clients.add(c); }
        MediaCtl.reset();
        MirrorService m = mirror;
        if (m != null) m.onClientsChanged();
    }

    public static void removeClient(WsConn c) {
        synchronized (clients) { clients.remove(c); }
        MirrorService mm = mirror;
        if (c.wantAudio && mm != null) AudioStreamer.update(mm);
        MirrorService m = mirror;
        if (m != null) m.onClientsChanged();
    }

    public static int clientCount() {
        synchronized (clients) { return clients.size(); }
    }

    /** So may dang thuc su nhin man hinh (khong thu nho, dang o tab Man hinh). */
    public static int viewerCount() {
        int n = 0;
        for (WsConn c : snapshotClients()) if (c.viewing) n++;
        return n;
    }

    public static List<WsConn> snapshotClients() {
        synchronized (clients) { return new ArrayList<>(clients); }
    }

    public static void closeAllClients() {
        for (WsConn c : snapshotClients()) c.close();
    }

    public static void broadcast(JSONObject msg) {
        String s = msg.toString();
        for (WsConn c : snapshotClients()) c.sendText(s);
    }

    // ---------------------------------------------------------------- H.264
    private static volatile long h264FailedUntil;

    /** Co may dang xem va tat ca deu giai ma duoc H.264. */
    public static boolean allWantH264() {
        if (android.os.SystemClock.uptimeMillis() < h264FailedUntil) return false;
        List<WsConn> l = snapshotClients();
        if (l.isEmpty()) return false;
        for (WsConn c : l) if (!c.wantH264) return false;
        return true;
    }

    /** Loi H.264: dung che do anh 2 phut roi cho thu lai. */
    public static void setH264Failed() { h264FailedUntil = android.os.SystemClock.uptimeMillis() + 120_000; }
    public static void clearH264Failed() { h264FailedUntil = 0; }

    public static void publishVideo(byte[] p, boolean key) {
        for (WsConn c : snapshotClients()) if (c.wantH264) c.sendVideo(p, key);
    }

    public static void requestKeyFrame() {
        MirrorService m = mirror;
        if (m != null) m.requestKeyFrame();
    }

    public static void publishFrame(byte[] data) {
        synchronized (frameLock) {
            frame = data;
            frameSeq++;
            frameLock.notifyAll();
        }
    }

    /** Cho den khi co khung hinh moi hon lastSeq. Tra ve null neu het thoi gian. */
    public static Object[] waitFrame(long lastSeq, long timeoutMs) throws InterruptedException {
        synchronized (frameLock) {
            long end = System.currentTimeMillis() + timeoutMs;
            while (frameSeq <= lastSeq || frame == null) {
                long left = end - System.currentTimeMillis();
                if (left <= 0) return null;
                frameLock.wait(left);
            }
            return new Object[]{frame, frameSeq};
        }
    }

    public static void clearFrame() {
        synchronized (frameLock) { frame = null; }
    }

    public static void toastAll(String msg) {
        try {
            broadcast(new JSONObject().put("t", "toast").put("msg", msg));
        } catch (Exception ignored) {}
    }
}
