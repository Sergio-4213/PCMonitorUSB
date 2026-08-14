package com.pcmonitorusb.model;

import java.util.ArrayList;
import java.util.List;

public final class PanelConfig {
    public String language = "";
    public int updateIntervalMs = 1000;
    public boolean showCpu = true;
    public boolean showGpu = true;
    public boolean showRam = true;
    public boolean showVram = true;
    public boolean showNetwork;
    public boolean showDisk;
    public boolean showFps;
    public double cpuElevated = 75;
    public double cpuCritical = 90;
    public double gpuElevated = 75;
    public double gpuCritical = 90;
    public boolean wakeOnLanEnabled = true;
    public boolean wakeOnLanAvailable;
    public String wakeComputerName = "";
    public String wakeMacAddress = "";
    public String wakeBroadcastAddress = "";
    public int wakePort = 9;
    public String wakeReason = "";
    public final List<PanelButton> buttons = new ArrayList<>();

    public static final class PanelButton {
        public final String id;
        public final String label;
        public final String icon;
        public final boolean available;

        public PanelButton(String id, String label, String icon, boolean available) {
            this.id = id;
            this.label = label;
            this.icon = icon;
            this.available = available;
        }

        public PanelButton(String id, String label, boolean available) {
            this(id, label, "", available);
        }
    }
}
