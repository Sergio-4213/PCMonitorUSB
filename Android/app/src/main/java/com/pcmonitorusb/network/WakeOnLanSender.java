package com.pcmonitorusb.network;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.Inet4Address;
import java.net.InetAddress;

public final class WakeOnLanSender {
    private WakeOnLanSender() { }

    public static void send(String macAddress, String broadcastAddress, int port) throws Exception {
        byte[] mac = parseMac(macAddress);
        InetAddress destination = InetAddress.getByName(broadcastAddress);
        if (!(destination instanceof Inet4Address) || destination.isLoopbackAddress() ||
                destination.isMulticastAddress() || destination.isAnyLocalAddress())
            throw new IllegalArgumentException("Invalid broadcast address");
        if (port != 9) throw new IllegalArgumentException("Invalid Wake-on-LAN port");

        byte[] magicPacket = new byte[6 + 16 * mac.length];
        for (int index = 0; index < 6; index++) magicPacket[index] = (byte) 0xFF;
        for (int repeat = 0; repeat < 16; repeat++)
            System.arraycopy(mac, 0, magicPacket, 6 + repeat * mac.length, mac.length);

        try (DatagramSocket socket = new DatagramSocket()) {
            socket.setBroadcast(true);
            DatagramPacket packet = new DatagramPacket(magicPacket, magicPacket.length, destination, port);
            socket.send(packet);
            socket.send(packet);
            socket.send(packet);
        }
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
