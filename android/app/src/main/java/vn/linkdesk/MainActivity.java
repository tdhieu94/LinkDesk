package vn.linkdesk;

import android.Manifest;
import android.app.Activity;
import android.app.NotificationManager;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.media.projection.MediaProjectionConfig;
import android.media.projection.MediaProjectionManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.Window;
import android.widget.Button;
import android.widget.CompoundButton;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.util.List;

public class MainActivity extends Activity {
    public static final String EXTRA_AUTO_START = "autoStart";
    private static final int REQ_CAPTURE = 1;
    private static final int REQ_PERMS = 2;

    // Bang mau
    private static final int BG = 0xFFF4F6FA, CARD = 0xFFFFFFFF, TEXT = 0xFF1B1F24, MUTED = 0xFF68707A,
            BLUE = 0xFF2563EB, BLUE_SOFT = 0xFFE8EFFE, GREEN = 0xFF16A34A, GREEN_SOFT = 0xFFE7F6EC,
            ORANGE = 0xFFC2410C, ORANGE_SOFT = 0xFFFDEDE3, RED = 0xFFDC2626, RED_SOFT = 0xFFFDECEC, LINE = 0xFFE6E9EF;

    private TextView heroTitle, heroSub, addrText, pinText, statusDot;
    private Button mainBtn;
    private TextView controlChip, notifChip;
    private Button controlBtn, notifBtn;
    private LinearLayout updateCard;
    private TextView updateText;
    private Button updateBtn;
    private Updater.Info pendingUpdate;
    private boolean installing;
    private final Handler h = new Handler(Looper.getMainLooper());
    private boolean autoStarted;
    private final Runnable tick = new Runnable() {
        @Override public void run() { refresh(); h.postDelayed(this, 1000); }
    };

