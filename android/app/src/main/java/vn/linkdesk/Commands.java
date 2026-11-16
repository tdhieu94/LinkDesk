package vn.linkdesk;

import android.accessibilityservice.AccessibilityService;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.media.AudioManager;
import android.os.BatteryManager;
import android.os.Build;

import org.json.JSONArray;
import org.json.JSONObject;

/** Xu ly lenh JSON tu trinh duyet. Toa do x,y la ti le 0..1 tren khung hinh. */
public final class Commands {
    private Commands() {}

    public static void sendHello(Context ctx, WsConn c) throws Exception {
        c.sendText(status(ctx).put("t", "hello").put("model", Build.MANUFACTURER + " " + Build.MODEL)
                .put("android", Build.VERSION.RELEASE).put("name", Discovery.deviceName(ctx)).toString());
        MirrorService m = Hub.mirror;
        if (m != null) m.requestFrame();
        NotifService n = Hub.notif;
        if (n != null) n.sendAll();
    }

    public static JSONObject status(Context ctx) throws Exception {
        JSONObject o = new JSONObject().put("t", "status")
                .put("control", Hub.control != null)
                .put("notif", Hub.notif != null)
                .put("photos", MediaApi.canReadPhotos(ctx))
                .put("files", FilesApi.canReadAll(ctx))
                .put("sms", PhoneApi.canSms(ctx))
                .put("calls", PhoneApi.canCalls(ctx))
                .put("mirror", Hub.mirror != null && Hub.mirror.isProjecting())
                .put("screenOn", MirrorService.isScreenOn(ctx))
                .put("keepAwake", Prefs.getKeepAwake(ctx))
                .put("dark", Prefs.getDark(ctx))
                .put("superDark", SecureSettings.canWrite(ctx) && Build.VERSION.SDK_INT >= 31)
                .put("appVersion", BuildInfo.versionName(ctx))
                .put("maxSize", Prefs.getMaxSize(ctx))
                .put("quality", Prefs.getQuality(ctx))
                .put("fps", Prefs.getFps(ctx))
                .put("audio", AudioStreamer.isRunning())
                .put("finding", FindPhone.isRinging())
                .put("dnd", Hub.notif != null && Hub.notif.isDnd())
                .put("callState", PhoneApi.callState)
                .put("codec", Hub.mirror != null && Hub.mirror.isH264() ? "h264" : "jpeg")
                .put("audioOk", AudioStreamer.supported() && AudioStreamer.permitted(ctx));
        try {
            Intent b = ctx.registerReceiver(null, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
            if (b != null) {
                int level = b.getIntExtra(BatteryManager.EXTRA_LEVEL, -1);
                int scale = b.getIntExtra(BatteryManager.EXTRA_SCALE, 100);
                int plugged = b.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0);
                o.put("battery", level * 100 / Math.max(1, scale)).put("charging", plugged != 0);
            }
        } catch (Exception ignored) {}
        return o;
    }

    public static void broadcastStatus(Context ctx) {
        if (Hub.clientCount() == 0) return;
        try { Hub.broadcast(status(ctx)); } catch (Exception ignored) {}
    }

    private static void toast(WsConn c, String msg) {
        try { c.sendText(new JSONObject().put("t", "toast").put("msg", msg).toString()); } catch (Exception ignored) {}
    }

    /** Mo link tren dien thoai (trinh duyet hoac app phu hop). */
    static String openUrl(Context ctx, String url) {
        url = url == null ? "" : url.trim();
        if (url.isEmpty()) return "Link trống.";
        if (!url.matches("(?i)^[a-z][a-z0-9+.-]*:.*")) url = "https://" + url;
        String scheme = url.substring(0, url.indexOf(':')).toLowerCase(java.util.Locale.ROOT);
        if (!scheme.equals("http") && !scheme.equals("https") && !scheme.equals("geo") && !scheme.equals("tel") && !scheme.equals("mailto"))
            return "Chỉ mở được link http, https, geo, tel, mailto.";
        try {
            Intent i = new Intent(Intent.ACTION_VIEW, android.net.Uri.parse(url));
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            Context starter = Hub.control != null ? Hub.control : ctx;
            starter.startActivity(i);
            if (Hub.control == null && Build.VERSION.SDK_INT >= 29) return "Nếu link không mở, hãy bật Trợ năng cho LinkDesk (Android chặn mở khi chạy nền).";
            return null;
        } catch (Exception e) {
            return "Không mở được link: " + e.getMessage();
        }
    }

    private static ControlService needControl(WsConn c) {
        ControlService s = Hub.control;
        if (s == null) toast(c, "Chưa bật quyền Trợ năng cho LinkDesk trên điện thoại nên chưa điều khiển được.");
        return s;
    }

