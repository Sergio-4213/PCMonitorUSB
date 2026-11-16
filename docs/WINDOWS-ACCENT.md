# Windows accent sync — 2.3.3

The phone follows the Windows user's accent color automatically through the existing authenticated `/api/config` response (`accentColor`, strictly `#RRGGBB`). Windows reads the color with DwmGetColorizationColor; it never modifies Windows personalization settings. Unsupported native calls fall back to the original cyan.

Android refreshes configuration approximately every 10–15 seconds while connected. A palette is rebuilt only when the color changes or the layout is rebound. Backgrounds, cards, headings, tabs, button borders and pressed feedback use derived shades. Primary text remains light; temperature warnings and USB connection status keep their semantic colors. Dark accents are lightened for text to maintain at least 4.5:1 contrast on the generated surfaces.

The last validated color is saved alongside the local offline panel preferences, so rotation and disconnected use retain the theme. Older servers without `accentColor` remain compatible. No permissions, dependencies, network listeners or remote commands were added.

## Verification

- Windows: 14/14 checks passed, including real native color retrieval, RGB byte ordering, authenticated API payload and existing API security checks.
- Android: optimized release build and lint passed; signature verified against the certificate already used by the installed app.
- Palette validation: malformed color inputs rejected; primary/accent text contrast checked over 4,096 RGB samples including black and white.
- Installed on the actual SM-J410G and Windows PC. Visually checked Monitor and Control in landscape and Control in portrait. Rotation retained the theme and USB data resumed normally.
- The live Windows accent changed during validation; successive phone captures reflected the changed palette without restarting the app. Purple was also returned by the native reader during the Windows tests.

Run the palette regression check with JDK 17:

```text
javac -d TestResults/theme Android/app/src/main/java/com/pcmonitorusb/theme/AccentPalette.java Android/tests/AccentPaletteTest.java
java -cp TestResults/theme AccentPaletteTest
```

Native API reference: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcolorizationcolor
