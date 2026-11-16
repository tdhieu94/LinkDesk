package vn.linkdesk;

import android.content.Context;
import android.content.SharedPreferences;

import java.security.SecureRandom;
import java.util.Locale;

public final class Prefs {
    private Prefs() {}

    private static SharedPreferences sp(Context c) {
        return c.getApplicationContext().getSharedPreferences("linkdesk", Context.MODE_PRIVATE);
    }

    public static synchronized String getPin(Context c) {
        String pin = sp(c).getString("pin", null);
        if (pin == null) pin = newPin(c);
        return pin;
    }

    public static synchronized String newPin(Context c) {
        String pin = String.format(Locale.ROOT, "%06d", new SecureRandom().nextInt(1_000_000));
        sp(c).edit().putString("pin", pin).apply();
        return pin;
    }

    public static int getMaxSize(Context c) { return sp(c).getInt("maxSize", 1280); }
    public static int getQuality(Context c) { return sp(c).getInt("quality", 70); }
    public static int getFps(Context c) { return sp(c).getInt("fps", 30); }

    /** Giu man hinh sang khi may tinh dang ket noi (mac dinh: bat). */
    public static boolean getKeepAwake(Context c) { return sp(c).getBoolean("keepAwake", true); }
    public static void setKeepAwake(Context c, boolean on) { sp(c).edit().putBoolean("keepAwake", on).apply(); }
    /** Lam toi man hinh dien thoai khi may tinh dang ket noi (mac dinh: bat). */
    public static boolean getDark(Context c) { return sp(c).getBoolean("dark", true); }
    public static void setDark(Context c, boolean on) { sp(c).edit().putBoolean("dark", on).apply(); }

    public static void setVideo(Context c, int maxSize, int quality, int fps) {
        sp(c).edit().putInt("maxSize", maxSize).putInt("quality", quality).putInt("fps", fps).apply();
    }
}