    public static void handle(Context ctx, WsConn c, JSONObject m) throws Exception {
        String t = m.optString("t");
        int[] real = ScreenCapturer.realSize(ctx);
        float W = real[0], H = real[1];
        switch (t) {
            case "ping":
                c.sendText(new JSONObject().put("t", "pong").put("ts", m.opt("ts")).toString());
                return;
            case "status":
                c.sendText(status(ctx).toString());
                return;
            case "tap": {
                ControlService s = needControl(c);
                if (s != null) s.tap((float) m.getDouble("x") * W, (float) m.getDouble("y") * H, m.optLong("dur", 40));
                return;
            }
            case "swipe":
            case "drag": {
                ControlService s = needControl(c);
                if (s == null) return;
                JSONArray pts = m.getJSONArray("pts");
                int n = pts.length();
                if (n == 0) return;
                float[] xs = new float[n], ys = new float[n];
                for (int i = 0; i < n; i++) {
                    JSONArray p = pts.getJSONArray(i);
                    xs[i] = (float) p.getDouble(0) * W;
                    ys[i] = (float) p.getDouble(1) * H;
                }
                if (t.equals("drag")) s.holdThenDrag(xs, ys, m.optLong("hold", 600), m.optLong("dur", 300));
                else s.swipe(xs, ys, m.optLong("dur", 300));
                return;
            }
            case "scroll": {
                ControlService s = needControl(c);
                if (s != null) s.scroll((float) m.getDouble("x") * W, (float) m.getDouble("y") * H,
                        (float) m.getDouble("dy") * H, (int) H);
                return;
            }
            case "key":
                key(ctx, c, m.optString("k"));
                return;
            case "text": {
                ControlService s = needControl(c);
                if (s == null) return;
                String err = s.typeText(m.optString("s"));
                if (err != null) toast(c, err);
                return;
            }
            case "edit": {
                ControlService s = needControl(c);
                if (s == null) return;
                String r = s.editKey(m.optString("k"));
                if (r != null && r.startsWith("COPY:")) {
                    c.sendText(new JSONObject().put("t", "copied").put("s", r.substring(5)).toString());
                } else if (r != null) {
                    toast(c, r);
                }
                return;
            }
            case "clip": {
                ControlService s = Hub.control;
                if (s != null) { s.setClipboard(m.optString("s")); if (!m.optBoolean("quiet")) toast(c, "Đã chép vào bộ nhớ tạm của điện thoại"); }
                else if (!m.optBoolean("quiet")) toast(c, "Cần bật Trợ năng để chép vào điện thoại.");
                return;
            }
            case "notifOpen":
            case "notifDismiss":
            case "notifReply":
            case "notifRefresh": {
                NotifService ns = Hub.notif;
                if (ns == null) { toast(c, "Chưa bật quyền đọc thông báo cho LinkDesk."); return; }
                String key = m.optString("key");
                String err = null;
                if (t.equals("notifOpen")) err = ns.open(key);
                else if (t.equals("notifDismiss")) ns.dismiss(key);
                else if (t.equals("notifReply")) { err = ns.reply(key, m.optString("s")); if (err == null) toast(c, "Đã gửi trả lời"); }
                else ns.sendAll();
                if (err != null) toast(c, err);
                return;
            }
            case "smsSend": {
                String err = PhoneApi.sendSms(ctx, m.optString("to"), m.optString("s"));
                toast(c, err != null ? err : "Đã gửi tin nhắn");
                return;
            }
            case "call": {
                String err = PhoneApi.call(ctx, m.optString("number"));
                toast(c, err != null ? err : "Đang gọi " + m.optString("number") + " trên điện thoại");
                return;
            }
            case "answerCall": {
                String err = PhoneApi.answerCall(ctx);
                if (err != null) toast(c, err);
                return;
            }
            case "findPhone":
                if (m.optBoolean("on", true)) FindPhone.start(ctx); else FindPhone.stop(ctx);
                return;
            case "dnd": {
                NotifService ns = Hub.notif;
                if (ns == null) { toast(c, "Cần bật quyền đọc thông báo cho LinkDesk để đổi Không làm phiền."); return; }
                String err = ns.setDnd(m.optBoolean("on"));
                if (err != null) toast(c, err);
                return;
            }
            case "endCall": {
                String err = PhoneApi.endCall(ctx);
                if (err != null) toast(c, err);
                return;
            }
            case "media": {
                String err = MediaCtl.action(ctx, m.optString("a"));
                if (err != null) toast(c, err);
                return;
            }
            case "video": {
                MirrorService ms = Hub.mirror;
                if (ms != null) ms.setVideo(m.optInt("maxSize", 1280), m.optInt("quality", 70), m.optInt("fps", 30));
                broadcastStatus(ctx);
                return;
            }
            case "keepAwake": {
                MirrorService ms = Hub.mirror;
                if (ms != null) ms.setKeepAwake(m.optBoolean("on"));
                else Prefs.setKeepAwake(ctx, m.optBoolean("on"));
                broadcastStatus(ctx);
                return;
            }
            case "dark": {
                MirrorService ms = Hub.mirror;
                if (ms != null) ms.setDark(m.optBoolean("on"));
                else Prefs.setDark(ctx, m.optBoolean("on"));
                if (m.optBoolean("on") && Hub.control == null) toast(c, "Cần bật Trợ năng cho LinkDesk để làm tối màn hình.");
                broadcastStatus(ctx);
                return;
            }
            case "audio": {
                c.wantAudio = m.optBoolean("on");
                String err = AudioStreamer.update(ctx);
                if (err != null) { c.wantAudio = false; toast(c, err); }
                c.sendText(status(ctx).toString());
                return;
            }
            case "codec": {
                c.wantH264 = m.optBoolean("h264");
                c.resetVideo();
                MirrorService ms = Hub.mirror;
                if (ms != null) ms.onClientsChanged();
                return;
            }
            case "launch": {
                String err = MediaApi.launch(ctx, m.optString("pkg"));
                if (err != null) toast(c, err);
                else if (Hub.control == null && Build.VERSION.SDK_INT >= 29) toast(c, "Nếu app không mở, hãy bật Trợ năng cho LinkDesk (Android chặn mở app khi chạy nền).");
                return;
            }
            case "openUrl": {
                String err = openUrl(ctx, m.optString("url"));
                if (err != null) toast(c, err);
                return;
            }
            case "view": {
                c.viewing = m.optBoolean("on", true);
                if (c.viewing) {
                    c.resetVideo();
                    MirrorService ms = Hub.mirror;
                    if (ms != null) ms.requestFrame();
                }
                return;
            }
            case "keyframe":
                c.resetVideo();
                Hub.requestKeyFrame();
                return;
            case "bankMode":
                bankMode(ctx);
                return;
            default:
                toast(c, "Lệnh không hỗ trợ: " + t);
        }
    }

