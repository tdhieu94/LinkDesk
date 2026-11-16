package vn.linkdesk;

import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class Net {
    private Net() {}

    /** Tat ca dia chi IPv4 noi bo (wlan truoc). */
    public static List<String> localIps() {
        List<String> wifi = new ArrayList<>();
        List<String> other = new ArrayList<>();
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp() || ni.isLoopback()) continue;
                String name = ni.getName();
                if (name.startsWith("rmnet") || name.startsWith("ccmni") || name.startsWith("dummy")) continue;
                for (InetAddress a : Collections.list(ni.getInetAddresses())) {
                    if (a instanceof Inet4Address && a.isSiteLocalAddress()) {
                        if (name.startsWith("wlan") || name.startsWith("ap") || name.startsWith("swlan")) wifi.add(a.getHostAddress());
                        else other.add(a.getHostAddress());
                    }
                }
            }
        } catch (Exception ignored) {}
        wifi.addAll(other);
        return wifi;
    }

    public static String bestIp() {
        List<String> l = localIps();
        return l.isEmpty() ? "?.?.?.?" : l.get(0);
    }
}
