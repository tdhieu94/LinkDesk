package vn.linkdesk;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.os.Build;
import android.os.Bundle;
import android.view.Surface;
import android.media.projection.MediaProjection;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.Display;

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;

/**
 * Chup man hinh qua MediaProjection. Hai che do dung chung 1 VirtualDisplay (Android 14 chi cho tao 1 lan):
 * - JPEG: VirtualDisplay -> ImageReader -> JPEG (trinh duyet, may tinh ban cu)
 * - H.264: VirtualDisplay -> MediaCodec (khi moi may tinh dang xem deu ho tro H.264)
 */
public class ScreenCapturer implements ImageReader.OnImageAvailableListener {
    private static final String TAG = "LinkDesk.Cap";

    private final Context ctx;
    private final MediaProjection projection;
    private HandlerThread thread;
    private Handler handler;
    private VirtualDisplay vd;
    private ImageReader reader;
    private int width, height, dpi, rotation = -1;

    private volatile int maxSize, quality, fps;
    private long lastFrameAt;
    private boolean pendingRetry;
    private Bitmap bitmap;
    private final ByteArrayOutputStream jpeg = new ByteArrayOutputStream(256 * 1024);
    private DisplayManager.DisplayListener displayListener;

    // ---- H.264
    private boolean h264;
    private MediaCodec codec;
    private Surface codecSurface;
    private volatile boolean codecRunning;
    private byte[] csd = new byte[0];
    private long lastKeyReq;

    public ScreenCapturer(Context ctx, MediaProjection projection) {
        this.ctx = ctx;
        this.projection = projection;
        maxSize = Prefs.getMaxSize(ctx);
        quality = Prefs.getQuality(ctx);
        fps = Prefs.getFps(ctx);
    }

    public void start() {
        thread = new HandlerThread("capture");
        thread.start();
        handler = new Handler(thread.getLooper());
        handler.post(new Runnable() {
            @Override public void run() { setup(); }
        });
    }

    private Display display() {
        DisplayManager dm = (DisplayManager) ctx.getSystemService(Context.DISPLAY_SERVICE);
        return dm.getDisplay(Display.DEFAULT_DISPLAY);
    }

    /** Kich thuoc that cua man hinh (theo huong hien tai). */
    public static int[] realSize(Context ctx) {
        DisplayManager dm = (DisplayManager) ctx.getSystemService(Context.DISPLAY_SERVICE);
        Display d = dm.getDisplay(Display.DEFAULT_DISPLAY);
        DisplayMetrics m = new DisplayMetrics();
        d.getRealMetrics(m);
        return new int[]{m.widthPixels, m.heightPixels, m.densityDpi};
    }

    private void computeSize() {
        int[] r = realSize(ctx);
        int w = r[0], h = r[1];
        float scale = Math.min(1f, (float) maxSize / Math.max(w, h));
        // H.264: canh boi 16 de bo giai ma khong phai cat vien
        int align = h264 ? ~15 : ~1;
        width = Math.max(16, Math.round(w * scale) & align);
        height = Math.max(16, Math.round(h * scale) & align);
        dpi = Math.max(80, Math.round(r[2] * scale));
        rotation = display().getRotation();
    }

