package vn.linkdesk;

import android.content.ComponentName;
import android.content.Context;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.media.MediaMetadata;
import android.media.session.MediaController;
import android.media.session.MediaSessionManager;
import android.media.session.PlaybackState;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.util.List;

/** Bai nhac/video dang phat (giong o "dang phat" cua Phone Link). Can quyen doc thong bao. */
public final class MediaCtl {
    private MediaCtl() {}

    private static volatile String last = "";
    private static volatile byte[] art;
    private static volatile int artId;

    public static void reset() { last = ""; }

    public static byte[] art() { return art; }

    private static MediaController current(Context c) {
        try {
            MediaSessionManager m = (MediaSessionManager) c.getSystemService(Context.MEDIA_SESSION_SERVICE);
            List<MediaController> list = m.getActiveSessions(new ComponentName(c, NotifService.class));
            if (list == null || list.isEmpty()) return null;
            for (MediaController mc : list) {
                PlaybackState st = mc.getPlaybackState();
                if (st != null && st.getState() == PlaybackState.STATE_PLAYING) return mc;
            }
            return list.get(0);
        } catch (Throwable e) { return null; }
    }

    /** Goi dinh ky; gui cho may tinh neu co thay doi. */
    public static void poll(Context c) {
        try {
            MediaController mc = current(c);
            JSONObject o = new JSONObject().put("t", "media");
            if (mc == null || mc.getMetadata() == null) {
                o.put("active", false);
            } else {
                MediaMetadata md = mc.getMetadata();
                PlaybackState st = mc.getPlaybackState();
                String title = str(md, MediaMetadata.METADATA_KEY_TITLE);
                if (title.isEmpty()) title = str(md, MediaMetadata.METADATA_KEY_DISPLAY_TITLE);
                String artist = str(md, MediaMetadata.METADATA_KEY_ARTIST);
                if (artist.isEmpty()) artist = str(md, MediaMetadata.METADATA_KEY_ALBUM_ARTIST);
                if (artist.isEmpty()) artist = str(md, MediaMetadata.METADATA_KEY_DISPLAY_SUBTITLE);
                String pkg = mc.getPackageName();
                String key = pkg + "|" + title + "|" + artist;
                Bitmap b = md.getBitmap(MediaMetadata.METADATA_KEY_ART);
                if (b == null) b = md.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART);
                if (b == null) b = md.getBitmap(MediaMetadata.METADATA_KEY_DISPLAY_ICON);
                if (!last.startsWith(key + "#")) {
                    byte[] a = null;
                    if (b != null) {
                        int w = b.getWidth(), h = b.getHeight();
                        float s = Math.min(1f, 320f / Math.max(w, h));
                        Bitmap sb = s < 1f ? Bitmap.createScaledBitmap(b, Math.max(1, (int) (w * s)), Math.max(1, (int) (h * s)), true) : b;
                        ByteArrayOutputStream bo = new ByteArrayOutputStream();
                        sb.compress(Bitmap.CompressFormat.JPEG, 85, bo);
                        a = bo.toByteArray();
                    }
                    art = a;
                    artId++;
                }
                o.put("active", true).put("title", title).put("artist", artist).put("pkg", pkg)
                        .put("app", appName(c, pkg))
                        .put("playing", st != null && st.getState() == PlaybackState.STATE_PLAYING)
                        .put("pos", st != null ? st.getPosition() : 0)
                        .put("dur", md.getLong(MediaMetadata.METADATA_KEY_DURATION))
                        .put("art", art != null ? artId : 0);
            }
            String s = o.toString();
            // khong gui lai chi vi vi tri thay doi
            String sig = sig(o);
            if (!sig.equals(last)) { last = sig; Hub.broadcast(o); }
        } catch (Throwable ignored) {}
    }

    private static String sig(JSONObject o) {
        return o.optString("pkg") + "|" + o.optString("title") + "|" + o.optString("artist") + "#" + o.optBoolean("active") + o.optBoolean("playing") + o.optInt("art");
    }

    private static String str(MediaMetadata md, String k) {
        CharSequence s = md.getText(k);
        return s == null ? "" : s.toString();
    }

    private static String appName(Context c, String pkg) {
        try {
            PackageManager pm = c.getPackageManager();
            return pm.getApplicationLabel(pm.getApplicationInfo(pkg, 0)).toString();
        } catch (Exception e) { return pkg; }
    }

    public static String action(Context c, String a) {
        MediaController mc = current(c);
        if (mc == null) return "Không có nhạc/video nào đang phát.";
        MediaController.TransportControls tc = mc.getTransportControls();
        PlaybackState st = mc.getPlaybackState();
        boolean playing = st != null && st.getState() == PlaybackState.STATE_PLAYING;
        if (a.equals("toggle")) { if (playing) tc.pause(); else tc.play(); }
        else if (a.equals("next")) tc.skipToNext();
        else if (a.equals("prev")) tc.skipToPrevious();
        else if (a.equals("play")) tc.play();
        else if (a.equals("pause")) tc.pause();
        reset();
        return null;
    }
}
