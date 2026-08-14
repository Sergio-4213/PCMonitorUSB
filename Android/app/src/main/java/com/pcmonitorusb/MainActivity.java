package com.pcmonitorusb;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.res.Configuration;
import android.net.ConnectivityManager;
import android.net.NetworkInfo;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.GridLayout;
import android.widget.PopupMenu;
import android.widget.Space;
import android.widget.TextView;

import com.pcmonitorusb.model.PanelConfig;
import com.pcmonitorusb.model.StatsSnapshot;
import com.pcmonitorusb.network.ApiClient;
import com.pcmonitorusb.network.WakeOnLanSender;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ScheduledThreadPoolExecutor;
import java.util.concurrent.TimeUnit;

public final class MainActivity extends Activity {
    private static final String LOG_TAG = "PCMonitorUSB";
    private static final int COLOR_NORMAL = Color.rgb(102, 217, 142);
    private static final int COLOR_ELEVATED = Color.rgb(242, 166, 64);
    private static final int COLOR_CRITICAL = Color.rgb(238, 84, 84);
    private static final int COLOR_PRIMARY = Color.rgb(242, 243, 245);
    private static final int COLOR_SECONDARY = Color.rgb(169, 175, 184);
    private static final long PROTECTION_INTERVAL_MS = 10 * 60 * 1000L;
    private static final String WAKE_PREFS = "wake_on_lan";

    private final Handler ui = new Handler(Looper.getMainLooper());
    private final ScheduledThreadPoolExecutor worker = new ScheduledThreadPoolExecutor(1);
    private final Map<String, Button> commandButtons = new HashMap<>();
    private ApiClient api;
    private volatile PanelConfig panelConfig = new PanelConfig();
    private volatile StatsSnapshot lastStats;
    private volatile boolean destroyed;
    private volatile int consecutiveFailures;
    private volatile long lastConfigFetch;
    private volatile long lastSuccess;
    private boolean controlMode;
    private boolean protectionEnabled;
    private String appliedLanguage = "";
    private View root;
    private View monitorContent;
    private View controlPanel;
    private View modeBar;
    private View wakePanel;
    private View sensorArea;
    private View cpuCard;
    private View gpuCard;
    private View memoryCard;
    private View cpuDetails;
    private View gpuDetails;
    private View networkGroup;
    private View diskGroup;
    private View fpsGroup;
    private GridLayout buttonGrid;
    private TextView connectionStatus;
    private TextView message;
    private TextView cpuName;
    private TextView cpuTemp;
    private TextView cpuUsage;
    private TextView cpuClock;
    private TextView cpuPower;
    private TextView gpuName;
    private TextView gpuTemp;
    private TextView gpuUsage;
    private TextView gpuHotspot;
    private TextView gpuClock;
    private TextView gpuVramClock;
    private TextView gpuPower;
    private TextView gpuFan;
    private TextView ramValue;
    private TextView ramUsage;
    private TextView vramValue;
    private TextView downloadValue;
    private TextView uploadValue;
    private TextView diskActivity;
    private TextView diskUsage;
    private TextView fpsValue;
    private TextView controlCpuValue;
    private TextView controlGpuValue;
    private TextView controlCpuDetails;
    private TextView controlGpuDetails;
    private TextView controlRamValue;
    private TextView controlVramValue;
    private TextView controlFpsValue;
    private Button modeMonitor;
    private Button modeControl;
    private Button wakeButton;
    private TextView wakeComputer;
    private TextView wakeStatus;

