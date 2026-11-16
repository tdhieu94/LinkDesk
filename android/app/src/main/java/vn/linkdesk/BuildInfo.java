package vn.linkdesk;

import android.content.Context;
import android.content.pm.PackageInfo;

public final class BuildInfo {
    private BuildInfo() {}

    public static String versionName(Context c) {
        try { return info(c).versionName; } catch (Exception e) { return "?"; }
    }

    @SuppressWarnings("deprecation")
    public static long versionCode(Context c) {
        try {
            PackageInfo p = info(c);
            return android.os.Build.VERSION.SDK_INT >= 28 ? p.getLongVersionCode() : p.versionCode;
        } catch (Exception e) { return 0; }
    }

    private static PackageInfo info(Context c) throws Exception {
        return c.getPackageManager().getPackageInfo(c.getPackageName(), 0);
    }
}