    private void setup() {
        computeSize();
        reader = ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, 2);
        reader.setOnImageAvailableListener(this, handler);
        vd = projection.createVirtualDisplay("LinkDesk", width, height, dpi,
                DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR, reader.getSurface(), null, handler);
        displayListener = new DisplayManager.DisplayListener() {
            @Override public void onDisplayAdded(int id) {}
            @Override public void onDisplayRemoved(int id) {}
            @Override public void onDisplayChanged(int id) {
                if (id == Display.DEFAULT_DISPLAY && display().getRotation() != rotation) resize();
            }
        };
        ((DisplayManager) ctx.getSystemService(Context.DISPLAY_SERVICE)).registerDisplayListener(displayListener, handler);
        Log.i(TAG, "capture " + width + "x" + height);
    }

    /** Doi kich thuoc khi xoay man hinh hoac doi chat luong. Chay tren handler. */
    private void resize() {
        if (vd == null) return;
        if (h264) { restartEncoder(); return; }
        computeSize();
        ImageReader old = reader;
        reader = ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, 2);
        reader.setOnImageAvailableListener(this, handler);
        vd.resize(width, height, dpi);
        vd.setSurface(reader.getSurface());
        if (old != null) old.close();
        bitmap = null;
        Log.i(TAG, "resize " + width + "x" + height);
    }

    public void setVideo(final int newMax, int newQuality, int newFps) {
        final boolean changed = quality != Math.max(20, Math.min(95, newQuality)) || fps != Math.max(5, Math.min(60, newFps));
        quality = Math.max(20, Math.min(95, newQuality));
        fps = Math.max(5, Math.min(60, newFps));
        final int clamped = Math.max(480, Math.min(4096, newMax));
        if (handler != null) handler.post(new Runnable() {
            @Override public void run() {
                if (clamped != maxSize) { maxSize = clamped; resize(); }
                else if (changed && h264) restartEncoder();
            }
        });
    }

    /** Yeu cau gui lai 1 khung (vd. khi co may tinh moi ket noi). */
    public void requestFrame() {
        if (handler == null) return;
        if (h264) { requestKey(); return; }
        handler.post(new Runnable() {
            @Override public void run() { encodeLast(true); }
        });
    }

    @Override
    public void onImageAvailable(ImageReader r) {
        if (r != reader) return;
        encodeLast(false);
    }

    private void encodeLast(boolean force) {
        if (reader == null) return;
        // Khong ai xem: chi cap nhat anh dem 2 lan/giay (khong nen JPEG) de tiet kiem pin,
        // nhung van co san khung hinh moi nhat khi may tinh vua ket noi.
        boolean watching = Hub.viewerCount() > 0;
        long now = SystemClock.uptimeMillis();
        long minGap = watching ? 1000L / fps : 500L;
        if (!force && now - lastFrameAt < minGap) {
            if (!pendingRetry) {
                pendingRetry = true;
                handler.postDelayed(new Runnable() {
                    @Override public void run() { pendingRetry = false; encodeLast(false); }
                }, minGap - (now - lastFrameAt));
            }
            return;
        }
        Image img;
        try {
            img = reader.acquireLatestImage();
        } catch (IllegalStateException e) {
            return;
        }
        if (img == null) {
            if (force && watching && bitmap != null && validW > 0) compressAndPublish(validW, validH);
            return;
        }
        lastFrameAt = now;
        try {
            Image.Plane p = img.getPlanes()[0];
            ByteBuffer buf = p.getBuffer();
            int pixelStride = p.getPixelStride();
            int rowStride = p.getRowStride();
            int w = img.getWidth(), h = img.getHeight();
            int fullW = rowStride / pixelStride;
            if (bitmap == null || bitmap.getWidth() != fullW || bitmap.getHeight() != h) {
                bitmap = Bitmap.createBitmap(fullW, h, Bitmap.Config.ARGB_8888);
            }
            buf.rewind();
            bitmap.copyPixelsFromBuffer(buf);
            img.close();
            img = null;
            validW = w; validH = h;
            if (watching) compressAndPublish(w, h);
        } catch (Exception e) {
            Log.w(TAG, "encode", e);
        } finally {
            if (img != null) img.close();
        }
    }

    private int validW, validH;

    private void compressAndPublish(int w, int h) {
        jpeg.reset();
        // header: 1 byte loai (1 = jpeg), 2 byte rong hop le, 2 byte cao
        jpeg.write(1);
        jpeg.write((w >> 8) & 0xff); jpeg.write(w & 0xff);
        jpeg.write((h >> 8) & 0xff); jpeg.write(h & 0xff);
        bitmap.compress(Bitmap.CompressFormat.JPEG, quality, jpeg);
        Hub.publishFrame(jpeg.toByteArray());
    }

    public boolean isH264() { return h264; }

    /** Doi che do theo cac may dang xem. Goi tu bat ky luong nao. */
    public void updateMode() {
        if (handler == null) return;
        handler.post(new Runnable() {
            @Override public void run() {
                boolean want = Hub.allWantH264();
                if (want == h264 || vd == null) { if (h264) requestKey(); return; }
                if (want) {
                    h264 = true;
                    if (!startEncoder()) { h264 = false; resize(); Hub.toastAll("Điện thoại không bật được video H.264, dùng lại chế độ ảnh JPEG."); Hub.setH264Failed(); }
                    else if (reader != null) { reader.close(); reader = null; }
                } else {
                    h264 = false;
                    reader = null;
                    resize(); // tao lai ImageReader va gan vao VirtualDisplay
                    stopEncoder();
                }
                Commands.broadcastStatus(ctx);
            }
        });
    }

    public void requestKey() {
        MediaCodec c = codec;
        long now = SystemClock.uptimeMillis();
        if (c == null || now - lastKeyReq < 300) return;
        lastKeyReq = now;
        try {
            Bundle b = new Bundle();
            b.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0);
            c.setParameters(b);
        } catch (Exception ignored) {}
    }

    private boolean startEncoder() {
        computeSize();
        int bitrate = (int) Math.min(20_000_000L, Math.max(1_000_000L,
                (long) width * height * fps * (0.04 + (quality - 20) * 0.002)));
        for (int attempt = 0; attempt < 2; attempt++) {
            MediaCodec c = null;
            try {
                MediaFormat f = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height);
                f.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
                f.setInteger(MediaFormat.KEY_BIT_RATE, bitrate);
                f.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
                f.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 3);
                f.setLong(MediaFormat.KEY_REPEAT_PREVIOUS_FRAME_AFTER, 250_000); // man hinh dung yen: lap khung 4 lan/giay
                if (Build.VERSION.SDK_INT >= 29) f.setFloat("max-fps-to-encoder", fps);
                if (attempt == 0) {
                    f.setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline);
                    f.setInteger(MediaFormat.KEY_LEVEL, MediaCodecInfo.CodecProfileLevel.AVCLevel41);
                }
                c = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
                c.configure(f, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
                Surface s = c.createInputSurface();
                c.start();
                codec = c; codecSurface = s; codecRunning = true; csd = new byte[0];
                final MediaCodec cc = c;
                final int w = width, h = height;
                Thread t = new Thread(new Runnable() {
                    @Override public void run() { drain(cc, w, h); }
                }, "h264-out");
                t.setPriority(Thread.MAX_PRIORITY);
                t.start();
                vd.resize(width, height, dpi);
                vd.setSurface(s);
                Log.i(TAG, "h264 " + width + "x" + height + " " + bitrate / 1000 + "kbps");
                return true;
            } catch (Exception e) {
                Log.w(TAG, "h264 start (attempt " + attempt + ")", e);
                if (c != null) { try { c.release(); } catch (Exception ignored) {} }
                codec = null; codecSurface = null; codecRunning = false;
            }
        }
        return false;
    }

    private void stopEncoder() {
        codecRunning = false;
        MediaCodec c = codec;
        Surface s = codecSurface;
        codec = null; codecSurface = null;
        if (c != null) {
            try { c.stop(); } catch (Exception ignored) {}
            try { c.release(); } catch (Exception ignored) {}
        }
        if (s != null) s.release();
    }

    private void restartEncoder() {
        vd.setSurface(null);
        stopEncoder();
        if (!startEncoder()) {
            h264 = false;
            resize();
            Hub.toastAll("Video H.264 bị lỗi, dùng lại chế độ ảnh JPEG.");
            Hub.setH264Failed();
            Commands.broadcastStatus(ctx);
        }
    }

    private void drain(MediaCodec c, int w, int h) {
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        try {
            while (codecRunning && codec == c) {
                int idx = c.dequeueOutputBuffer(info, 100_000);
                if (idx < 0) continue;
                ByteBuffer b = c.getOutputBuffer(idx);
                byte[] data = new byte[info.size];
                if (b != null && info.size > 0) {
                    b.position(info.offset);
                    b.limit(info.offset + info.size);
                    b.get(data);
                }
                c.releaseOutputBuffer(idx, false);
                if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) { csd = data; continue; }
                if (data.length == 0) continue;
                boolean key = (info.flags & MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0;
                byte[] pre = key ? csd : new byte[0];
                // [4][co: 1 = khung khoa][rong 2][cao 2][Annex-B H.264]
                byte[] p = new byte[6 + pre.length + data.length];
                p[0] = 4; p[1] = (byte) (key ? 1 : 0);
                p[2] = (byte) (w >> 8); p[3] = (byte) w; p[4] = (byte) (h >> 8); p[5] = (byte) h;
                System.arraycopy(pre, 0, p, 6, pre.length);
                System.arraycopy(data, 0, p, 6 + pre.length, data.length);
                Hub.publishVideo(p, key);
            }
        } catch (Exception e) {
            if (codecRunning) Log.w(TAG, "h264 drain", e);
        }
    }

    public void stop() {
        if (handler == null) return;
        handler.post(new Runnable() {
            @Override public void run() {
                try {
                    if (displayListener != null)
                        ((DisplayManager) ctx.getSystemService(Context.DISPLAY_SERVICE)).unregisterDisplayListener(displayListener);
                    if (vd != null) vd.release();
                    if (reader != null) reader.close();
                } catch (Exception ignored) {}
                stopEncoder();
                vd = null;
                reader = null;
                bitmap = null;
                thread.quitSafely();
            }
        });
        Hub.clearFrame();
    }
}