    private final Runnable pollTask = new Runnable() {
        @Override public void run() {
            if (destroyed) return;
            try {
                StatsSnapshot stats = api.fetchStats();
                PanelConfig freshConfig = null;
                long now = SystemClock.elapsedRealtime();
                if (now - lastConfigFetch >= 10000 || lastConfigFetch == 0) {
                    freshConfig = api.fetchConfig();
                    panelConfig = freshConfig;
                    saveWakeConfiguration(freshConfig);
                    lastConfigFetch = now;
                }
                lastStats = stats;
                consecutiveFailures = 0;
                lastSuccess = now;
                final PanelConfig updatedConfig = freshConfig;
                ui.post(() -> {
                    if (destroyed) return;
                    showConnected();
                    if (updatedConfig != null) {
                        if (applyServerLanguage(updatedConfig.language)) bindLayout();
                        else applyPanelConfig(updatedConfig);
                    }
                    renderStats(stats);
                });
            } catch (Exception ignored) {
                consecutiveFailures++;
                if (consecutiveFailures >= 3) ui.post(() -> showDisconnected());
            } finally {
                if (!destroyed) {
                    int delay = Math.max(500, Math.min(2000, panelConfig.updateIntervalMs));
                    worker.schedule(this, delay, TimeUnit.MILLISECONDS);
                }
            }
        }
    };

    private final Runnable protectionTask = new Runnable() {
        @Override public void run() {
            if (destroyed) return;
            if (protectionEnabled && root != null) {
                int phase = (int) ((SystemClock.elapsedRealtime() / PROTECTION_INTERVAL_MS) % 5);
                float dp = getResources().getDisplayMetrics().density;
                float x = (phase == 1 ? 2 : phase == 2 ? -2 : phase == 3 ? 1 : phase == 4 ? -1 : 0) * dp;
                float y = (phase == 1 ? -1 : phase == 2 ? 1 : phase == 3 ? 2 : phase == 4 ? -2 : 0) * dp;
                root.setTranslationX(x);
                root.setTranslationY(y);
            }
            ui.postDelayed(this, PROTECTION_INTERVAL_MS);
        }
    };

