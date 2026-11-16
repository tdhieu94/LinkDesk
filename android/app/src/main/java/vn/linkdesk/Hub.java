package vn.linkdesk;

import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/** Trang thai dung chung giua cac service (cung tien trinh). */
public final class Hub {
    private Hub() {}

    public static final int PORT = 8686;

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
        MirrorService m = mirror;
        if (m != null) m.onClientsChanged();
    }

    public static void removeClient(WsConn c) {
        synchronized (clients) { clients.remove(c); }
        MirrorService m = mirror;
        if (m != null) m.onClientsChanged();
    }

    public static int clientCount() {
        synchronized (clients) { return clients.size(); }
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
