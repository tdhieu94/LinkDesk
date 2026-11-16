package vn.linkdesk;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.GestureDescription;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.view.View;
import android.view.WindowManager;
import android.os.Build;
import android.os.Bundle;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;

/** Dich vu Tro nang: thuc hien cham/vuot/go chu theo lenh tu may tinh. */
public class ControlService extends AccessibilityService {

    @Override
    protected void onServiceConnected() {
        super.onServiceConnected();
        Hub.control = this;
        Commands.broadcastStatus(this);
        MirrorService m = Hub.mirror;
        if (m != null) m.refreshAwake();
    }

    @Override public void onAccessibilityEvent(AccessibilityEvent event) {}
    @Override public void onInterrupt() {}

    @Override
    public void onDestroy() {
        setOverlay(false, false);
        if (Hub.control == this) Hub.control = null;
        MirrorService m = Hub.mirror;
        if (m != null) m.refreshAwake();
        Commands.broadcastStatus(this);
        super.onDestroy();
    }

    // ------------------------------------------------------------ cu chi


    public boolean tap(float x, float y, long durationMs) {
        Path p = new Path();
        p.moveTo(x, y);
        GestureDescription.StrokeDescription s = new GestureDescription.StrokeDescription(p, 0, Math.max(1, durationMs));
        return dispatchGesture(new GestureDescription.Builder().addStroke(s).build(), null, null);
    }

    public boolean swipe(float[] xs, float[] ys, long durationMs) {
        if (xs.length < 2) return tap(xs[0], ys[0], 40);
        Path p = new Path();
        p.moveTo(xs[0], ys[0]);
        for (int i = 1; i < xs.length; i++) p.lineTo(xs[i], ys[i]);
        long d = Math.max(20, Math.min(durationMs, Math.min(10_000, GestureDescription.getMaxGestureDuration())));
        GestureDescription.StrokeDescription s = new GestureDescription.StrokeDescription(p, 0, d);
        return dispatchGesture(new GestureDescription.Builder().addStroke(s).build(), null, null);
    }

    /** Giu yen tai diem dau (holdMs) roi keo theo duong — dung de keo-tha icon. */
    public boolean holdThenDrag(final float[] xs, final float[] ys, long holdMs, final long moveMs) {
        Path hold = new Path();
        hold.moveTo(xs[0], ys[0]);
        final GestureDescription.StrokeDescription first =
                new GestureDescription.StrokeDescription(hold, 0, Math.max(1, Math.min(holdMs, 5000)), true);
        return dispatchGesture(new GestureDescription.Builder().addStroke(first).build(), new GestureResultCallback() {
            @Override
            public void onCompleted(GestureDescription gd) {
                Path move = new Path();
                move.moveTo(xs[0], ys[0]);
                for (int i = 1; i < xs.length; i++) move.lineTo(xs[i], ys[i]);
                GestureDescription.StrokeDescription next =
                        first.continueStroke(move, 0, Math.max(20, Math.min(moveMs, 10_000)), false);
                dispatchGesture(new GestureDescription.Builder().addStroke(next).build(), null, null);
            }
        }, null);
    }

    public boolean scroll(float x, float y, float dyPx, int screenH) {
        float endY = Math.max(5, Math.min(screenH - 5, y - dyPx));
        if (Math.abs(endY - y) < 5) return false;
        return swipe(new float[]{x, x}, new float[]{y, endY}, 220);
    }

    public boolean global(int action) {
        return performGlobalAction(action);
    }

    // ------------------------------------------------------------ go chu

    private AccessibilityNodeInfo focusedInput() {
        AccessibilityNodeInfo n = findFocus(AccessibilityNodeInfo.FOCUS_INPUT);
        if (n != null && n.isEditable()) return n;
        return n;
    }

    private static CharSequence currentText(AccessibilityNodeInfo n) {
        CharSequence t = n.getText();
        if (t == null) return "";
        if (Build.VERSION.SDK_INT >= 26 && n.isShowingHintText()) return "";
        return t;
    }