    @Override protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        worker.setRemoveOnCancelPolicy(true);
        applyIntent(getIntent());
        panelConfig = loadWakeConfiguration();
        bindLayout();
        enterImmersiveMode();
        ui.postDelayed(protectionTask, PROTECTION_INTERVAL_MS);
        worker.execute(pollTask);
    }

    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        applyIntent(intent);
        lastConfigFetch = 0;
        worker.execute(() -> {
            try { api.fetchConfig(); } catch (Exception ignored) { }
        });
    }

    @Override public void onConfigurationChanged(Configuration newConfig) {
        super.onConfigurationChanged(newConfig);
        bindLayout();
        enterImmersiveMode();
    }

    @Override public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) enterImmersiveMode();
    }

    @Override protected void onDestroy() {
        destroyed = true;
        ui.removeCallbacksAndMessages(null);
        worker.shutdownNow();
        super.onDestroy();
    }

    private void applyIntent(Intent intent) {
        int port = intent.getIntExtra("api_port", 8765);
        String token = intent.getStringExtra("api_token");
        if (api == null) api = new ApiClient(port, token);
        else api.configure(port, token);
    }

    private void bindLayout() {
        setContentView(R.layout.activity_main);
        root = findViewById(R.id.root);
        monitorContent = findViewById(R.id.monitor_content);
        controlPanel = findViewById(R.id.control_panel);
        modeBar = findViewById(R.id.mode_bar);
        wakePanel = findViewById(R.id.wake_panel);
        sensorArea = findViewById(R.id.sensor_area);
        cpuCard = findViewById(R.id.cpu_card);
        gpuCard = findViewById(R.id.gpu_card);
        memoryCard = findViewById(R.id.memory_card);
        cpuDetails = findViewById(R.id.cpu_details);
        gpuDetails = findViewById(R.id.gpu_details);
        networkGroup = findViewById(R.id.network_group);
        diskGroup = findViewById(R.id.disk_group);
        fpsGroup = findViewById(R.id.fps_group);
        buttonGrid = findViewById(R.id.button_grid);
        connectionStatus = findViewById(R.id.connection_status);
        message = findViewById(R.id.message);
        cpuName = findViewById(R.id.cpu_name);
        cpuTemp = findViewById(R.id.cpu_temp);
        cpuUsage = findViewById(R.id.cpu_usage);
        cpuClock = findViewById(R.id.cpu_clock);
        cpuPower = findViewById(R.id.cpu_power);
        gpuName = findViewById(R.id.gpu_name);
        gpuTemp = findViewById(R.id.gpu_temp);
        gpuUsage = findViewById(R.id.gpu_usage);
        gpuHotspot = findViewById(R.id.gpu_hotspot);
        gpuClock = findViewById(R.id.gpu_clock);
        gpuVramClock = findViewById(R.id.gpu_vram_clock);
        gpuPower = findViewById(R.id.gpu_power);
        gpuFan = findViewById(R.id.gpu_fan);
        ramValue = findViewById(R.id.ram_value);
        ramUsage = findViewById(R.id.ram_usage);
        vramValue = findViewById(R.id.vram_value);
        downloadValue = findViewById(R.id.download_value);
        uploadValue = findViewById(R.id.upload_value);
        diskActivity = findViewById(R.id.disk_activity);
        diskUsage = findViewById(R.id.disk_usage);
        fpsValue = findViewById(R.id.fps_value);
        controlCpuValue = findViewById(R.id.control_cpu_value);
        controlGpuValue = findViewById(R.id.control_gpu_value);
        controlCpuDetails = findViewById(R.id.control_cpu_details);
        controlGpuDetails = findViewById(R.id.control_gpu_details);
        controlRamValue = findViewById(R.id.control_ram_value);
        controlVramValue = findViewById(R.id.control_vram_value);
        controlFpsValue = findViewById(R.id.control_fps_value);
        modeMonitor = findViewById(R.id.mode_monitor);
        modeControl = findViewById(R.id.mode_control);
        wakeButton = findViewById(R.id.wake_button);
        wakeComputer = findViewById(R.id.wake_computer);
        wakeStatus = findViewById(R.id.wake_status);
        modeMonitor.setOnClickListener(v -> { controlMode = false; applyMode(); });
        modeControl.setOnClickListener(v -> { controlMode = true; applyMode(); });
        wakeButton.setOnClickListener(this::sendWakeOnLan);
        findViewById(R.id.menu_button).setOnClickListener(this::showMenu);
        bindCommandButtons();
        applyPanelConfig(panelConfig);
        applyMode();
        if (lastStats != null && consecutiveFailures < 3) {
            showConnected();
            renderStats(lastStats);
        } else showDisconnected();
    }

    private void bindCommandButtons() {
        commandButtons.clear();
        addCommandButton("media_previous", R.id.cmd_media_previous);
        addCommandButton("media_play_pause", R.id.cmd_media_play_pause);
        addCommandButton("media_next", R.id.cmd_media_next);
        addCommandButton("mute", R.id.cmd_mute);
        addCommandButton("volume_down", R.id.cmd_volume_down);
        addCommandButton("volume_up", R.id.cmd_volume_up);
        addCommandButton("show_desktop", R.id.cmd_show_desktop);
        addCommandButton("open_task_manager", R.id.cmd_open_task_manager);
        addCommandButton("open_steam", R.id.cmd_open_steam);
        addCommandButton("open_amd", R.id.cmd_open_amd);
        addCommandButton("custom_1", R.id.cmd_custom_1);
        addCommandButton("custom_2", R.id.cmd_custom_2);
        addCommandButton("custom_3", R.id.cmd_custom_3);
        addCommandButton("custom_4", R.id.cmd_custom_4);
        buttonGrid.removeAllViews();
    }

    private void addCommandButton(String id, int viewId) {
        Button button = findViewById(viewId);
        button.setTag(id);
        button.setOnClickListener(this::sendCommand);
        commandButtons.put(id, button);
    }

    private void applyPanelConfig(PanelConfig config) {
        cpuCard.setVisibility(config.showCpu ? View.VISIBLE : View.GONE);
        gpuCard.setVisibility(config.showGpu ? View.VISIBLE : View.GONE);
        memoryCard.setVisibility(config.showRam || config.showVram || config.showNetwork || config.showDisk || config.showFps ? View.VISIBLE : View.GONE);
        ramValue.setVisibility(config.showRam ? View.VISIBLE : View.GONE);
        ramUsage.setVisibility(config.showRam ? View.VISIBLE : View.GONE);
        vramValue.setVisibility(config.showVram ? View.VISIBLE : View.GONE);
        networkGroup.setVisibility(config.showNetwork ? View.VISIBLE : View.GONE);
        diskGroup.setVisibility(config.showDisk ? View.VISIBLE : View.GONE);
        fpsGroup.setVisibility(config.showFps ? View.VISIBLE : View.GONE);
        controlFpsValue.setVisibility(config.showFps ? View.VISIBLE : View.GONE);
        applyButtons(config);
    }

    private void applyButtons(PanelConfig config) {
        if (buttonGrid == null) return;
        buttonGrid.removeAllViews();
        if (!controlMode) return;
        List<PanelConfig.PanelButton> buttons = config.buttons;
        if (buttons.isEmpty()) buttons = defaultButtons();
        int limit = 14;
        List<PanelConfig.PanelButton> visibleItems = new ArrayList<>(limit);
        for (PanelConfig.PanelButton item : buttons) {
            if (visibleItems.size() >= limit) break;
            Button button = commandButtons.get(item.id);
            if (button == null) continue;
            visibleItems.add(item);
        }

        boolean portrait = getResources().getConfiguration().orientation != Configuration.ORIENTATION_LANDSCAPE;
        int logicalColumns = 4;
        buttonGrid.setColumnCount(logicalColumns * 2);
        int buttonHeight = portrait ? Math.round(54 * getResources().getDisplayMetrics().density) : 0;
        int margin = Math.round(3 * getResources().getDisplayMetrics().density);

        int rowCount = (visibleItems.size() + logicalColumns - 1) / logicalColumns;
        buttonGrid.setRowCount(Math.max(1, rowCount));
        for (int row = 0; row < rowCount; row++) {
            int rowStart = row * logicalColumns;
            int itemsInRow = Math.min(logicalColumns, visibleItems.size() - rowStart);
            int sideHalfColumns = logicalColumns - itemsInRow;

            if (sideHalfColumns > 0) addGridSpacer(row, 0, sideHalfColumns, sideHalfColumns / 2f);
            for (int positionInRow = 0; positionInRow < itemsInRow; positionInRow++) {
                PanelConfig.PanelButton item = visibleItems.get(rowStart + positionInRow);
                Button button = commandButtons.get(item.id);
                if (button == null) continue;
                button.setText(item.icon.length() == 0 ? item.label : item.icon + " " + item.label);
                button.setEnabled(item.available);
                button.setAlpha(item.available ? 1f : 0.35f);
                GridLayout.LayoutParams params = new GridLayout.LayoutParams();
                params.width = 0;
                params.height = buttonHeight;
                params.rowSpec = portrait ? GridLayout.spec(row, 1) : GridLayout.spec(row, 1, 1f);
                params.columnSpec = GridLayout.spec(sideHalfColumns + positionInRow * 2, 2, 1f);
                params.setMargins(margin, margin, margin, margin);
                button.setLayoutParams(params);
                buttonGrid.addView(button);
            }
            if (sideHalfColumns > 0) {
                int endingColumn = sideHalfColumns + itemsInRow * 2;
                addGridSpacer(row, endingColumn, sideHalfColumns, sideHalfColumns / 2f);
            }
        }
    }

    private void addGridSpacer(int row, int column, int span, float weight) {
        Space spacer = new Space(this);
        GridLayout.LayoutParams params = new GridLayout.LayoutParams();
        params.width = 0;
        params.height = 1;
        params.rowSpec = GridLayout.spec(row, 1);
        params.columnSpec = GridLayout.spec(column, span, weight);
        spacer.setLayoutParams(params);
        buttonGrid.addView(spacer);
    }

    private List<PanelConfig.PanelButton> defaultButtons() {
        List<PanelConfig.PanelButton> list = new ArrayList<>(7);
        list.add(new PanelConfig.PanelButton("media_previous", getString(R.string.previous), true));
        list.add(new PanelConfig.PanelButton("media_play_pause", "PLAY/PAUSE", true));
        list.add(new PanelConfig.PanelButton("media_next", getString(R.string.next), true));
        list.add(new PanelConfig.PanelButton("mute", "MUTE", true));
        list.add(new PanelConfig.PanelButton("volume_down", "VOL -", true));
        list.add(new PanelConfig.PanelButton("volume_up", "VOL +", true));
        list.add(new PanelConfig.PanelButton("show_desktop", "DESKTOP", true));
        return list;
    }

    private void applyMode() {
        monitorContent.setVisibility(controlMode ? View.GONE : View.VISIBLE);
        controlPanel.setVisibility(controlMode ? View.VISIBLE : View.GONE);
        cpuDetails.setVisibility(View.VISIBLE);
        gpuDetails.setVisibility(View.VISIBLE);
        modeMonitor.setTextColor(controlMode ? COLOR_SECONDARY : Color.rgb(42, 199, 218));
        modeControl.setTextColor(controlMode ? Color.rgb(42, 199, 218) : COLOR_SECONDARY);
        modeMonitor.setBackgroundResource(controlMode ? R.drawable.mode_unselected : R.drawable.mode_selected);
        modeControl.setBackgroundResource(controlMode ? R.drawable.mode_selected : R.drawable.mode_unselected);
        applyButtons(panelConfig);
    }

    private void sendWakeOnLan(View view) {
        final PanelConfig config = panelConfig;
        if (!config.wakeOnLanAvailable) {
            setText(wakeStatus, getString(R.string.wake_unavailable));
            return;
        }
        if (!isWifiConnected()) {
            setText(wakeStatus, getString(R.string.wake_wifi_required));
            return;
        }

        wakeButton.setEnabled(false);
        wakeButton.setAlpha(0.65f);
        setText(wakeStatus, getString(R.string.wake_sending));
        final Context applicationContext = getApplicationContext();
        worker.execute(() -> {
            try {
                int sent = WakeOnLanSender.send(applicationContext, config.wakeMacAddress,
                        config.wakeBroadcastAddress, config.wakePort);
                Log.i(LOG_TAG, "Wake-on-LAN: " + sent + " magic packets sent over Wi-Fi to " +
                        config.wakeMacAddress + " / " + config.wakeBroadcastAddress);
                ui.post(() -> {
                    if (destroyed || wakeStatus == null) return;
                    setText(wakeStatus, getString(R.string.wake_sent));
                    wakeButton.setEnabled(true);
                    wakeButton.setAlpha(1f);
                });
            } catch (Exception exception) {
                Log.w(LOG_TAG, "Wake-on-LAN send failed", exception);
                ui.post(() -> {
                    if (destroyed || wakeStatus == null) return;
                    setText(wakeStatus, getString(R.string.wake_failed));
                    wakeButton.setEnabled(true);
                    wakeButton.setAlpha(1f);
                });
            }
        });
    }

    private void sendCommand(View view) {
        final Button button = (Button) view;
        final String command = (String) button.getTag();
        button.setAlpha(0.65f);
        ui.postDelayed(() -> button.setAlpha(button.isEnabled() ? 1f : 0.35f), 160);
        worker.execute(() -> {
            boolean ok = api.sendCommand(command);
            if (!ok) ui.post(() -> showTemporaryMessage(getString(R.string.command_failed)));
        });
    }

    private void renderStats(StatsSnapshot stats) {
        PanelConfig config = panelConfig;
        setText(cpuName, stats.cpu.name);
        setText(cpuTemp, format(stats.cpu.temperature, "%.0f°C"));
        setText(cpuUsage, format(stats.cpu.usage, "%.0f%%"));
        setText(cpuClock, Double.isNaN(stats.cpu.clock) ? "-- GHz" : String.format(Locale.US, "%.2f GHz", stats.cpu.clock / 1000.0));
        setText(cpuPower, format(stats.cpu.power, "%.0f W"));
        cpuTemp.setTextColor(temperatureColor(stats.cpu.temperature, config.cpuElevated, config.cpuCritical));

        setText(gpuName, stats.gpu.name);
        setText(gpuTemp, format(stats.gpu.temperature, "%.0f°C"));
        setText(gpuUsage, format(stats.gpu.usage, "%.0f%%"));
        setText(gpuHotspot, "HOTSPOT " + format(stats.gpu.hotspot, "%.0f°C"));
        setText(gpuClock, "CORE " + format(stats.gpu.clock, "%.0f MHz"));
        setText(gpuVramClock, "VRAM " + format(stats.gpu.vramClock, "%.0f MHz"));
        setText(gpuPower, format(stats.gpu.power, "%.0f W"));
        setText(gpuFan, "FAN " + format(stats.gpu.fanRPM, "%.0f RPM") + " / " + format(stats.gpu.fanPercent, "%.0f%%"));
        gpuTemp.setTextColor(temperatureColor(stats.gpu.temperature, config.gpuElevated, config.gpuCritical));
        gpuHotspot.setTextColor(temperatureColor(stats.gpu.hotspot, config.gpuElevated, config.gpuCritical));

        setText(ramValue, formatPair(stats.ram.used, stats.ram.total));
        setText(ramUsage, format(stats.ram.usage, "%.0f%%"));
        setText(vramValue, formatPair(stats.gpu.vramUsed, stats.gpu.vramTotal));
        setText(downloadValue, "↓ " + format(stats.network.download, "%.2f MB/s"));
        setText(uploadValue, "↑ " + format(stats.network.upload, "%.2f MB/s"));
        setText(diskActivity, getString(R.string.disk_format, format(stats.disk.activity, "%.0f%%")));
        setText(diskUsage, getString(R.string.usage_format, format(stats.disk.mainUsage, "%.0f%%")));
        setText(fpsValue, format(stats.fps, "%.0f"));
        setText(controlCpuValue, format(stats.cpu.temperature, "%.0f°C") + "  •  " + format(stats.cpu.usage, "%.0f%%"));
        setText(controlGpuValue, format(stats.gpu.temperature, "%.0f°C") + "  •  " + format(stats.gpu.usage, "%.0f%%"));
        setText(controlCpuDetails, (Double.isNaN(stats.cpu.clock) ? "-- GHz" : String.format(Locale.US, "%.2f GHz", stats.cpu.clock / 1000.0)) + "  •  " + format(stats.cpu.power, "%.0f W"));
        setText(controlGpuDetails, format(stats.gpu.clock, "%.0f MHz") + "  •  " + format(stats.gpu.power, "%.0f W"));
        setText(controlRamValue, "RAM  " + formatPair(stats.ram.used, stats.ram.total));
        setText(controlVramValue, "VRAM  " + formatPair(stats.gpu.vramUsed, stats.gpu.vramTotal));
        setText(controlFpsValue, "FPS  " + format(stats.fps, "%.0f"));
        controlCpuValue.setTextColor(temperatureColor(stats.cpu.temperature, config.cpuElevated, config.cpuCritical));
        controlGpuValue.setTextColor(temperatureColor(stats.gpu.temperature, config.gpuElevated, config.gpuCritical));
        fpsGroup.setVisibility(config.showFps ? View.VISIBLE : View.GONE);
    }

    private void showConnected() {
        boolean returningFromWakeScreen = wakePanel.getVisibility() == View.VISIBLE;
        wakePanel.setVisibility(View.GONE);
        modeBar.setVisibility(View.VISIBLE);
        if (returningFromWakeScreen) applyMode();
        setText(connectionStatus, "● USB");
        connectionStatus.setTextColor(COLOR_NORMAL);
        sensorArea.setAlpha(1f);
        controlPanel.setAlpha(1f);
        if (getString(R.string.connection_lost).contentEquals(message.getText())) setText(message, "");
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    }

    private void showDisconnected() {
        if (destroyed || connectionStatus == null) return;
        setText(connectionStatus, getString(R.string.disconnected));
        connectionStatus.setTextColor(COLOR_CRITICAL);
        setText(message, getString(R.string.connection_lost));
        sensorArea.setAlpha(0.42f);
        controlPanel.setAlpha(0.42f);
        clearValues();
        PanelConfig config = panelConfig;
        if (config.wakeOnLanEnabled) {
            boolean openingWakeScreen = wakePanel.getVisibility() != View.VISIBLE;
            modeBar.setVisibility(View.GONE);
            monitorContent.setVisibility(View.GONE);
            controlPanel.setVisibility(View.GONE);
            wakePanel.setVisibility(View.VISIBLE);
            setText(message, "");
            setText(wakeComputer, config.wakeComputerName.length() == 0
                    ? getString(R.string.wake_not_configured) : config.wakeComputerName);
            wakeButton.setEnabled(config.wakeOnLanAvailable);
            wakeButton.setAlpha(config.wakeOnLanAvailable ? 1f : 0.4f);
            if (openingWakeScreen) setText(wakeStatus, config.wakeOnLanAvailable
                    ? getString(R.string.wake_instructions) : getString(R.string.wake_unavailable));
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        } else {
            boolean returningFromWakeScreen = wakePanel.getVisibility() == View.VISIBLE;
            wakePanel.setVisibility(View.GONE);
            modeBar.setVisibility(View.VISIBLE);
            if (returningFromWakeScreen) applyMode();
        }
        if (!config.wakeOnLanEnabled && (lastSuccess == 0 || SystemClock.elapsedRealtime() - lastSuccess > 30000))
            getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    }

    @SuppressWarnings("deprecation")
    private boolean isWifiConnected() {
        ConnectivityManager manager = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
        NetworkInfo active = manager == null ? null : manager.getActiveNetworkInfo();
        return active != null && active.isConnected() && active.getType() == ConnectivityManager.TYPE_WIFI;
    }

    private void saveWakeConfiguration(PanelConfig config) {
        getSharedPreferences(WAKE_PREFS, MODE_PRIVATE).edit()
                .putBoolean("enabled", config.wakeOnLanEnabled)
                .putBoolean("available", config.wakeOnLanAvailable)
                .putString("computer", config.wakeComputerName)
                .putString("mac", config.wakeMacAddress)
                .putString("broadcast", config.wakeBroadcastAddress)
                .putInt("port", config.wakePort)
                .putString("reason", config.wakeReason)
                .apply();
    }

    private PanelConfig loadWakeConfiguration() {
        SharedPreferences preferences = getSharedPreferences(WAKE_PREFS, MODE_PRIVATE);
        PanelConfig config = new PanelConfig();
        config.wakeOnLanEnabled = preferences.getBoolean("enabled", true);
        config.wakeOnLanAvailable = preferences.getBoolean("available", false);
        config.wakeComputerName = preferences.getString("computer", "");
        config.wakeMacAddress = preferences.getString("mac", "");
        config.wakeBroadcastAddress = preferences.getString("broadcast", "");
        config.wakePort = preferences.getInt("port", 9);
        config.wakeReason = preferences.getString("reason", "");
        return config;
    }

    private void clearValues() {
        setText(cpuTemp, "--°C"); setText(cpuUsage, "--%"); setText(cpuClock, "-- GHz"); setText(cpuPower, "-- W");
        setText(gpuTemp, "--°C"); setText(gpuUsage, "--%"); setText(gpuHotspot, "HOTSPOT --°C");
        setText(gpuClock, "CORE -- MHz"); setText(gpuVramClock, "VRAM -- MHz"); setText(gpuPower, "-- W"); setText(gpuFan, "FAN -- RPM / --%");
        setText(ramValue, "-- / -- GB"); setText(ramUsage, "--%"); setText(vramValue, "-- / -- GB");
        setText(downloadValue, "↓ -- MB/s"); setText(uploadValue, "↑ -- MB/s"); setText(diskActivity, getString(R.string.disk_value)); setText(diskUsage, getString(R.string.usage_value)); setText(fpsValue, "--");
        setText(controlCpuValue, "--°C  •  --%"); setText(controlGpuValue, "--°C  •  --%");
        setText(controlCpuDetails, "-- GHz  •  -- W"); setText(controlGpuDetails, "-- MHz  •  -- W");
        setText(controlRamValue, "RAM  -- / -- GB"); setText(controlVramValue, "VRAM  -- / -- GB");
        setText(controlFpsValue, "FPS  --");
    }

    private void showTemporaryMessage(String text) {
        setText(message, text);
        ui.postDelayed(() -> { if (text.contentEquals(message.getText())) setText(message, ""); }, 2200);
    }

    private void showMenu(View anchor) {
        PopupMenu popup = new PopupMenu(this, anchor);
        popup.getMenu().add(0, 1, 0, getString(R.string.brightness_normal));
        popup.getMenu().add(0, 2, 1, getString(R.string.brightness_low));
        popup.getMenu().add(0, 3, 2, getString(R.string.brightness_minimum));
        popup.getMenu().add(0, 4, 3, protectionEnabled ? getString(R.string.disable_protection) : getString(R.string.enable_protection));
        popup.setOnMenuItemClickListener(item -> {
            if (item.getItemId() == 1) setActivityBrightness(1f);
            else if (item.getItemId() == 2) setActivityBrightness(0.28f);
            else if (item.getItemId() == 3) setActivityBrightness(0.06f);
            else if (item.getItemId() == 4) {
                protectionEnabled = !protectionEnabled;
                if (!protectionEnabled) { root.setTranslationX(0); root.setTranslationY(0); }
                showTemporaryMessage(getString(protectionEnabled ? R.string.protection_enabled : R.string.protection_disabled));
            }
            return true;
        });
        popup.show();
    }

    private void setActivityBrightness(float value) {
        WindowManager.LayoutParams params = getWindow().getAttributes();
        params.screenBrightness = value;
        getWindow().setAttributes(params);
    }

    @SuppressWarnings("deprecation")
    private boolean applyServerLanguage(String code) {
        if (!("pt".equals(code) || "en".equals(code)) || code.equals(appliedLanguage)) return false;
        Locale locale = new Locale(code);
        Configuration configuration = new Configuration(getResources().getConfiguration());
        configuration.setLocale(locale);
        getResources().updateConfiguration(configuration, getResources().getDisplayMetrics());
        appliedLanguage = code;
        return true;
    }

    private void enterImmersiveMode() {
        Window window = getWindow();
        window.setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN, WindowManager.LayoutParams.FLAG_FULLSCREEN);
        window.getDecorView().setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY |
                View.SYSTEM_UI_FLAG_FULLSCREEN |
                View.SYSTEM_UI_FLAG_HIDE_NAVIGATION |
                View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN |
                View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION |
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
    }

    private static String format(double value, String pattern) {
        return Double.isNaN(value) || Double.isInfinite(value) ? pattern.replaceAll("%\\.?[0-9]*[df]", "--") : String.format(Locale.US, pattern, value);
    }

    private static String formatPair(double used, double total) {
        if (Double.isNaN(used) || Double.isNaN(total)) return "-- / -- GB";
        return String.format(Locale.US, "%.1f / %.1f GB", used, total);
    }

    private static int temperatureColor(double value, double elevated, double critical) {
        if (Double.isNaN(value)) return COLOR_SECONDARY;
        if (value >= critical) return COLOR_CRITICAL;
        if (value >= elevated) return COLOR_ELEVATED;
        return COLOR_PRIMARY;
    }

    private static void setText(TextView view, String value) {
        if (!value.contentEquals(view.getText())) view.setText(value);
    }
}
