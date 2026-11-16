package vn.linkdesk;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;

/** Chia se anh tu may tinh vao clipboard dien thoai (content://vn.linkdesk.clip/<ten>). */
public class ClipProvider extends ContentProvider {
    public static final String AUTH = "vn.linkdesk.clip";
    public static final long MAX = 30L * 1024 * 1024;

    static File dir(Context c) {
        File d = new File(c.getCacheDir(), "clip");
        //noinspection ResultOfMethodCallIgnored
        d.mkdirs();
        return d;
    }

    /** Doc anh tu luong, luu lai va dat vao clipboard. Tra ve loi hoac null. */
    public static String receive(Context c, InputStream in, long len, String mime) throws IOException {
        if (len <= 0 || len > MAX) return "Ảnh quá lớn";
        File d = dir(c);
        File[] old = d.listFiles();
        if (old != null) for (File f : old) //noinspection ResultOfMethodCallIgnored
            f.delete();
        String ext = mime != null && mime.contains("jpeg") ? ".jpg" : ".png";
        String name = "anh-" + System.currentTimeMillis() + ext;
        File f = new File(d, name);
        FileOutputStream fo = new FileOutputStream(f);
        byte[] buf = new byte[65536];
        long left = len;
        try {
            while (left > 0) {
                int n = in.read(buf, 0, (int) Math.min(buf.length, left));
                if (n < 0) throw new IOException("ket noi bi ngat");
                fo.write(buf, 0, n);
                left -= n;
            }
        } finally { fo.close(); }
        Uri u = Uri.parse("content://" + AUTH + "/" + name);
        Context ctx = Hub.control != null ? Hub.control : c;
        ClipboardManager cm = (ClipboardManager) ctx.getSystemService(Context.CLIPBOARD_SERVICE);
        cm.setPrimaryClip(ClipData.newUri(ctx.getContentResolver(), "LinkDesk", u));
        return null;
    }

    private File file(Uri u) throws FileNotFoundException {
        String n = u.getLastPathSegment();
        if (n == null || n.contains("/") || n.startsWith(".")) throw new FileNotFoundException();
        File f = new File(dir(getContext()), n);
        if (!f.isFile()) throw new FileNotFoundException();
        return f;
    }

    @Override public boolean onCreate() { return true; }

    @Override public String getType(Uri u) {
        String n = u.getLastPathSegment();
        return n != null && n.endsWith(".jpg") ? "image/jpeg" : "image/png";
    }

    @Override public ParcelFileDescriptor openFile(Uri u, String mode) throws FileNotFoundException {
        return ParcelFileDescriptor.open(file(u), ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override public Cursor query(Uri u, String[] proj, String sel, String[] args, String sort) {
        File f;
        try { f = file(u); } catch (FileNotFoundException e) { return null; }
        if (proj == null) proj = new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE};
        MatrixCursor c = new MatrixCursor(proj, 1);
        Object[] row = new Object[proj.length];
        for (int i = 0; i < proj.length; i++) {
            if (OpenableColumns.DISPLAY_NAME.equals(proj[i])) row[i] = f.getName();
            else if (OpenableColumns.SIZE.equals(proj[i])) row[i] = f.length();
        }
        c.addRow(row);
        return c;
    }

    @Override public Uri insert(Uri u, ContentValues v) { return null; }
    @Override public int delete(Uri u, String s, String[] a) { return 0; }
    @Override public int update(Uri u, ContentValues v, String s, String[] a) { return 0; }
}
