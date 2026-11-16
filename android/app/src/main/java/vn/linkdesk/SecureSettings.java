package vn.linkdesk;

import android.content.ComponentName;
import android.content.Context;
import android.content.pm.PackageManager;
import android.provider.Settings;
import android.text.TextUtils;

/**
 * Tuy chon: neu da cap quyen WRITE_SECURE_SETTINGS mot lan bang adb
 * (adb shell pm grant vn.linkdesk android.permission.WRITE_SECURE_SETTINGS),
 * app co the tu bat/tat dich vu Tro nang cua chinh no ma khong can vao Cai dat.
 */
public final class SecureSettings {
    private SecureSettings() {}

    public static boolean canWrite(Context c) {
        return c.checkSelfPermission("android.permission.WRITE_SECURE_SETTINGS") == PackageManager.PERMISSION_GRANTED;
    }

    private static String me(Context c) {
        return new ComponentName(c, ControlService.class).flattenToString();
    }

    public static boolean isAccessibilityOn(Context c) {
        String v = Settings.Secure.getString(c.getContentResolver(), Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES);
        if (v == null) return false;
        String mine = me(c);
        String shortName = new ComponentName(c, ControlService.class).flattenToShortString();
        for (String s : v.split(":")) if (s.equalsIgnoreCase(mine) || s.equalsIgnoreCase(shortName)) return true;
        return false;
    }

    public static boolean setAccessibility(Context c, boolean on) {
        if (!canWrite(c)) return false;
        try {
            String v = Settings.Secure.getString(c.getContentResolver(), Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES);
            StringBuilder out = new StringBuilder();
            String mine = me(c);
            String shortName = new ComponentName(c, ControlService.class).flattenToShortString();
            if (v != null) for (String s : v.split(":")) {
                if (s.isEmpty() || s.equalsIgnoreCase(mine) || s.equalsIgnoreCase(shortName)) continue;
                if (out.length() > 0) out.append(':');
                out.append(s);
            }
            if (on) {
                if (out.length() > 0) out.append(':');
                out.append(mine);
            }
            String result = out.toString();
            Settings.Secure.putString(c.getContentResolver(), Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
                    TextUtils.isEmpty(result) ? "" : result);
            Settings.Secure.putInt(c.getContentResolver(), Settings.Secure.ACCESSIBILITY_ENABLED,
                    TextUtils.isEmpty(result) ? 0 : 1);
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    // ------------------------------------------------------------ "Lam mo them" (Extra dim, Android 12+)

    private static final String EXTRA_DIM_ON = "reduce_bright_colors_activated";
    private static final String EXTRA_DIM_LEVEL = "reduce_bright_colors_level";
    private static int savedOn = -1, savedLevel = -1;

    /**
     * Bat/tat "Lam mo them" cua Android (giam do sang xuong duoi muc thap nhat).
     * Bo loc mau nay chi ap dung tren man hinh that, khong vao hinh gui sang may tinh.
     * Can WRITE_SECURE_SETTINGS. Tra ve true neu da doi duoc.
     */
    public static synchronized boolean setExtraDim(Context c, boolean on) {
        if (!canWrite(c) || android.os.Build.VERSION.SDK_INT < 31) return false;
        try {
            android.content.ContentResolver cr = c.getContentResolver();
            if (on) {
                if (savedOn < 0) {
                    savedOn = Settings.Secure.getInt(cr, EXTRA_DIM_ON, 0);
                    savedLevel = Settings.Secure.getInt(cr, EXTRA_DIM_LEVEL, 0);
                }
                Settings.Secure.putInt(cr, EXTRA_DIM_LEVEL, 100);
                Settings.Secure.putInt(cr, EXTRA_DIM_ON, 1);
            } else if (savedOn >= 0) {
                Settings.Secure.putInt(cr, EXTRA_DIM_LEVEL, savedLevel);
                Settings.Secure.putInt(cr, EXTRA_DIM_ON, savedOn);
                savedOn = -1;
                savedLevel = -1;
            }
            return true;
        } catch (Exception e) {
            return false;
        }
    }
}
