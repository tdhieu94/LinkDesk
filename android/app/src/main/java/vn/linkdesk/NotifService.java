package vn.linkdesk;

import android.app.ActivityOptions;
import android.app.Notification;
import android.app.PendingIntent;
import android.app.RemoteInput;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Parcelable;
import android.service.notification.NotificationListenerService;
import android.service.notification.StatusBarNotification;

import org.json.JSONArray;
import org.json.JSONObject;

/** Doc thong bao de hien tren may tinh (giong Phone Link). */
public class NotifService extends NotificationListenerService {

    @Override
    public void onListenerConnected() {
        Hub.notif = this;
        Commands.broadcastStatus(this);
        sendAll();
    }

    @Override
    public void onListenerDisconnected() {
        if (Hub.notif == this) Hub.notif = null;
        Commands.broadcastStatus(this);
    }

    @Override
    public void onDestroy() {
        if (Hub.notif == this) Hub.notif = null;
        super.onDestroy();
    }

    @Override
    public void onNotificationPosted(StatusBarNotification sbn) {
        JSONObject o = toJson(sbn);
        if (o == null) return;
        try {
            Hub.broadcast(new JSONObject().put("t", "notif").put("n", o));
        } catch (Exception ignored) {}
    }

    @Override
    public void onNotificationRemoved(StatusBarNotification sbn) {
        try {
            Hub.broadcast(new JSONObject().put("t", "notifRemoved").put("key", sbn.getKey()));
        } catch (Exception ignored) {}
    }

    public void sendAll() {
        try {
            JSONArray arr = new JSONArray();
            StatusBarNotification[] all = getActiveNotifications();
            if (all != null) for (StatusBarNotification s : all) {
                JSONObject o = toJson(s);
                if (o != null) arr.put(o);
            }
            Hub.broadcast(new JSONObject().put("t", "notifs").put("list", arr));
        } catch (Exception ignored) {}
    }

    private String appName(String pkg) {
        try {
            PackageManager pm = getPackageManager();
            ApplicationInfo ai = pm.getApplicationInfo(pkg, 0);
            return pm.getApplicationLabel(ai).toString();
        } catch (Exception e) {
            return pkg;
        }
    }

    private static String str(CharSequence c) { return c == null ? "" : c.toString(); }

    private JSONObject toJson(StatusBarNotification sbn) {
        try {
            if (sbn.getPackageName().equals(getPackageName())) return null;
            Notification n = sbn.getNotification();
            if ((n.flags & Notification.FLAG_GROUP_SUMMARY) != 0) return null;
            Bundle ex = n.extras;
            String title = str(ex.getCharSequence(Notification.EXTRA_TITLE));
            String text = str(ex.getCharSequence(Notification.EXTRA_BIG_TEXT));
            if (text.isEmpty()) text = str(ex.getCharSequence(Notification.EXTRA_TEXT));
            Parcelable[] msgs = ex.getParcelableArray(Notification.EXTRA_MESSAGES);
            if (msgs != null && msgs.length > 0) {
                StringBuilder sb = new StringBuilder();
                int from = Math.max(0, msgs.length - 5);
                for (int i = from; i < msgs.length; i++) {
                    if (!(msgs[i] instanceof Bundle)) continue;
                    Bundle m = (Bundle) msgs[i];
                    CharSequence sender = m.getCharSequence("sender");
                    CharSequence t = m.getCharSequence("text");
                    if (sb.length() > 0) sb.append('\n');
                    if (sender != null) sb.append(sender).append(": ");
                    sb.append(str(t));
                }
                if (sb.length() > 0) text = sb.toString();
            }
            if (title.isEmpty() && text.isEmpty()) return null;
            boolean ongoing = (n.flags & Notification.FLAG_ONGOING_EVENT) != 0;
            return new JSONObject()
                    .put("key", sbn.getKey())
                    .put("pkg", sbn.getPackageName())
                    .put("app", appName(sbn.getPackageName()))
                    .put("title", title)
                    .put("text", text.length() > 2000 ? text.substring(0, 2000) : text)
                    .put("time", sbn.getPostTime())
                    .put("ongoing", ongoing)
                    .put("clearable", sbn.isClearable())
                    .put("canOpen", n.contentIntent != null)
                    .put("canReply", findReply(n) != null);
        } catch (Exception e) {
            return null;
        }
    }

    private static Notification.Action findReply(Notification n) {
        if (n.actions == null) return null;
        for (Notification.Action a : n.actions) {
            RemoteInput[] ri = a.getRemoteInputs();
            if (ri != null && ri.length > 0 && a.actionIntent != null) return a;
        }
        return null;
    }

    private StatusBarNotification find(String key) {
        StatusBarNotification[] arr = getActiveNotifications(new String[]{key});
        return (arr != null && arr.length > 0) ? arr[0] : null;
    }

    public String reply(String key, String text) {
        StatusBarNotification s = find(key);
        if (s == null) return "Thông báo không còn nữa.";
        Notification.Action a = findReply(s.getNotification());
        if (a == null) return "Thông báo này không cho trả lời nhanh.";
        RemoteInput[] inputs = a.getRemoteInputs();
        Intent i = new Intent();
        Bundle b = new Bundle();
        for (RemoteInput ri : inputs) b.putCharSequence(ri.getResultKey(), text);
        RemoteInput.addResultsToIntent(inputs, i, b);
        try {
            a.actionIntent.send(this, 0, i);
            return null;
        } catch (PendingIntent.CanceledException e) {
            return "Gửi trả lời thất bại.";
        }
    }

    public String open(String key) {
        StatusBarNotification s = find(key);
        if (s == null) return "Thông báo không còn nữa.";
        PendingIntent pi = s.getNotification().contentIntent;
        if (pi == null) return "Thông báo này không mở được.";
        try {
            if (Build.VERSION.SDK_INT >= 34) {
                ActivityOptions o = ActivityOptions.makeBasic();
                o.setPendingIntentBackgroundActivityStartMode(ActivityOptions.MODE_BACKGROUND_ACTIVITY_START_ALLOWED);
                pi.send(this, 0, null, null, null, null, o.toBundle());
            } else {
                pi.send();
            }
            if ((s.getNotification().flags & Notification.FLAG_AUTO_CANCEL) != 0) cancelNotification(key);
            return null;
        } catch (PendingIntent.CanceledException e) {
            return "Không mở được.";
        }
    }

    public void dismiss(String key) {
        cancelNotification(key);
    }
}
