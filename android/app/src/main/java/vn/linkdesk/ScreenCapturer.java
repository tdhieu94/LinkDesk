package vn.linkdesk;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.media.projection.MediaProjection;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.Display;

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;

/** Chup man hinh qua MediaProjection -> ImageReader -> JPEG. */
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
        width = Math.max(2, Math.round(w * scale) & ~1);
        height = Math.max(2, Math.round(h * scale) & ~1);
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
        quality = Math.max(20, Math.min(95, newQuality));
        fps = Math.max(5, Math.min(60, newFps));
        final int clamped = Math.max(480, Math.min(4096, newMax));
        if (handler != null) handler.post(new Runnable() {
            @Override public void run() {
                if (clamped != maxSize) { maxSize = clamped; resize(); }
            }
        });
    }

    /** Yeu cau gui lai 1 khung (vd. khi co may tinh moi ket noi). */
    public void requestFrame() {
        if (handler == null) return;
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
        boolean watching = Hub.clientCount() > 0;
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
                vd = null;
                reader = null;
                bitmap = null;
                thread.quitSafely();
            }
        });
        Hub.clearFrame();
    }
}
