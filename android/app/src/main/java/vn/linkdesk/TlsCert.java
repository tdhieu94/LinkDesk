package vn.linkdesk;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.math.BigInteger;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.KeyStore;
import java.security.MessageDigest;
import java.security.PrivateKey;
import java.security.SecureRandom;
import java.security.Signature;
import java.security.cert.Certificate;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.security.spec.ECGenParameterSpec;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

import javax.net.ssl.KeyManagerFactory;
import javax.net.ssl.SSLContext;

/**
 * Chung chi TLS tu ky cho rieng dien thoai nay (EC P-256), tao 1 lan va luu trong bo nho cua app.
 * May tinh ghi nho "dau van tay" (SHA-256) cua chung chi o lan ghep doi dau tien.
 */
public final class TlsCert {
    private TlsCert() {}
    private static final char[] PW = "linkdesk".toCharArray();
    private static SSLContext ctx;
    private static String fp;

    public static synchronized SSLContext context(File dir) throws Exception {
        if (ctx != null) return ctx;
        File f = new File(dir, "tls.p12");
        KeyStore ks = KeyStore.getInstance("PKCS12");
        if (f.exists()) {
            try (FileInputStream in = new FileInputStream(f)) { ks.load(in, PW); }
        } else {
            ks.load(null, null);
            KeyPairGenerator g = KeyPairGenerator.getInstance("EC");
            g.initialize(new ECGenParameterSpec("secp256r1"), new SecureRandom());
            KeyPair kp = g.generateKeyPair();
            X509Certificate cert = selfSign(kp);
            ks.setKeyEntry("linkdesk", kp.getPrivate(), PW, new Certificate[]{cert});
            File tmp = new File(dir, "tls.p12.tmp");
            try (FileOutputStream out = new FileOutputStream(tmp)) { ks.store(out, PW); }
            if (!tmp.renameTo(f)) throw new IllegalStateException("save cert");
        }
        Certificate c = ks.getCertificate("linkdesk");
        fp = hex(MessageDigest.getInstance("SHA-256").digest(c.getEncoded()));
        KeyManagerFactory kmf = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm());
        kmf.init(ks, PW);
        SSLContext s = SSLContext.getInstance("TLS");
        s.init(kmf.getKeyManagers(), null, new SecureRandom());
        ctx = s;
        return s;
    }

    /** SHA-256 cua chung chi (hex in hoa), hoac null neu chua tao. */
    public static synchronized String fingerprint() { return fp; }

    /** Ma ngan de nguoi dung so sanh giua dien thoai va may tinh, vd "3F9A 1C07". */
    public static String shortCode(String fp) {
        if (fp == null || fp.length() < 8) return "";
        return fp.substring(0, 4) + " " + fp.substring(4, 8);
    }

    static String hex(byte[] b) {
        StringBuilder sb = new StringBuilder();
        for (byte x : b) sb.append(String.format(Locale.ROOT, "%02X", x & 0xff));
        return sb.toString();
    }

    // ------------------------------------------------------------ DER toi gian
    private static byte[] tlv(int tag, byte[]... parts) {
        ByteArrayOutputStream body = new ByteArrayOutputStream();
        for (byte[] p : parts) body.write(p, 0, p.length);
        int n = body.size();
        ByteArrayOutputStream o = new ByteArrayOutputStream();
        o.write(tag);
        if (n < 128) o.write(n);
        else if (n < 256) { o.write(0x81); o.write(n); }
        else { o.write(0x82); o.write(n >> 8); o.write(n & 0xff); }
        byte[] b = body.toByteArray();
        o.write(b, 0, b.length);
        return o.toByteArray();
    }

    private static byte[] seq(byte[]... p) { return tlv(0x30, p); }

    private static byte[] time(Date d) {
        // RFC 5280: UTCTime truoc nam 2050, GeneralizedTime tu 2050
        boolean utc = d.getTime() < 2524608000000L;
        SimpleDateFormat f = new SimpleDateFormat(utc ? "yyMMddHHmmss'Z'" : "yyyyMMddHHmmss'Z'", Locale.ROOT);
        f.setTimeZone(TimeZone.getTimeZone("UTC"));
        return tlv(utc ? 0x17 : 0x18, f.format(d).getBytes());
    }

    private static X509Certificate selfSign(KeyPair kp) throws Exception {
        // ecdsa-with-SHA256 1.2.840.10045.4.3.2
        byte[] alg = seq(new byte[]{0x06, 0x08, 0x2A, (byte) 0x86, 0x48, (byte) 0xCE, 0x3D, 0x04, 0x03, 0x02});
        byte[] name = seq(tlv(0x31, seq(new byte[]{0x06, 0x03, 0x55, 0x04, 0x03}, tlv(0x0C, "LinkDesk".getBytes("UTF-8")))));
        long now = System.currentTimeMillis();
        byte[] validity = seq(time(new Date(now - 86400000L)), time(new Date(now + 30L * 365 * 86400000L)));
        byte[] serial = tlv(0x02, new BigInteger(63, new SecureRandom()).add(BigInteger.ONE).toByteArray());
        byte[] tbs = seq(serial, alg, name, validity, name, kp.getPublic().getEncoded());
        Signature s = Signature.getInstance("SHA256withECDSA");
        s.initSign((PrivateKey) kp.getPrivate());
        s.update(tbs);
        byte[] sig = s.sign();
        byte[] bits = new byte[sig.length + 1];
        System.arraycopy(sig, 0, bits, 1, sig.length);
        byte[] der = seq(tbs, alg, tlv(0x03, bits));
        return (X509Certificate) CertificateFactory.getInstance("X.509").generateCertificate(new ByteArrayInputStream(der));
    }
}
