package vn.linkdesk;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.media.AudioAttributes;
import android.media.AudioManager;
import android.media.MediaPlayer;
import android.media.RingtoneManager;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.util.Log;

/** "Tim dien thoai": do chuong bao thuc to nhat (ke ca khi dang im lang) toi da 60 giay. */
public final class FindPhone {
    private FindPhone() {}
    private static final String TAG = "LinkDesk.Find";
    private static final String CHANNEL = "find";
    private static final int NOTIF_ID = 77;
    public static final String ACTION_STOP = "vn.linkdesk.FIND_STOP";
    private static MediaPlayer player;
    private static int oldVol = -1;
    private static BroadcastReceiver stopper;
    private static final Handler h = new Handler(Looper.getMainLooper());
    private static final Runnable autoStop = new Runnable() { @Override public void run() { stop(app); } };
    private static Context app;

    public static boolean isRinging() { return player != null; }

    public static void start(final Context ctx) {
        h.post(new Runnable() { @Override public void run() { startMain(ctx.getApplicationContext()); } });
    }

    private static void startMain(Context c) {
        if (player != null) return;
        app = c;
        try {
            AudioManager am = (AudioManager) c.getSystemService(Context.AUDIO_SERVICE);
            oldVol = am.getStreamVolume(AudioManager.STREAM_ALARM);
            am.setStreamVolume(AudioManager.STREAM_ALARM, am.getStreamMaxVolume(AudioManager.STREAM_ALARM), 0);
            Uri u = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM);
            if (u == null) u = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE);
            MediaPlayer p = new MediaPlayer();
            p.setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build());
            p.setDataSource(c, u);
            p.setLooping(true);
            p.prepare();
            p.start();
            player = p;
        } catch (Exception e) {
            Log.w(TAG, "ring", e);
            Hub.toastAll("Không phát được chuông: " + e.getMessage());
        }
        try {
            Vibrator v = (Vibrator) c.getSystemService(Context.VIBRATOR_SERVICE);
            if (v != null) v.vibrate(VibrationEffect.createWaveform(new long[]{0, 800, 600}, 0));
        } catch (Exception ignored) {}
        // Mo khoa hoac bam thong bao thi tat
        stopper = new BroadcastReceiver() {
            @Override public void onReceive(Context cx, Intent i) { stop(cx); }
        };
        IntentFilter f = new IntentFilter(Intent.ACTION_USER_PRESENT);
        f.addAction(ACTION_STOP);
        if (Build.VERSION.SDK_INT >= 33) c.registerReceiver(stopper, f, Context.RECEIVER_NOT_EXPORTED);
        else c.registerReceiver(stopper, f);
        try {
            NotificationManager nm = (NotificationManager) c.getSystemService(Context.NOTIFICATION_SERVICE);
            nm.createNotificationChannel(new NotificationChannel(CHANNEL, "Tìm điện thoại", NotificationManager.IMPORTANCE_HIGH));
            PendingIntent pi = PendingIntent.getBroadcast(c, 7, new Intent(ACTION_STOP).setPackage(c.getPackageName()), PendingIntent.FLAG_IMMUTABLE);
            nm.notify(NOTIF_ID, new Notification.Builder(c, CHANNEL)
                    .setSmallIcon(R.drawable.ic_stat)
                    .setContentTitle("Máy tính đang tìm điện thoại này")
                    .setContentText("Chạm để tắt chuông")
                    .setContentIntent(pi).setDeleteIntent(pi).setAutoCancel(true)
                    .addAction(new Notification.Action.Builder(null, "Tắt chuông", pi).build())
                    .build());
        } catch (Exception ignored) {}
        h.postDelayed(autoStop, 60_000);
        Commands.broadcastStatus(c);
    }

    public static void stop(final Context ctx) {
        h.post(new Runnable() { @Override public void run() { stopMain(ctx.getApplicationContext()); } });
    }

    private static void stopMain(Context c) {
        h.removeCallbacks(autoStop);
        MediaPlayer p = player;
        player = null;
        if (p != null) { try { p.stop(); } catch (Exception ignored) {} p.release(); }
        try { ((Vibrator) c.getSystemService(Context.VIBRATOR_SERVICE)).cancel(); } catch (Exception ignored) {}
        if (oldVol >= 0) {
            try { ((AudioManager) c.getSystemService(Context.AUDIO_SERVICE)).setStreamVolume(AudioManager.STREAM_ALARM, oldVol, 0); } catch (Exception ignored) {}
            oldVol = -1;
        }
        if (stopper != null) { try { c.unregisterReceiver(stopper); } catch (Exception ignored) {} stopper = null; }
        try { ((NotificationManager) c.getSystemService(Context.NOTIFICATION_SERVICE)).cancel(NOTIF_ID); } catch (Exception ignored) {}
        Commands.broadcastStatus(c);
    }
}
