package vn.linkdesk;

import android.Manifest;
import android.content.ContentResolver;
import android.content.Context;
import android.content.pm.PackageManager;
import android.database.ContentObserver;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.CallLog;
import android.provider.ContactsContract;
import android.telecom.TelecomManager;
import android.telephony.SmsManager;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.Map;

/** Tin nhan SMS, nhat ky cuoc goi, danh ba (giong Phone Link). */
public final class PhoneApi {
    private PhoneApi() {}

    public static final String[] SMS_PERMS = {Manifest.permission.READ_SMS, Manifest.permission.SEND_SMS, Manifest.permission.RECEIVE_SMS, Manifest.permission.READ_CONTACTS};
    public static final String[] CALL_PERMS = {Manifest.permission.READ_CALL_LOG, Manifest.permission.CALL_PHONE, Manifest.permission.READ_CONTACTS};

    static boolean has(Context c, String p) { return c.checkSelfPermission(p) == PackageManager.PERMISSION_GRANTED; }
    public static boolean canSms(Context c) { return has(c, Manifest.permission.READ_SMS) && has(c, Manifest.permission.SEND_SMS); }
    public static boolean canCalls(Context c) { return has(c, Manifest.permission.READ_CALL_LOG) && has(c, Manifest.permission.CALL_PHONE); }

    public static boolean handles(String p) {
        return p.equals("/sms/threads") || p.equals("/sms/thread") || p.equals("/calls") || p.equals("/contacts");
    }

    public static void serve(Context c, String path, Map<String, String> q, OutputStream out) throws IOException {
        try {
            Object res;
            if (path.startsWith("/sms")) {
                if (!canSms(c)) { deny(out, "Điện thoại chưa cho phép đọc tin nhắn. Mở app LinkDesk → mục Quyền → \"Cho phép tin nhắn\"."); return; }
                res = path.equals("/sms/threads") ? threads(c) : thread(c, q.get("id"));
            } else if (path.equals("/calls")) {
                if (!has(c, Manifest.permission.READ_CALL_LOG)) { deny(out, "Điện thoại chưa cho phép đọc nhật ký cuộc gọi. Mở app LinkDesk → mục Quyền → \"Cho phép cuộc gọi\"."); return; }
                res = calls(c);
            } else {
                if (!has(c, Manifest.permission.READ_CONTACTS)) { deny(out, "Điện thoại chưa cho phép đọc danh bạ."); return; }
                res = contacts(c);
            }
            MediaApi.send(out, 200, "application/json; charset=utf-8", res.toString().getBytes(StandardCharsets.UTF_8), false);
        } catch (Exception e) {
            MediaApi.send(out, 500, "text/plain; charset=utf-8", String.valueOf(e.getMessage()).getBytes(StandardCharsets.UTF_8), false);
        }
    }

    private static void deny(OutputStream out, String msg) throws IOException {
        MediaApi.send(out, 403, "text/plain; charset=utf-8", msg.getBytes(StandardCharsets.UTF_8), false);
    }

    // ---- ten trong danh ba
    private static final Map<String, String> nameCache = new HashMap<>();

    static String name(Context c, String number) {
        if (number == null || number.isEmpty() || !has(c, Manifest.permission.READ_CONTACTS)) return "";
        synchronized (nameCache) { if (nameCache.containsKey(number)) return nameCache.get(number); }
        String n = "";
        try (Cursor cur = c.getContentResolver().query(Uri.withAppendedPath(ContactsContract.PhoneLookup.CONTENT_FILTER_URI, Uri.encode(number)),
                new String[]{ContactsContract.PhoneLookup.DISPLAY_NAME}, null, null, null)) {
            if (cur != null && cur.moveToFirst()) n = cur.getString(0);
        } catch (Exception ignored) {}
        if (n == null) n = "";
        synchronized (nameCache) { nameCache.put(number, n); }
        return n;
    }

