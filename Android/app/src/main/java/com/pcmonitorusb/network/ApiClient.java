package com.pcmonitorusb.network;

import com.pcmonitorusb.model.PanelConfig;
import com.pcmonitorusb.model.StatsSnapshot;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

public final class ApiClient {
    private static final int MAX_RESPONSE_CHARS = 131072;
    private volatile int port;
    private volatile String token;

    public ApiClient(int port, String token) {
        this.port = port;
        this.token = token == null ? "" : token;
    }

    public void configure(int newPort, String newToken) {
        port = newPort;
        token = newToken == null ? "" : newToken;
    }

    public StatsSnapshot fetchStats() throws IOException, JSONException {
        JSONObject root = new JSONObject(request("GET", "/api/stats", null));
        StatsSnapshot result = new StatsSnapshot();
        JSONObject cpu = root.optJSONObject("cpu");
        JSONObject gpu = root.optJSONObject("gpu");
        JSONObject ram = root.optJSONObject("ram");
        JSONObject network = root.optJSONObject("network");
        JSONObject disk = root.optJSONObject("disk");
        if (cpu != null) {
            result.cpu.name = cpu.optString("name", "CPU");
            result.cpu.temperature = number(cpu, "temperature");
            result.cpu.usage = number(cpu, "usage");
            result.cpu.clock = number(cpu, "clock");
            result.cpu.power = number(cpu, "power");
        }
        if (gpu != null) {
            result.gpu.name = gpu.optString("name", "GPU");
            result.gpu.temperature = number(gpu, "temperature");
            result.gpu.hotspot = number(gpu, "hotspot");
            result.gpu.usage = number(gpu, "usage");
            result.gpu.clock = number(gpu, "clock");
            result.gpu.vramClock = number(gpu, "vramClock");
            result.gpu.vramUsed = number(gpu, "vramUsed");
            result.gpu.vramTotal = number(gpu, "vramTotal");
            result.gpu.power = number(gpu, "power");
            result.gpu.fanRPM = number(gpu, "fanRPM");
            result.gpu.fanPercent = number(gpu, "fanPercent");
        }
        if (ram != null) {
            result.ram.used = number(ram, "used");
            result.ram.total = number(ram, "total");
            result.ram.usage = number(ram, "usage");
        }
        if (network != null) {
            result.network.download = number(network, "download");
            result.network.upload = number(network, "upload");
        }
        if (disk != null) {
            result.disk.activity = number(disk, "activity");
            result.disk.mainUsage = number(disk, "mainUsage");
        }
        result.fps = number(root, "fps");
        return result;
    }

    public PanelConfig fetchConfig() throws IOException, JSONException {
        JSONObject root = new JSONObject(request("GET", "/api/config", null));
        PanelConfig config = new PanelConfig();
        config.updateIntervalMs = root.optInt("updateIntervalMs", 1000);
        config.showCpu = root.optBoolean("showCpu", true);
        config.showGpu = root.optBoolean("showGpu", true);
        config.showRam = root.optBoolean("showRam", true);
        config.showVram = root.optBoolean("showVram", true);
        config.showNetwork = root.optBoolean("showNetwork", false);
        config.showDisk = root.optBoolean("showDisk", false);
        config.showFps = root.optBoolean("showFps", false);
        JSONObject temperatures = root.optJSONObject("temperatures");
        if (temperatures != null) {
            config.cpuElevated = temperatures.optDouble("cpuElevated", 75);
            config.cpuCritical = temperatures.optDouble("cpuCritical", 90);
            config.gpuElevated = temperatures.optDouble("gpuElevated", 75);
            config.gpuCritical = temperatures.optDouble("gpuCritical", 90);
        }
        JSONArray buttons = root.optJSONArray("buttons");
        if (buttons != null) {
            for (int i = 0; i < buttons.length(); i++) {
                JSONObject item = buttons.optJSONObject(i);
                if (item == null) continue;
                String id = item.optString("id", "");
                if (id.length() == 0) continue;
                config.buttons.add(new PanelConfig.PanelButton(id, item.optString("label", id), item.optString("icon", ""), item.optBoolean("available", true)));
            }
        }
        return config;
    }

    public boolean sendCommand(String command) {
        try {
            JSONObject body = new JSONObject();
            body.put("command", command);
            request("POST", "/api/command", body.toString());
            return true;
        } catch (Exception ignored) {
            return false;
        }
    }

    private String request(String method, String path, String body) throws IOException {
        HttpURLConnection connection = null;
        try {
            URL url = new URL("http://127.0.0.1:" + port + path);
            connection = (HttpURLConnection) url.openConnection();
            connection.setRequestMethod(method);
            connection.setConnectTimeout(900);
            connection.setReadTimeout(900);
            connection.setUseCaches(false);
            connection.setRequestProperty("Accept", "application/json");
            if (token.length() > 0) connection.setRequestProperty("X-PCMonitor-Token", token);
            if (body != null) {
                byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
                connection.setDoOutput(true);
                connection.setFixedLengthStreamingMode(bytes.length);
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                try (OutputStream output = connection.getOutputStream()) {
                    output.write(bytes);
                }
            }
            int code = connection.getResponseCode();
            InputStream stream = code >= 200 && code < 300 ? connection.getInputStream() : connection.getErrorStream();
            String response = readLimited(stream);
            if (code < 200 || code >= 300) throw new IOException("HTTP " + code);
            return response;
        } finally {
            if (connection != null) connection.disconnect();
        }
    }

    private static String readLimited(InputStream stream) throws IOException {
        if (stream == null) return "";
        StringBuilder builder = new StringBuilder(2048);
        char[] buffer = new char[2048];
        try (BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8), 4096)) {
            int read;
            while ((read = reader.read(buffer)) >= 0) {
                if (builder.length() + read > MAX_RESPONSE_CHARS) throw new IOException("Resposta grande demais");
                builder.append(buffer, 0, read);
            }
        }
        return builder.toString();
    }

    private static double number(JSONObject object, String key) {
        if (object.isNull(key)) return Double.NaN;
        return object.optDouble(key, Double.NaN);
    }
}
