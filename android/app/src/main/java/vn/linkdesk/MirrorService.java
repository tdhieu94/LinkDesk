package vn.linkdesk;

import android.app.AppOpsManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.ServiceInfo;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;
import android.os.Process;
import android.util.Log;

/**
 * Service chay nen: giu may chu web + chia se man hinh.
 * May chu van chay khi man hinh tat hoac khi he thong dung chia se man hinh,
 * de may tinh khong bi ngat (van nhan thong bao, bam "bat man hinh" duoc).
 */
public class MirrorService extends Service {
    private static final String TAG = "LinkDesk.Svc";
    public static final String ACTION_START = "vn.linkdesk.START";
    public static final String ACTION_STOP = "vn.linkdesk.STOP";
    public static final String EXTRA_CODE = "code";
    public static final String EXTRA_DATA = "data";
    private static final String CHANNEL = "mirror";
    private static final int NOTIF_ID = 1;

    private WebServer web;
    private Discovery discovery;
    private MediaProjection projection;
    private ScreenCapturer capturer;
    private final Handler main = new Handler(Looper.getMainLooper());
    private PowerManager.WakeLock keepAwake;
    private BroadcastReceiver screenReceiver;

    public static volatile boolean running;

    @Override public IBinder onBind(Intent intent) { return null; }