    private int dp(float v) {
        return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, getResources().getDisplayMetrics());
    }

    private TextView text(String s, float sp, int color, boolean bold) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(sp);
        t.setTextColor(color);
        if (bold) t.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        t.setLineSpacing(0, 1.15f);
        return t;
    }

    private GradientDrawable round(int color, float radiusDp) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(radiusDp));
        return g;
    }

    private Button button(String s, int bg, int fg, View.OnClickListener l) {
        Button b = new Button(this);
        b.setText(s);
        b.setAllCaps(false);
        b.setTextColor(fg);
        b.setTextSize(15);
        b.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        b.setStateListAnimator(null);
        b.setBackground(round(bg, 12));
        b.setOnClickListener(l);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(50));
        lp.topMargin = dp(12);
        b.setLayoutParams(lp);
        return b;
    }

    private LinearLayout card(LinearLayout parent) {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setPadding(dp(18), dp(16), dp(18), dp(16));
        GradientDrawable bg = round(CARD, 18);
        bg.setStroke(1, LINE);
        c.setBackground(bg);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.bottomMargin = dp(14);
        c.setLayoutParams(lp);
        parent.addView(c);
        return c;
    }

    private TextView section(LinearLayout parent, String s) {
        TextView t = text(s.toUpperCase(), 12, MUTED, true);
        t.setLetterSpacing(0.06f);
        t.setPadding(dp(4), dp(8), 0, dp(8));
        parent.addView(t);
        return t;
    }

    private TextView chip(String s, int fg, int bg) {
        TextView t = text(s, 12, fg, true);
        t.setPadding(dp(10), dp(4), dp(10), dp(4));
        t.setBackground(round(bg, 20));
        return t;
    }

    private void setChip(TextView t, String s, int fg, int bg) {
        t.setText(s);
        t.setTextColor(fg);
        t.setBackground(round(bg, 20));
    }

    /** Dong: tieu de + mo ta ben trai, chip trang thai ben phai. */
    private TextView permRow(LinearLayout parent, String title, String desc) {
        LinearLayout row = new LinearLayout(this);
        row.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout left = new LinearLayout(this);
        left.setOrientation(LinearLayout.VERTICAL);
        left.addView(text(title, 16, TEXT, true));
        TextView d = text(desc, 13, MUTED, false);
        d.setPadding(0, dp(2), dp(8), 0);
        left.addView(d);
        row.addView(left, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        TextView c = chip("", MUTED, LINE);
        row.addView(c);
        parent.addView(row);
        return c;
    }

    private Switch toggle(LinearLayout parent, String title, String desc, boolean checked, CompoundButton.OnCheckedChangeListener l) {
        LinearLayout row = new LinearLayout(this);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(0, dp(6), 0, dp(6));
        LinearLayout left = new LinearLayout(this);
        left.setOrientation(LinearLayout.VERTICAL);
        left.addView(text(title, 16, TEXT, true));
        TextView d = text(desc, 13, MUTED, false);
        d.setPadding(0, dp(2), dp(8), 0);
        left.addView(d);
        row.addView(left, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        Switch s = new Switch(this);
        s.setChecked(checked);
        s.setOnCheckedChangeListener(l);
        int[][] states = {{android.R.attr.state_checked}, {}};
        s.setThumbTintList(new ColorStateList(states, new int[]{BLUE, 0xFFFFFFFF}));
        s.setTrackTintList(new ColorStateList(states, new int[]{0x882563EB, 0xFFC9CED6}));
        row.addView(s);
        parent.addView(row);
        return s;
    }

    private View divider() {
        View v = new View(this);
        v.setBackgroundColor(LINE);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 1);
        lp.topMargin = dp(12);
        lp.bottomMargin = dp(12);
        v.setLayoutParams(lp);
        return v;
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        Window w = getWindow();
        w.setStatusBarColor(BG);
        w.setNavigationBarColor(BG);
        w.getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR | View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);

        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(BG);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(18), dp(20), dp(18), dp(28));
        scroll.addView(root);

        // ---- dau trang
        LinearLayout top = new LinearLayout(this);
        top.setGravity(Gravity.CENTER_VERTICAL);
        TextView logo = text("L", 22, Color.WHITE, true);
        logo.setGravity(Gravity.CENTER);
        logo.setBackground(round(BLUE, 12));
        top.addView(logo, new LinearLayout.LayoutParams(dp(42), dp(42)));
        LinearLayout tt = new LinearLayout(this);
        tt.setOrientation(LinearLayout.VERTICAL);
        tt.setPadding(dp(12), 0, 0, 0);
        tt.addView(text("LinkDesk", 22, TEXT, true));
        tt.addView(text("Điều khiển điện thoại từ máy tính · v" + BuildInfo.versionName(this), 13, MUTED, false));
        top.addView(tt);
        LinearLayout.LayoutParams tlp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        tlp.bottomMargin = dp(18);
        root.addView(top, tlp);

        // ---- the cap nhat (an cho den khi co ban moi)
        updateCard = card(root);
        updateCard.setBackground(round(BLUE_SOFT, 18));
        updateText = text("", 15, TEXT, true);
        updateCard.addView(updateText);
        updateBtn = button("Cập nhật ngay", BLUE, Color.WHITE, new View.OnClickListener() {
            @Override public void onClick(View v) { startUpdate(); }
        });
        updateCard.addView(updateBtn);
        updateCard.setVisibility(View.GONE);

        // ---- the trang thai chinh
        LinearLayout hero = card(root);
        LinearLayout hrow = new LinearLayout(this);
        hrow.setGravity(Gravity.CENTER_VERTICAL);
        statusDot = new TextView(this);
        statusDot.setBackground(round(MUTED, 6));
        hrow.addView(statusDot, new LinearLayout.LayoutParams(dp(12), dp(12)));
        heroTitle = text("", 20, TEXT, true);
        heroTitle.setPadding(dp(10), 0, 0, 0);
        hrow.addView(heroTitle);
        hero.addView(hrow);
        heroSub = text("", 14, MUTED, false);
        heroSub.setPadding(0, dp(6), 0, 0);
        hero.addView(heroSub);

        LinearLayout pinBox = new LinearLayout(this);
        pinBox.setOrientation(LinearLayout.VERTICAL);
        pinBox.setPadding(dp(14), dp(12), dp(14), dp(12));
        pinBox.setBackground(round(BG, 14));
        LinearLayout.LayoutParams plp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        plp.topMargin = dp(14);
        LinearLayout prow = new LinearLayout(this);
        prow.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout pl = new LinearLayout(this);
        pl.setOrientation(LinearLayout.VERTICAL);
        pl.addView(text("Mã PIN", 12, MUTED, true));
        pinText = text("", 28, TEXT, true);
        pinText.setLetterSpacing(0.2f);
        pl.addView(pinText);
        prow.addView(pl, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        TextView newPin = text("Đổi PIN", 14, BLUE, true);
        newPin.setPadding(dp(10), dp(8), dp(4), dp(8));
        newPin.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Prefs.newPin(MainActivity.this);
                Hub.closeAllClients();
                refresh();
                Toast.makeText(MainActivity.this, "Đã đổi PIN, máy tính đang kết nối sẽ phải nhập lại", Toast.LENGTH_SHORT).show();
            }
        });
        prow.addView(newPin);
        pinBox.addView(prow);
        addrText = text("", 13, MUTED, false);
        addrText.setTextIsSelectable(true);
        addrText.setPadding(0, dp(6), 0, 0);
        pinBox.addView(addrText);
        hero.addView(pinBox, plp);

        mainBtn = button("", BLUE, Color.WHITE, new View.OnClickListener() {
            @Override public void onClick(View v) { toggleMirror(); }
        });
        hero.addView(mainBtn);

        // ---- man hinh dien thoai
        section(root, "Màn hình điện thoại khi đang kết nối");
        LinearLayout disp = card(root);
        toggle(disp, "Không tự tắt màn hình", "Giữ kết nối, không bị ngắt khi màn hình tự tắt", Prefs.getKeepAwake(this),
                new CompoundButton.OnCheckedChangeListener() {
                    @Override public void onCheckedChanged(CompoundButton b, boolean on) {
                        if (Hub.mirror != null) Hub.mirror.setKeepAwake(on); else Prefs.setKeepAwake(MainActivity.this, on);
                    }
                });
        disp.addView(divider());
        toggle(disp, "Làm tối màn hình", "Màn hình điện thoại tối gần như tắt, máy tính vẫn thấy hình bình thường", Prefs.getDark(this),
                new CompoundButton.OnCheckedChangeListener() {
                    @Override public void onCheckedChanged(CompoundButton b, boolean on) {
                        if (Hub.mirror != null) Hub.mirror.setDark(on); else Prefs.setDark(MainActivity.this, on);
                    }
                });

        // ---- quyen
        section(root, "Quyền");
        LinearLayout perms = card(root);
        controlChip = permRow(perms, "Điều khiển (Trợ năng)", "Chạm, vuốt, gõ chữ, làm tối màn hình");
        controlBtn = button("", BLUE_SOFT, BLUE, new View.OnClickListener() {
            @Override public void onClick(View v) { toggleControl(); }
        });
        perms.addView(controlBtn);
        perms.addView(divider());
        notifChip = permRow(perms, "Thông báo", "Hiện và trả lời tin nhắn trên máy tính");
        notifBtn = button("Cấp quyền thông báo", BLUE_SOFT, BLUE, new View.OnClickListener() {
            @Override public void onClick(View v) { startActivity(new Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)); }
        });
        perms.addView(notifBtn);

        // ---- ngan hang
        section(root, "App ngân hàng");
        LinearLayout bank = card(root);
        bank.addView(text("Trước khi mở app ngân hàng, bấm nút này để dừng chia sẻ và tắt Trợ năng của LinkDesk.", 14, MUTED, false));
        bank.addView(button("Chế độ ngân hàng — tắt hết", RED_SOFT, RED, new View.OnClickListener() {
            @Override public void onClick(View v) {
                Commands.bankMode(MainActivity.this);
                if (!SecureSettings.canWrite(MainActivity.this) && Hub.control == null && SecureSettings.isAccessibilityOn(MainActivity.this)) {
                    openAccessibilitySettings();
                }
                h.postDelayed(new Runnable() { @Override public void run() { refresh(); } }, 500);
            }
        }));

        // ---- meo
        section(root, "Mẹo cho Xiaomi / HyperOS");
        LinearLayout tips = card(root);
        tips.addView(text("• Không bật được Trợ năng (\"Cài đặt bị hạn chế\"): Thông tin ứng dụng → ⋮ → Cho phép cài đặt bị hạn chế.\n"
                + "• Tiết kiệm pin: Không hạn chế, bật Tự khởi động.\n"
                + "• Điện thoại và máy tính dùng chung WiFi.", 14, MUTED, false));
        tips.addView(button("Mở Thông tin ứng dụng", BG, TEXT, new View.OnClickListener() {
            @Override public void onClick(View v) {
                startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getPackageName())));
            }
        }));

        setContentView(scroll);
        if (getIntent() != null && getIntent().getBooleanExtra(EXTRA_AUTO_START, false)) autoStart();
        else askRuntimePermissions();
        checkUpdate();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        if (intent != null && intent.getBooleanExtra(EXTRA_AUTO_START, false)) autoStart();
    }

    /** Duoc goi tu dong sau khi mo khoa may (chi khi Android khong can hoi lai). */
    private void autoStart() {
        if (Hub.mirror != null && Hub.mirror.isProjecting()) { moveTaskToBack(true); return; }
        autoStarted = true;
        requestCapture();
    }

    // ------------------------------------------------------------ cap nhat
    private long lastCheck;

    private void checkUpdate() {
        if (System.currentTimeMillis() - lastCheck < 30 * 60 * 1000L) return;
        lastCheck = System.currentTimeMillis();
        Updater.check(this, new Updater.Listener() {
            @Override public void onResult(Updater.Info newer) {
                pendingUpdate = newer;
                if (newer == null) { updateCard.setVisibility(View.GONE); return; }
                updateText.setText("Có bản mới " + newer.versionName + (newer.notes.isEmpty() ? "" : "\n" + newer.notes));
                updateCard.setVisibility(View.VISIBLE);
            }
        });
    }

    private void startUpdate() {
        if (pendingUpdate == null || installing) return;
        if (!Updater.canInstall(this)) {
            Toast.makeText(this, "Hãy bật \"Cho phép từ nguồn này\" cho LinkDesk rồi quay lại bấm Cập nhật", Toast.LENGTH_LONG).show();
            Updater.openInstallPermission(this);
            return;
        }
        installing = true;
        updateBtn.setText("Đang tải… 0%");
        Updater.install(this, pendingUpdate, new Updater.Progress() {
            @Override public void onProgress(int percent) { updateBtn.setText("Đang tải… " + percent + "%"); }
            @Override public void onDone(String error) {
                installing = false;
                if (error != null) {
                    updateBtn.setText("Thử lại");
                    Toast.makeText(MainActivity.this, error, Toast.LENGTH_LONG).show();
                } else {
                    updateBtn.setText("Đang cài… (bấm Cập nhật trên hộp thoại)");
                }
            }
        });
    }

    // ------------------------------------------------------------ trang thai

    private void askRuntimePermissions() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, REQ_PERMS);
        } else if (Build.VERSION.SDK_INT < 29 && checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.WRITE_EXTERNAL_STORAGE}, REQ_PERMS);
        }
    }

    @Override protected void onResume() { super.onResume(); h.post(tick); checkUpdate(); }
    @Override protected void onPause() { super.onPause(); h.removeCallbacks(tick); }

    private boolean notifGranted() {
        NotificationManager nm = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 27) return nm.isNotificationListenerAccessGranted(new ComponentName(this, NotifService.class));
        String v = Settings.Secure.getString(getContentResolver(), "enabled_notification_listeners");
        return v != null && v.contains(getPackageName());
    }

    private void refresh() {
        List<String> ips = Net.localIps();
        pinText.setText(Prefs.getPin(this));
        if (ips.isEmpty()) {
            addrText.setText("Chưa kết nối WiFi");
        } else {
            StringBuilder sb = new StringBuilder("Địa chỉ: ");
            for (int i = 0; i < ips.size(); i++) { if (i > 0) sb.append(" · "); sb.append(ips.get(i)); }
            addrText.setText(sb.toString());
        }

        boolean server = MirrorService.running;
        boolean mirror = server && Hub.mirror != null && Hub.mirror.isProjecting();
        int n = Hub.clientCount();
        int dot;
        if (mirror && n > 0) {
            heroTitle.setText("Máy tính đang kết nối");
            heroSub.setText(n == 1 ? "Đang chia sẻ màn hình với 1 máy tính." : "Đang chia sẻ màn hình với " + n + " máy tính.");
            dot = GREEN;
        } else if (mirror) {
            heroTitle.setText("Sẵn sàng");
            heroSub.setText("Mở LinkDesk trên máy tính, chọn điện thoại này và nhập mã PIN bên dưới.");
            dot = BLUE;
        } else if (server) {
            heroTitle.setText("Đã dừng chia sẻ hình");
            heroSub.setText("Thường do khoá máy. Máy tính vẫn kết nối và nhận thông báo. Bấm nút dưới để chia sẻ lại.");
            dot = ORANGE;
        } else {
            heroTitle.setText("Đang tắt");
            heroSub.setText("Bấm Bắt đầu để máy tính có thể kết nối.");
            dot = MUTED;
        }
        statusDot.setBackground(round(dot, 6));
        mainBtn.setText(mirror ? "Dừng chia sẻ" : server ? "Chia sẻ lại màn hình" : "Bắt đầu");
        mainBtn.setBackground(round(mirror ? RED_SOFT : BLUE, 12));
        mainBtn.setTextColor(mirror ? RED : Color.WHITE);

        boolean control = Hub.control != null;
        boolean canWrite = SecureSettings.canWrite(this);
        if (control) setChip(controlChip, "Đang bật", GREEN, GREEN_SOFT);
        else setChip(controlChip, "Đang tắt", ORANGE, ORANGE_SOFT);
        controlBtn.setText(control ? "Tắt điều khiển" : (canWrite ? "Bật điều khiển" : "Bật trong cài đặt Trợ năng"));

        boolean notif = notifGranted();
        if (notif) setChip(notifChip, Hub.notif != null ? "Đang bật" : "Đang chờ…", GREEN, GREEN_SOFT);
        else setChip(notifChip, "Chưa cấp", ORANGE, ORANGE_SOFT);
        notifBtn.setVisibility(notif ? View.GONE : View.VISIBLE);
    }

    private void openAccessibilitySettings() {
        startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));
        Toast.makeText(this, "Tìm LinkDesk trong danh sách (thường ở mục \"Ứng dụng đã tải xuống\")", Toast.LENGTH_LONG).show();
    }

    private void toggleControl() {
        boolean on = Hub.control != null;
        if (on) {
            Hub.control.turnOff();
            SecureSettings.setAccessibility(this, false);
        } else if (!SecureSettings.setAccessibility(this, true)) {
            openAccessibilitySettings();
        }
        h.postDelayed(new Runnable() { @Override public void run() { refresh(); } }, 600);
    }

    private void toggleMirror() {
        if (MirrorService.running && Hub.mirror != null && Hub.mirror.isProjecting()) {
            startService(new Intent(this, MirrorService.class).setAction(MirrorService.ACTION_STOP));
            h.postDelayed(new Runnable() { @Override public void run() { refresh(); } }, 400);
            return;
        }
        requestCapture();
    }

    private void requestCapture() {
        MediaProjectionManager mpm = (MediaProjectionManager) getSystemService(MEDIA_PROJECTION_SERVICE);
        Intent i;
        if (Build.VERSION.SDK_INT >= 34) {
            // Bat buoc chia se ca man hinh (khong chon 1 app) de toa do cham dung
            i = mpm.createScreenCaptureIntent(MediaProjectionConfig.createConfigForDefaultDisplay());
        } else {
            i = mpm.createScreenCaptureIntent();
        }
        startActivityForResult(i, REQ_CAPTURE);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_CAPTURE) return;
        boolean auto = autoStarted;
        autoStarted = false;
        if (resultCode != RESULT_OK || data == null) {
            Toast.makeText(this, "Bạn chưa cho phép chia sẻ màn hình", Toast.LENGTH_SHORT).show();
            if (auto) moveTaskToBack(true);
            return;
        }
        Intent svc = new Intent(this, MirrorService.class)
                .setAction(MirrorService.ACTION_START)
                .putExtra(MirrorService.EXTRA_CODE, resultCode)
                .putExtra(MirrorService.EXTRA_DATA, data);
        startForegroundService(svc);
        if (auto) moveTaskToBack(true);
        h.postDelayed(new Runnable() { @Override public void run() { refresh(); } }, 800);
    }
}
