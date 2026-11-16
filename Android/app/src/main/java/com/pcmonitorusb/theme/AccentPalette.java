package com.pcmonitorusb.theme;

/** Small, Android-independent palette. Derived once per accent change. */
public final class AccentPalette {
    public static final String DEFAULT = "#2AC7DA";
    public final String source;
    public final int background, surface, raised, line, selected, pressed, accent;

    public AccentPalette(String value) {
        source = normalize(value);
        int raw = 0xff000000 | Integer.parseInt(source.substring(1), 16);
        background = mix(0xff090909, raw, 0.07);
        surface = mix(0xff151515, raw, 0.13);
        raised = mix(0xff202020, raw, 0.17);
        line = mix(0xff353535, raw, 0.32);
        selected = mix(0xff171717, raw, 0.28);
        pressed = mix(0xff202020, raw, 0.32);
        int readable = raw;
        // Lighten dark accents until small text is readable on all themed surfaces.
        for (int i = 0; i <= 100; i++) {
            readable = mix(raw, 0xffffffff, i / 100.0);
            if (contrast(readable, pressed) >= 4.5 && contrast(readable, selected) >= 4.5
                    && contrast(readable, surface) >= 4.5 && contrast(readable, raised) >= 4.5
                    && contrast(readable, background) >= 4.5) break;
        }
        accent = readable;
    }

    public static String normalize(String value) {
        if (value == null || value.length() != 7 || value.charAt(0) != '#') return DEFAULT;
        for (int i = 1; i < 7; i++) if (Character.digit(value.charAt(i), 16) < 0) return DEFAULT;
        return value.toUpperCase(java.util.Locale.ROOT);
    }

    private static int mix(int a, int b, double amount) {
        int result = 0xff000000;
        for (int shift = 0; shift <= 16; shift += 8) {
            int first = (a >> shift) & 255, second = (b >> shift) & 255;
            result |= ((int) Math.round(first + (second - first) * amount)) << shift;
        }
        return result;
    }

    public static double contrast(int a, int b) {
        double x = luminance(a), y = luminance(b);
        return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
    }

    private static double luminance(int color) {
        return channel((color >> 16) & 255) * 0.2126
                + channel((color >> 8) & 255) * 0.7152 + channel(color & 255) * 0.0722;
    }

    private static double channel(int value) {
        double c = value / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    }
}
