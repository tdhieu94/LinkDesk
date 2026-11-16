package vn.linkdesk;

import android.content.Context;
import android.content.pm.PackageManager;
import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioPlaybackCaptureConfiguration;
import android.media.AudioRecord;
import android.media.projection.MediaProjection;
import android.os.Build;
import android.util.Log;

/**
 * Thu am thanh dang phat tren dien thoai (nhac, video) va gui sang may tinh.
 * Dung AudioPlaybackCapture (Android 10+), can dang chia se man hinh va quyen RECORD_AUDIO.
 * Khung nhi phan: [3][PCM 16-bit, 48 kHz, stereo, little-endian].
 */
public final class AudioStreamer {
    private static final String TAG = "LinkDesk.Audio";
    public static final int RATE = 48000;
    private static AudioStreamer cur;

    private final AudioRecord rec;
    private volatile boolean running = true;

    private AudioStreamer(AudioRecord rec) { this.rec = rec; }

    public static boolean supported() { return Build.VERSION.SDK_INT >= 29; }

    public static boolean permitted(Context ctx) {
        return ctx.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED;
    }

    public static synchronized boolean isRunning() { return cur != null; }

    private static boolean anyWants() {
        for (WsConn c : Hub.snapshotClients()) if (c.wantAudio) return true;
        return false;
    }

    /** Bat/tat theo nhu cau hien tai. Tra ve loi (tieng Viet) neu khong bat duoc. */
    public static synchronized String update(Context ctx) {
        if (!anyWants()) { stop(); return null; }
        if (cur != null) return null;
        if (!supported()) return "Nghe âm thanh cần Android 10 trở lên.";
        if (!permitted(ctx)) return "Chưa cho phép Âm thanh: mở app LinkDesk trên điện thoại, mục Quyền, bấm \"Cho phép âm thanh\".";
        MirrorService m = Hub.mirror;
        MediaProjection p = m != null ? m.projection() : null;
        if (p == null) return "Cần đang chia sẻ màn hình thì mới nghe được âm thanh.";
        try {
            cur = start(p);
            return null;
        } catch (Exception e) {
            Log.w(TAG, "start", e);
            return "Không bật được âm thanh: " + e.getMessage();
        }
    }

    public static synchronized void stop() {
        AudioStreamer s = cur;
        cur = null;
        if (s != null) s.running = false;
    }

    @android.annotation.TargetApi(29)
    private static AudioStreamer start(MediaProjection p) {
        AudioPlaybackCaptureConfiguration cfg = new AudioPlaybackCaptureConfiguration.Builder(p)
                .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
                .addMatchingUsage(AudioAttributes.USAGE_GAME)
                .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
                .build();
        AudioFormat fmt = new AudioFormat.Builder()
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                .setSampleRate(RATE)
                .setChannelMask(AudioFormat.CHANNEL_IN_STEREO)
                .build();
        int min = AudioRecord.getMinBufferSize(RATE, AudioFormat.CHANNEL_IN_STEREO, AudioFormat.ENCODING_PCM_16BIT);
        AudioRecord r = new AudioRecord.Builder()
                .setAudioFormat(fmt)
                .setBufferSizeInBytes(Math.max(min, RATE * 4 / 5))
                .setAudioPlaybackCaptureConfig(cfg)
                .build();
        if (r.getState() != AudioRecord.STATE_INITIALIZED) { r.release(); throw new IllegalStateException("AudioRecord chưa sẵn sàng"); }
        final AudioStreamer s = new AudioStreamer(r);
        r.startRecording();
        Thread t = new Thread(new Runnable() {
            @Override public void run() { s.loop(); }
        }, "audio-capture");
        t.setPriority(Thread.MAX_PRIORITY);
        t.start();
        Log.i(TAG, "started");
        return s;
    }

    private void loop() {
        final int chunk = RATE * 4 / 50; // 20 ms
        byte[] buf = new byte[chunk];
        int silent = 0;
        try {
            while (running) {
                int off = 0;
                while (off < chunk && running) {
                    int n = rec.read(buf, off, chunk - off);
                    if (n < 0) throw new IllegalStateException("read " + n);
                    off += n;
                }
                if (!running) break;
                boolean zero = true;
                for (int i = 0; i < chunk; i += 2) if (buf[i] != 0 || buf[i + 1] != 0) { zero = false; break; }
                // Im lang lau: khong gui de do ton mang (may tinh tu dem lai khi co tieng)
                if (zero) { if (++silent > 10) continue; } else silent = 0;
                byte[] f = new byte[chunk + 1];
                f[0] = 3;
                System.arraycopy(buf, 0, f, 1, chunk);
                for (WsConn c : Hub.snapshotClients()) if (c.wantAudio) c.sendAudio(f);
            }
        } catch (Exception e) {
            Log.w(TAG, "loop", e);
            synchronized (AudioStreamer.class) { if (cur == this) cur = null; }
        } finally {
            try { rec.stop(); } catch (Exception ignored) {}
            rec.release();
            Log.i(TAG, "stopped");
        }
    }
}
