package com.pcmonitorusb.network;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Build;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.util.LinkedHashSet;
import java.util.Set;

public final class WakeOnLanSender {
    private WakeOnLanSender() { }

    public static int send(Context context, String macAddress, String broadcastAddress, int port) throws Exception {
        byte[] mac = parseMac(macAddress);
        InetAddress configuredDestination = validateDestination(broadcastAddress);
        if (port != 9) throw new IllegalArgumentException("Invalid Wake-on-LAN port");

        byte[] magicPacket = new byte[6 + 16 * mac.length];
        for (int index = 0; index < 6; index++) magicPacket[index] = (byte) 0xFF;
        for (int repeat = 0; repeat < 16; repeat++)
            System.arraycopy(mac, 0, magicPacket, 6 + repeat * mac.length, mac.length);

        ConnectivityManager manager = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
        Network wifiNetwork = findWifiNetwork(manager);
        Set<InetAddress> destinations = new LinkedHashSet<>();
        destinations.add(configuredDestination);
        InetAddress currentWifiBroadcast = findWifiBroadcast(manager, wifiNetwork);
        if (currentWifiBroadcast != null) destinations.add(currentWifiBroadcast);
        destinations.add(InetAddress.getByName("255.255.255.255"));

        int sent = 0;
        Exception lastFailure = null;
        try (DatagramSocket socket = openWifiSocket(wifiNetwork)) {
            socket.setBroadcast(true);
            int[] ports = {9, 7};
            for (int repeat = 0; repeat < 6; repeat++) {
                for (InetAddress destination : destinations) {
                    for (int targetPort : ports) {
                        try {
                            socket.send(new DatagramPacket(magicPacket, magicPacket.length, destination, targetPort));
                            sent++;
                        } catch (Exception failure) {
                            lastFailure = failure;
                        }
                    }
                }
                if (repeat < 5) Thread.sleep(120);
            }
        }
        if (sent == 0) {
            if (lastFailure != null) throw lastFailure;
            throw new IllegalStateException("No Wake-on-LAN packet was sent");
        }
        return sent;
    }

    private static InetAddress validateDestination(String address) throws Exception {
        InetAddress destination = InetAddress.getByName(address);
        if (!(destination instanceof Inet4Address) || destination.isLoopbackAddress() ||
                destination.isMulticastAddress() || destination.isAnyLocalAddress())
            throw new IllegalArgumentException("Invalid broadcast address");
        return destination;
    }

    private static Network findWifiNetwork(ConnectivityManager manager) {
        if (manager == null) return null;
        for (Network network : manager.getAllNetworks()) {
            NetworkCapabilities capabilities = manager.getNetworkCapabilities(network);
            if (capabilities != null && capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) return network;
        }
        return null;
    }

    private static InetAddress findWifiBroadcast(ConnectivityManager manager, Network network) {
        if (manager == null || network == null) return null;
        LinkProperties properties = manager.getLinkProperties(network);
        if (properties == null) return null;
        for (LinkAddress link : properties.getLinkAddresses()) {
            InetAddress address = link.getAddress();
            int prefix = link.getPrefixLength();
            if (address instanceof Inet4Address && !address.isLoopbackAddress() && prefix >= 1 && prefix <= 30) {
                byte[] bytes = address.getAddress();
                int value = ((bytes[0] & 0xff) << 24) | ((bytes[1] & 0xff) << 16) |
                        ((bytes[2] & 0xff) << 8) | (bytes[3] & 0xff);
                int mask = -1 << (32 - prefix);
                int broadcast = value | ~mask;
                try {
                    return InetAddress.getByAddress(new byte[] {
                            (byte) (broadcast >>> 24), (byte) (broadcast >>> 16),
                            (byte) (broadcast >>> 8), (byte) broadcast
                    });
                } catch (Exception ignored) { return null; }
            }
        }
        return null;
    }

    private static DatagramSocket openWifiSocket(Network network) throws Exception {
        DatagramSocket socket = new DatagramSocket();
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && network != null) {
            try {
                network.bindSocket(socket);
            } catch (Exception ignored) {
                socket.close();
                socket = new DatagramSocket();
            }
        }
        return socket;
    }

    static byte[] parseMac(String value) {
        if (value == null) throw new IllegalArgumentException("Missing MAC address");
        String compact = value.replace(":", "").replace("-", "");
        if (!compact.matches("[0-9A-Fa-f]{12}")) throw new IllegalArgumentException("Invalid MAC address");
        byte[] result = new byte[6];
        for (int index = 0; index < result.length; index++)
            result[index] = (byte) Integer.parseInt(compact.substring(index * 2, index * 2 + 2), 16);
        return result;
    }
}