    private static JSONArray threads(Context c) throws Exception {
        LinkedHashMap<Long, JSONObject> map = new LinkedHashMap<>();
        Map<Long, Integer> unread = new HashMap<>();
        try (Cursor cur = c.getContentResolver().query(Uri.parse("content://sms"),
                new String[]{"thread_id", "address", "body", "date", "read", "type"}, null, null, "date DESC")) {
            if (cur != null) {
                int scanned = 0;
                while (cur.moveToNext() && scanned++ < 5000) {
                    long tid = cur.getLong(0);
                    if (cur.getInt(4) == 0 && cur.getInt(5) == 1) unread.put(tid, (unread.containsKey(tid) ? unread.get(tid) : 0) + 1);
                    if (map.containsKey(tid) || map.size() >= 200) continue;
                    String addr = cur.getString(1) == null ? "" : cur.getString(1);
                    map.put(tid, new JSONObject().put("id", tid).put("address", addr).put("name", name(c, addr))
                            .put("body", cur.getString(2) == null ? "" : cur.getString(2)).put("time", cur.getLong(3))
                            .put("out", cur.getInt(5) != 1));
                }
            }
        }
        JSONArray arr = new JSONArray();
        for (Map.Entry<Long, JSONObject> e : map.entrySet())
            arr.put(e.getValue().put("unread", unread.containsKey(e.getKey()) ? unread.get(e.getKey()) : 0));
        return arr;
    }

    private static JSONObject thread(Context c, String id) throws Exception {
        ArrayList<JSONObject> list = new ArrayList<>();
        String addr = "";
        try (Cursor cur = c.getContentResolver().query(Uri.parse("content://sms"),
                new String[]{"_id", "address", "body", "date", "type"}, "thread_id = ?", new String[]{id}, "date DESC")) {
            if (cur != null) {
                while (cur.moveToNext() && list.size() < 300) {
                    if (addr.isEmpty() && cur.getString(1) != null) addr = cur.getString(1);
                    int type = cur.getInt(4);
                    list.add(new JSONObject().put("id", cur.getLong(0)).put("body", cur.getString(2) == null ? "" : cur.getString(2))
                            .put("time", cur.getLong(3)).put("out", type != 1).put("failed", type == 5).put("pending", type == 4 || type == 6));
                }
            }
        }
        JSONArray arr = new JSONArray();
        for (int i = list.size() - 1; i >= 0; i--) arr.put(list.get(i));
        // danh dau da doc (co the khong duoc neu khong phai app SMS mac dinh)
        try {
            android.content.ContentValues v = new android.content.ContentValues();
            v.put("read", 1);
            c.getContentResolver().update(Uri.parse("content://sms/inbox"), v, "thread_id = ? AND read = 0", new String[]{id});
        } catch (Exception ignored) {}
        return new JSONObject().put("id", id).put("address", addr).put("name", name(c, addr)).put("messages", arr);
    }

    private static JSONArray calls(Context c) throws Exception {
        JSONArray arr = new JSONArray();
        try (Cursor cur = c.getContentResolver().query(CallLog.Calls.CONTENT_URI,
                new String[]{CallLog.Calls.NUMBER, CallLog.Calls.CACHED_NAME, CallLog.Calls.TYPE, CallLog.Calls.DATE, CallLog.Calls.DURATION},
                null, null, CallLog.Calls.DATE + " DESC")) {
            if (cur != null) {
                while (cur.moveToNext() && arr.length() < 200) {
                    String num = cur.getString(0) == null ? "" : cur.getString(0);
                    String nm = cur.getString(1);
                    if (nm == null || nm.isEmpty()) nm = name(c, num);
                    int t = cur.getInt(2);
                    String type = t == CallLog.Calls.INCOMING_TYPE ? "in" : t == CallLog.Calls.OUTGOING_TYPE ? "out"
                            : t == CallLog.Calls.MISSED_TYPE ? "missed" : t == CallLog.Calls.REJECTED_TYPE ? "rejected" : "other";
                    arr.put(new JSONObject().put("number", num).put("name", nm).put("type", type)
                            .put("time", cur.getLong(3)).put("duration", cur.getLong(4)));
                }
            }
        }
        return arr;
    }

