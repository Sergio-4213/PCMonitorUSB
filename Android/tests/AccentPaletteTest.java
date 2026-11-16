import com.pcmonitorusb.theme.AccentPalette;

public final class AccentPaletteTest {
    public static void main(String[] args) {
        String[] invalid = {null, "", "purple", "#123", "#FFFFFFFF", "#GG0000", "#12;456"};
        for (String color : invalid) {
            if (!AccentPalette.normalize(color).equals(AccentPalette.DEFAULT))
                throw new AssertionError("Invalid color accepted: " + color);
        }
        if (!AccentPalette.normalize("#7b35c1").equals("#7B35C1")) throw new AssertionError("Case");
        int count = 0;
        for (int r = 0; r <= 255; r += 17)
            for (int g = 0; g <= 255; g += 17)
                for (int b = 0; b <= 255; b += 17) {
                    AccentPalette p = new AccentPalette(String.format("#%02X%02X%02X", r, g, b));
                    int[] surfaces = {p.background, p.surface, p.raised, p.selected, p.pressed};
                    for (int bg : surfaces) {
                        if (AccentPalette.contrast(p.accent, bg) < 4.5)
                            throw new AssertionError("Accent contrast: " + p.source);
                        if (AccentPalette.contrast(0xfff2f3f5, bg) < 4.5)
                            throw new AssertionError("Primary text contrast: " + p.source);
                    }
                    count++;
                }
        System.out.println("PASS: color validation and readable contrast for " + count + " palettes.");
    }
}
