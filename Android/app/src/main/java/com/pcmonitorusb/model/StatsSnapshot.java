package com.pcmonitorusb.model;

public final class StatsSnapshot {
    public final Cpu cpu = new Cpu();
    public final Gpu gpu = new Gpu();
    public final Ram ram = new Ram();
    public final Network network = new Network();
    public final Disk disk = new Disk();
    public double fps = Double.NaN;

    public static final class Cpu {
        public String name = "CPU";
        public double temperature = Double.NaN;
        public double usage = Double.NaN;
        public double clock = Double.NaN;
        public double power = Double.NaN;
    }

    public static final class Gpu {
        public String name = "GPU";
        public double temperature = Double.NaN;
        public double hotspot = Double.NaN;
        public double usage = Double.NaN;
        public double clock = Double.NaN;
        public double vramClock = Double.NaN;
        public double vramUsed = Double.NaN;
        public double vramTotal = Double.NaN;
        public double power = Double.NaN;
        public double fanRPM = Double.NaN;
        public double fanPercent = Double.NaN;
    }

    public static final class Ram {
        public double used = Double.NaN;
        public double total = Double.NaN;
        public double usage = Double.NaN;
    }

    public static final class Network {
        public double download = Double.NaN;
        public double upload = Double.NaN;
    }

    public static final class Disk {
        public double activity = Double.NaN;
        public double mainUsage = Double.NaN;
    }
}