    private static void key(Context ctx, WsConn c, String k) {
        AudioManager am = (AudioManager) ctx.getSystemService(Context.AUDIO_SERVICE);
        switch (k) {
            case "volup": am.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_RAISE, AudioManager.FLAG_SHOW_UI); return;
            case "voldown": am.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_LOWER, AudioManager.FLAG_SHOW_UI); return;
            case "mute": am.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_TOGGLE_MUTE, AudioManager.FLAG_SHOW_UI); return;
            case "wake": MirrorService.wakeScreen(ctx); return;
            default: break;
        }
        ControlService s = needControl(c);
        if (s == null) return;
        int a;
        switch (k) {
            case "back": a = AccessibilityService.GLOBAL_ACTION_BACK; break;
            case "home": a = AccessibilityService.GLOBAL_ACTION_HOME; break;
            case "recents": a = AccessibilityService.GLOBAL_ACTION_RECENTS; break;
            case "notifications": a = AccessibilityService.GLOBAL_ACTION_NOTIFICATIONS; break;
            case "quick": a = AccessibilityService.GLOBAL_ACTION_QUICK_SETTINGS; break;
            case "power": a = AccessibilityService.GLOBAL_ACTION_POWER_DIALOG; break;
            case "split": a = AccessibilityService.GLOBAL_ACTION_TOGGLE_SPLIT_SCREEN; break;
            case "lock":
                if (Build.VERSION.SDK_INT < 28) { toast(c, "Cần Android 9 trở lên"); return; }
                a = AccessibilityService.GLOBAL_ACTION_LOCK_SCREEN; break;
            case "screenshot":
                if (Build.VERSION.SDK_INT < 28) { toast(c, "Cần Android 9 trở lên"); return; }
                a = AccessibilityService.GLOBAL_ACTION_TAKE_SCREENSHOT; break;
            default:
                toast(c, "Phím không hỗ trợ: " + k);
                return;
        }
        s.global(a);
    }

    /** Tat het: dung chia se man hinh va tat Tro nang. */
    public static void bankMode(Context ctx) {
        Hub.toastAll("Đã bật chế độ ngân hàng: ngắt kết nối và tắt điều khiển.");
        ControlService s = Hub.control;
        if (s != null) s.turnOff();
        SecureSettings.setAccessibility(ctx, false);
        ctx.startService(new Intent(ctx, MirrorService.class).setAction(MirrorService.ACTION_STOP));
    }
}