    private static boolean setText(AccessibilityNodeInfo n, CharSequence text, int cursor) {
        Bundle b = new Bundle();
        b.putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, text);
        boolean ok = n.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, b);
        if (ok) setSelection(n, cursor, cursor);
        return ok;
    }

    private static void setSelection(AccessibilityNodeInfo n, int start, int end) {
        Bundle s = new Bundle();
        s.putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_START_INT, start);
        s.putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_END_INT, end);
        n.performAction(AccessibilityNodeInfo.ACTION_SET_SELECTION, s);
    }

    /** Tra ve null neu thanh cong, nguoc lai la thong bao loi. */
    public String typeText(String text) {
        AccessibilityNodeInfo n = focusedInput();
        if (n == null) return "Chưa có ô nhập chữ nào được chọn trên điện thoại. Hãy bấm vào ô nhập trước.";
        if (n.isPassword() || !n.isEditable()) {
            return paste(n, text) ? null : "Không gõ được vào ô này.";
        }
        CharSequence cur = currentText(n);
        int s = n.getTextSelectionStart(), e = n.getTextSelectionEnd();
        if (s < 0 || e < 0 || s > cur.length() || e > cur.length()) { s = e = cur.length(); }
        if (s > e) { int t = s; s = e; e = t; }
        String next = cur.subSequence(0, s) + text + cur.subSequence(e, cur.length());
        if (setText(n, next, s + text.length())) return null;
        return paste(n, text) ? null : "Ô nhập này không cho phép gõ từ xa.";
    }

    private boolean paste(AccessibilityNodeInfo n, String text) {
        ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        cm.setPrimaryClip(ClipData.newPlainText("LinkDesk", text));
        return n.performAction(AccessibilityNodeInfo.ACTION_PASTE);
    }

    public String editKey(String key) {
        AccessibilityNodeInfo n = focusedInput();
        if (n == null) {
            if ("enter".equals(key)) return null;
            return "Chưa chọn ô nhập chữ.";
        }
        CharSequence cur = currentText(n);
        int len = cur.length();
        int s = n.getTextSelectionStart(), e = n.getTextSelectionEnd();
        if (s < 0 || e < 0 || s > len || e > len) { s = e = len; }
        if (s > e) { int t = s; s = e; e = t; }
        switch (key) {
            case "backspace": {
                if (s == e) {
                    if (s == 0) return null;
                    s = s - 1;
                    if (s > 0 && Character.isLowSurrogate(cur.charAt(s)) && Character.isHighSurrogate(cur.charAt(s - 1))) s--;
                }
                setText(n, "" + cur.subSequence(0, s) + cur.subSequence(e, len), s);
                return null;
            }
            case "delete": {
                if (s == e) {
                    if (e >= len) return null;
                    e = e + 1;
                    if (e < len && Character.isLowSurrogate(cur.charAt(e))) e++;
                }
                setText(n, "" + cur.subSequence(0, s) + cur.subSequence(e, len), s);
                return null;
            }
            case "left": setSelection(n, Math.max(0, s - 1), Math.max(0, s - 1)); return null;
            case "right": setSelection(n, Math.min(len, e + 1), Math.min(len, e + 1)); return null;
            case "home": setSelection(n, 0, 0); return null;
            case "end": setSelection(n, len, len); return null;
            case "selectall": setSelection(n, 0, len); return null;
            case "enter": {
                if (Build.VERSION.SDK_INT >= 30) {
                    if (n.performAction(AccessibilityNodeInfo.AccessibilityAction.ACTION_IME_ENTER.getId())) return null;
                }
                if (n.isMultiLine()) return typeText("\n");
                return null;
            }
            case "copy": {
                if (s == e) return "Hãy bôi đen chữ cần sao chép trên điện thoại trước.";
                return "COPY:" + cur.subSequence(s, e);
            }
            case "cut": {
                if (s == e) return "Hãy bôi đen chữ cần cắt trước.";
                String sel = "" + cur.subSequence(s, e);
                setText(n, "" + cur.subSequence(0, s) + cur.subSequence(e, len), s);
                return "COPY:" + sel;
            }
            default: return "Phím không hỗ trợ: " + key;
        }
    }

    public void setClipboard(String text) {
        ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        cm.setPrimaryClip(ClipData.newPlainText("LinkDesk", text));
    }

    // ------------------------------------------------------------ giu sang / lam toi

    private View overlay;

    /**
     * Lop phu trong suot, khong nhan cham: giu man hinh khong tu tat (FLAG_KEEP_SCREEN_ON)
     * va neu dark=true thi ha do sang xuong thap nhat. Hinh gui sang may tinh van binh thuong.
     * Goi tren luong chinh. Tra ve false neu khong tao duoc.
     */
    public boolean setOverlay(boolean show, boolean dark) {
        WindowManager wm = (WindowManager) getSystemService(WINDOW_SERVICE);
        try {
            if (!show) {
                if (overlay != null) wm.removeView(overlay);
                overlay = null;
                return true;
            }
            WindowManager.LayoutParams lp = new WindowManager.LayoutParams(
                    WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT,
                    WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
                            | WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                    PixelFormat.TRANSLUCENT);
            lp.screenBrightness = dark ? WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_OFF
                    : WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE;
            if (overlay == null) {
                overlay = new View(this);
                wm.addView(overlay, lp);
            } else {
                wm.updateViewLayout(overlay, lp);
            }
            return true;
        } catch (Exception e) {
            overlay = null;
            return false;
        }
    }

    /** Tat dich vu Tro nang nay (dung cho che do ngan hang). */
    public void turnOff() {
        new android.os.Handler(android.os.Looper.getMainLooper()).post(new Runnable() {
            @Override public void run() { setOverlay(false, false); }
        });
        disableSelf();
    }
}
