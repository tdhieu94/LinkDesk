package vn.linkdesk;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.widget.Toast;
import org.json.JSONObject;
import java.util.ArrayList;

/** "Gui sang may tinh" trong menu Chia se: chu -> clipboard may tinh, tep/anh -> tai ve may tinh. */
public class ShareActivity extends Activity {
    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        String msg;
        try { msg = handle(getIntent()); } catch (Exception e) { msg = "Lỗi: " + e.getMessage(); }
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show();
        finish();
    }

    private String handle(Intent i) throws Exception {
        if (Hub.clientCount() == 0) return "Máy tính chưa kết nối với LinkDesk.";
        String action = i.getAction();
        ArrayList<Uri> uris = new ArrayList<>();
        if (Intent.ACTION_SEND.equals(action)) {
            Uri u = i.getParcelableExtra(Intent.EXTRA_STREAM);
            if (u != null) uris.add(u);
        } else if (Intent.ACTION_SEND_MULTIPLE.equals(action)) {
            ArrayList<Uri> l = i.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
            if (l != null) uris.addAll(l);
        } else if (Intent.ACTION_MAIN.equals(action) || "vn.linkdesk.SEND_CLIP".equals(action)) {
            // tu nut trong app: gui clipboard hien tai
            ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
            ClipData cd = cm.getPrimaryClip();
            CharSequence t = cd != null && cd.getItemCount() > 0 ? cd.getItemAt(0).coerceToText(this) : null;
            if (t == null || t.length() == 0) return "Bộ nhớ tạm đang trống.";
            Hub.broadcast(new JSONObject().put("t", "copied").put("s", t.toString()));
            return "Đã gửi chữ sang máy tính";
        }
        if (!uris.isEmpty()) {
            String type = i.getType();
            boolean oneImage = uris.size() == 1 && type != null && type.startsWith("image/");
            for (Uri u : uris) {
                String id = FilesApi.addShared(u);
                Hub.broadcast(new JSONObject().put("t", "incoming").put("id", id)
                        .put("name", FilesApi.displayName(this, u)).put("size", FilesApi.size(this, u))
                        .put("clip", oneImage));
            }
            if (oneImage) return "Đang gửi ảnh sang máy tính (tự chép vào clipboard)…";
            return uris.size() == 1 ? "Đang gửi sang máy tính…" : "Đang gửi " + uris.size() + " tệp sang máy tính…";
        }
        CharSequence text = i.getCharSequenceExtra(Intent.EXTRA_TEXT);
        if (text != null && text.length() > 0) {
            String s = text.toString().trim();
            if (s.matches("(?i)^https?://\\S+$")) {
                Hub.broadcast(new JSONObject().put("t", "link").put("url", s));
                return "Đã mở link trên máy tính";
            }
            Hub.broadcast(new JSONObject().put("t", "copied").put("s", text.toString()));
            return "Đã chép sang clipboard máy tính";
        }
        return "Không có gì để gửi.";
    }
}