    public boolean isProjecting() { return projection != null; }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent == null || ACTION_STOP.equals(intent.getAction())) {
            stopSelf();
            return START_NOT_STICKY;
        }
        if (ACTION_START.equals(intent.getAction())) {
            // startForegroundService() bat buoc phai goi startForeground() trong vai giay
            updateNotification(true);
            try {
                if (!running) startServer();
                if (projection == null) {
                    int code = intent.getIntExtra(EXTRA_CODE, 0);
                    Intent data = Build.VERSION.SDK_INT >= 33
                            ? intent.getParcelableExtra(EXTRA_DATA, Intent.class)
                            : (Intent) intent.getParcelableExtra(EXTRA_DATA);
                    startProjection(code, data);
                }
            } catch (Exception e) {
                Log.e(TAG, "start failed", e);
                if (!running) stopSelf();
            }
            Commands.broadcastStatus(this);
        }
        return START_NOT_STICKY;
    }

    private void startServer() throws Exception {
        PhoneApi.watch(this);
        web = new WebServer(this);
        web.start();
        discovery = new Discovery(this);
        discovery.start();
        running = true;
        Hub.mirror = this;
        screenReceiver = new BroadcastReceiver() {
            @Override public void onReceive(Context c, Intent i) { onScreenEvent(i.getAction()); }
        };
        IntentFilter f = new IntentFilter();
        f.addAction(Intent.ACTION_SCREEN_ON);
        f.addAction(Intent.ACTION_SCREEN_OFF);
        f.addAction(Intent.ACTION_USER_PRESENT);
        registerReceiver(screenReceiver, f);
    }

    private void startProjection(int code, Intent data) {
        MediaProjectionManager mpm = (MediaProjectionManager) getSystemService(MEDIA_PROJECTION_SERVICE);
        final MediaProjection p = mpm.getMediaProjection(code, data);
        if (p == null) throw new IllegalStateException("projection null");
        p.registerCallback(new MediaProjection.Callback() {
            @Override public void onStop() {
                if (projection != p) return;
                Log.i(TAG, "projection stopped by system/user");
                stopProjection();
                Hub.toastAll("Điện thoại đã dừng chia sẻ màn hình (thường do khoá máy). Kết nối vẫn giữ; mở khoá điện thoại để chia sẻ lại.");
                Commands.broadcastStatus(MirrorService.this);
                updateNotification(false);
            }
        }, main);
        projection = p;
        capturer = new ScreenCapturer(this, p);
        capturer.start();
        updateNotification(true);
        onClientsChanged();
    }

    private void stopProjection() {
        if (capturer != null) capturer.stop();
        capturer = null;
        MediaProjection p = projection;
        projection = null;
        if (p != null) {
            try { p.stop(); } catch (Exception ignored) {}
        }
    }

    private void onScreenEvent(String action) {
        Commands.broadcastStatus(this);
        if (Intent.ACTION_SCREEN_ON.equals(action) || Intent.ACTION_USER_PRESENT.equals(action)) {
            if (capturer != null) capturer.requestFrame();
            // Neu he thong da dung chia se khi khoa may: thu chia se lai (chi tu dong neu khong can hoi lai)
            if (projection == null && Hub.clientCount() > 0 && canProjectSilently()) {
                ControlService cs = Hub.control;
                Context starter = cs != null ? cs : this;
                try {
                    starter.startActivity(new Intent(starter, MainActivity.class)
                            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_NO_ANIMATION)
                            .putExtra(MainActivity.EXTRA_AUTO_START, true));
                } catch (Exception e) {
                    Log.w(TAG, "auto restart", e);
                }
            }
        }
        main.post(new Runnable() { @Override public void run() { updateAwake(); } });
    }

    /** Da cap "adb shell appops set vn.linkdesk PROJECT_MEDIA allow" thi Android khong hoi lai. */
    public boolean canProjectSilently() {
        try {
            AppOpsManager ao = (AppOpsManager) getSystemService(APP_OPS_SERVICE);
            int mode = Build.VERSION.SDK_INT >= 29
                    ? ao.unsafeCheckOpNoThrow("android:project_media", Process.myUid(), getPackageName())
                    : ao.checkOpNoThrow("android:project_media", Process.myUid(), getPackageName());
            return mode == AppOpsManager.MODE_ALLOWED;
        } catch (Exception e) {
            return false;
        }
    }

    private void updateNotification(boolean sharing) {
        NotificationManager nm = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        nm.createNotificationChannel(new NotificationChannel(CHANNEL, "Chia sẻ màn hình", NotificationManager.IMPORTANCE_LOW));
        Intent open = new Intent(this, MainActivity.class);
        PendingIntent pOpen = PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_IMMUTABLE);
        Intent stop = new Intent(this, MirrorService.class).setAction(ACTION_STOP);
        PendingIntent pStop = PendingIntent.getService(this, 1, stop, PendingIntent.FLAG_IMMUTABLE);
        Notification n = new Notification.Builder(this, CHANNEL)
                .setSmallIcon(R.drawable.ic_stat)
                .setContentTitle(sharing ? "LinkDesk đang chia sẻ màn hình" : "LinkDesk đã dừng chia sẻ màn hình")
                .setContentText(sharing ? "Máy tính kết nối tới " + Net.bestIp() : "Chạm để chia sẻ lại. Máy tính vẫn nhận được thông báo.")
                .setContentIntent(pOpen)
                .setOngoing(true)
                .addAction(new Notification.Action.Builder(null, "Tắt hẳn", pStop).build())
                .build();
        try {
            if (sharing) {
                if (Build.VERSION.SDK_INT >= 34) {
                    startForeground(NOTIF_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION | ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
                } else if (Build.VERSION.SDK_INT >= 29) {
                    startForeground(NOTIF_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
                } else {
                    startForeground(NOTIF_ID, n);
                }
            } else if (Build.VERSION.SDK_INT >= 34) {
                // Het chia se man hinh: van chay nen de giu ket noi voi may tinh
                startForeground(NOTIF_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
            } else {
                nm.notify(NOTIF_ID, n);
            }
        } catch (Exception e) {
            Log.w(TAG, "foreground update", e);
            try { nm.notify(NOTIF_ID, n); } catch (Exception ignored) {}
        }
    }

    /** Goi tu Hub khi so luong may tinh ket noi thay doi. */
    public void onClientsChanged() {
        main.post(new Runnable() {
            @Override public void run() {
                if (Hub.clientCount() > 0 && capturer != null) capturer.requestFrame();
                updateAwake();
            }
        });
    }

    public void setVideo(int maxSize, int quality, int fps) {
        Prefs.setVideo(this, maxSize, quality, fps);
        if (capturer != null) {
            capturer.setVideo(maxSize, quality, fps);
            capturer.requestFrame();
        }
    }

    public void requestFrame() {
        if (capturer != null) capturer.requestFrame();
    }

    public void setKeepAwake(boolean on) {
        Prefs.setKeepAwake(this, on);
        main.post(new Runnable() { @Override public void run() { updateAwake(); } });
    }

    public boolean isKeepAwake() { return Prefs.getKeepAwake(this); }

    public void setDark(boolean on) {
        Prefs.setDark(this, on);
        main.post(new Runnable() { @Override public void run() { updateAwake(); } });
    }

    /**
     * Khi co may tinh dang xem: giu man hinh khong tu tat (de chia se khong bi dung),
     * va neu bat "lam toi" thi giam do sang xuong thap nhat bang lop phu trong suot.
     * Chay tren luong chinh.
     */
    @SuppressWarnings("deprecation")
    private void updateAwake() {
        boolean watching = running && Hub.clientCount() > 0;
        boolean wantAwake = watching && Prefs.getKeepAwake(this);
        boolean wantDark = watching && Prefs.getDark(this);
        ControlService cs = Hub.control;
        boolean overlayOk = false;
        if (cs != null) overlayOk = cs.setOverlay(wantAwake || wantDark, wantDark);
        // "Sieu toi": them bo loc Lam mo them cua Android neu co quyen (adb 1 lan)
        SecureSettings.setExtraDim(this, wantDark);
        // Du phong khi chua bat Tro nang: wake lock (khong lam toi duoc)
        boolean needLock = wantAwake && !overlayOk;
        if (needLock && keepAwake == null) {
            PowerManager pm = (PowerManager) getSystemService(POWER_SERVICE);
            keepAwake = pm.newWakeLock(PowerManager.SCREEN_DIM_WAKE_LOCK | PowerManager.ON_AFTER_RELEASE, "LinkDesk:awake");
            keepAwake.acquire(8 * 60 * 60 * 1000L);
        } else if (!needLock && keepAwake != null) {
            if (keepAwake.isHeld()) keepAwake.release();
            keepAwake = null;
        }
    }

    /** Goi tu ControlService khi Tro nang bat/tat. */
    public void refreshAwake() {
        main.post(new Runnable() { @Override public void run() { updateAwake(); } });
    }

    public static boolean isScreenOn(Context c) {
        PowerManager pm = (PowerManager) c.getSystemService(POWER_SERVICE);
        return pm.isInteractive();
    }

    /** Bat sang man hinh (neu dang tat). */
    @SuppressWarnings("deprecation")
    public static void wakeScreen(Context c) {
        PowerManager pm = (PowerManager) c.getSystemService(POWER_SERVICE);
        PowerManager.WakeLock wl = pm.newWakeLock(PowerManager.SCREEN_BRIGHT_WAKE_LOCK
                | PowerManager.ACQUIRE_CAUSES_WAKEUP | PowerManager.ON_AFTER_RELEASE, "LinkDesk:wake");
        wl.acquire(3000);
    }

    @Override
    public void onDestroy() {
        running = false;
        Hub.mirror = null;
        if (screenReceiver != null) { try { unregisterReceiver(screenReceiver); } catch (Exception ignored) {} }
        stopProjection();
        if (web != null) web.stop();
        web = null;
        if (discovery != null) discovery.stop();
        discovery = null;
        updateAwake();
        SecureSettings.setExtraDim(this, false);
        ControlService cs = Hub.control;
        if (cs != null) cs.setOverlay(false, false);
        if (Build.VERSION.SDK_INT >= 24) stopForeground(STOP_FOREGROUND_REMOVE);
        super.onDestroy();
    }
}