    private static JSONArray contacts(Context c) throws Exception {
        JSONArray arr = new JSONArray();
        try (Cursor cur = c.getContentResolver().query(ContactsContract.CommonDataKinds.Phone.CONTENT_URI,
                new String[]{ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME, ContactsContract.CommonDataKinds.Phone.NUMBER},
                null, null, ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME + " ASC")) {
            if (cur != null) {
                while (cur.moveToNext() && arr.length() < 3000)
                    arr.put(new JSONObject().put("name", cur.getString(0) == null ? "" : cur.getString(0)).put("number", cur.getString(1) == null ? "" : cur.getString(1)));
            }
        }
        return arr;
    }

    // ---- hanh dong
    public static String sendSms(Context c, String to, String body) {
        if (!has(c, Manifest.permission.SEND_SMS)) return "Điện thoại chưa cho phép gửi tin nhắn.";
        if (to == null || to.trim().isEmpty() || body == null || body.isEmpty()) return "Thiếu số điện thoại hoặc nội dung.";
        try {
            SmsManager sm = android.os.Build.VERSION.SDK_INT >= 31 ? c.getSystemService(SmsManager.class) : SmsManager.getDefault();
            ArrayList<String> parts = sm.divideMessage(body);
            sm.sendMultipartTextMessage(to.trim(), null, parts, null, null);
            // Android chi tu luu tin da gui neu la app SMS mac dinh; luu tay de hien trong cuoc tro chuyen
            try {
                android.content.ContentValues v = new android.content.ContentValues();
                v.put("address", to.trim()); v.put("body", body); v.put("date", System.currentTimeMillis()); v.put("read", 1); v.put("type", 2);
                c.getContentResolver().insert(Uri.parse("content://sms/sent"), v);
            } catch (Exception ignored) {}
            return null;
        } catch (Exception e) { return "Không gửi được: " + e.getMessage(); }
    }

    public static String call(Context c, String number) {
        if (!has(c, Manifest.permission.CALL_PHONE)) return "Điện thoại chưa cho phép gọi điện.";
        if (number == null || number.trim().isEmpty()) return "Chưa nhập số.";
        try {
            TelecomManager tm = (TelecomManager) c.getSystemService(Context.TELECOM_SERVICE);
            tm.placeCall(Uri.fromParts("tel", number.trim(), null), new Bundle());
            return null;
        } catch (SecurityException e) { return "Không gọi được: thiếu quyền."; }
        catch (Exception e) { return "Không gọi được: " + e.getMessage(); }
    }

    public static String endCall(Context c) {
        try {
            TelecomManager tm = (TelecomManager) c.getSystemService(Context.TELECOM_SERVICE);
            if (android.os.Build.VERSION.SDK_INT >= 28 && has(c, "android.permission.ANSWER_PHONE_CALLS")) { tm.endCall(); return null; }
        } catch (Exception ignored) {}
        return "Hãy kết thúc cuộc gọi trên điện thoại.";
    }

    // ---- bao may tinh khi co tin nhan moi
    private static ContentObserver observer;

    public static void watch(final Context c) {
        if (observer != null || !has(c, Manifest.permission.READ_SMS)) return;
        observer = new ContentObserver(new Handler(Looper.getMainLooper())) {
            long last;
            @Override public void onChange(boolean self) {
                long now = System.currentTimeMillis();
                if (now - last < 800) return;
                last = now;
                try { Hub.broadcast(new JSONObject().put("t", "smsChanged")); } catch (Exception ignored) {}
            }
        };
        try { c.getContentResolver().registerContentObserver(Uri.parse("content://sms"), true, observer); } catch (Exception e) { observer = null; }
        try {
            c.getContentResolver().registerContentObserver(CallLog.Calls.CONTENT_URI, true, new ContentObserver(new Handler(Looper.getMainLooper())) {
                @Override public void onChange(boolean self) { try { Hub.broadcast(new JSONObject().put("t", "callsChanged")); } catch (Exception ignored) {} }
            });
        } catch (Exception ignored) {}
    }
}
