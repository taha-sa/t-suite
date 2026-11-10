# T-Suite design system

`design/tokens.json` is the source of truth. Based on the supplied
`tasarim-sistemi-onerisi.md`; the five dark base accents are the agreed brand colors.
Edit tokens, then run `powershell -NoProfile -ExecutionPolicy Bypass -File design/generate.ps1`
from the repository root. The generator requires only Windows PowerShell 5.1/.NET;
generated files belong in source control. Do not edit generated files.

## Platform consumption

| Platform | Exact files to copy/import | Usage |
| --- | --- | --- |
| WinForms | `design/out/csharp/TSuiteTokens.cs` | Compile into the app; `using TSuite.Design;` then `TSuiteTokens.ColorNeutralDarkBg`, `TSuiteTokens.ColorAccentTBridgeDarkBase`, `TSuiteTokens.RadiusMd`, `TSuiteTokens.MotionBase`. Colors and TimeSpans are `static readonly` because C# cannot make them `const`; numeric values are `const int`. Geometry is logical 96-DPI pixels; scale once for device DPI. |
| WPF | `design/out/wpf/Tokens.Dark.xaml`, `Tokens.Light.xaml` | Copy as WPF Page resources; merge exactly one dictionary into `Application.Resources.MergedDictionaries`. Switch dictionaries for theme changes and bind brushes with `DynamicResource`. All colors have matching `Color.*` and `Brush.*` keys. |
| T-Bridge / Svelte | `design/out/css/tokens.css` | Import once in the root layout, after old palette declarations. Dark is the default; set `data-theme="light"` on the root element for light mode. Drop-in aliases: `--bg`, `--s1`, `--s2`, `--s3`, `--text`, `--muted`, `--faint`, `--border`, `--border-hi`, `--gold`, `--gold-hi`, `--gold-lo`, `--gold-grad`, `--on-accent`. `--gold-grad` is a CSS image; use it with `background`, not `background-color`. All original tokens also have qualified variables such as `--color-accent-tBridge-dark-base`. |
| Android Compose | `design/out/kotlin/TSuiteTokens.kt` | Import `tsuite.design.TSuiteTokens`; colors are Compose `Color`, spacing/radius are `Dp`, type sizes/line heights are `TextUnit` (`sp`), weights are integers, motion values are milliseconds. Use the platform font family (Segoe/Roboto CSS font stacks are metadata). |
| React Native | `design/out/ts/tokens.ts` | `import { tokens } from './tokens'`; select `tokens.color.neutral[theme]` and `tokens.color.accent.tBridge[theme]`. Numeric spacing/radius use RN logical units; map `fontWeight` to a string and parse motion milliseconds for Animated. CSS easing/shadows need platform adapters (Bezier/elevation/shadow props). |

The SDK repository has no T-Bridge Svelte or T-DeskTree application source to patch.
The aliases above form the integration contract. Tokens do not automatically theme existing controls.

### T-DeskTree WPF mapping

Both dictionaries default their short accent aliases to T-DeskTree. Other apps can
use qualified keys, for example `Brush.color.accent.tBridge.dark.base`, or override
short aliases after merging. Use `{DynamicResource Brush.Accent}` in controls.
For controls using unprefixed surface resources, `Bg.Base` and `Bg.Surface1/2/3`
are also SolidColorBrush aliases of their `Brush.Bg.*` counterparts.

| Token in selected theme | Brush key | Color key |
| --- | --- | --- |
| `neutral.bg` | `Brush.Bg.Base` | `Color.Bg.Base` |
| `neutral.surface1/2/3` | `Brush.Bg.Surface1/2/3` | `Color.Bg.Surface1/2/3` |
| `neutral.border / borderStrong` | `Brush.Border / Brush.BorderStrong` | `Color.Border / Color.BorderStrong` |
| `neutral.text / textMuted / textFaint` | `Brush.Text / Brush.TextMuted / Brush.TextFaint` | `Color.Text / Color.TextMuted / Color.TextFaint` |
| `neutral.focus` | `Brush.Focus` | `Color.Focus` |
| `accent.tDeskTree.base` | `Brush.Accent` | `Color.Accent` |
| `accent.tDeskTree.hover / pressed` | `Brush.Accent.Hover / Brush.Accent.Pressed` | `Color.Accent.Hover / Color.Accent.Pressed` |
| `accent.tDeskTree.onAccent / subtle` | `Brush.Accent.OnAccent / Brush.Accent.Subtle` | `Color.Accent.OnAccent / Color.Accent.Subtle` |

RGBA values become ARGB in C#/WPF/Kotlin; 10% alpha rounds to 26/255. Keep subtle
colors translucent and composite them over the chosen surface. Never use subtle
fills as a substitute for a visible focus ring or a control boundary.

## Accent contrast

WCAG sRGB relative luminance, `(lighter + .05)/(darker + .05)`. Ratios below use
opaque base fills and neutral `bg` (`#0D0F12` dark, `#F8FAFC` light). Text columns
show base and the minimum across base/hover/pressed. UI columns show base against
the theme background. Run `powershell -NoProfile -File design/audit-contrast.ps1`
to check all 30 combinations (text >= 4.5; dark UI >= 3).

| App | Dark base | Dark onAccent | Dark text base / min | Dark UI | Light base | Light text base / min | Light UI |
| --- | --- | --- | --- | --- | --- | --- | --- |
| T-Bridge (flagship gold) | `#E3A92B` | `#121417` | 8.765 / 6.634 | 9.118 | `#9E6B05` | 4.606 / 4.606 | 4.402 |
| T-Downloader (sky) | `#38BDF8` | `#06132B` | 8.631 / 4.515 | 8.959 | `#027BC7` | 4.506 / 4.506 | 4.307 |
| T-RamCash (violet) | `#C084FC` | `#1B082B` | 7.120 / 4.755 | 7.263 | `#7E22CE` | 6.983 / 6.983 | 6.674 |
| T-DeskTree (teal) | `#2DD4BF` | `#041D1A` | 9.434 / 7.055 | 10.310 | `#0F766E` | 5.473 / 5.473 | 5.231 |
| T-Desk Viewer (rose) | `#FB7185` | `#1E0409` | 7.248 / 5.313 | 7.131 | `#BE123C` | 6.285 / 6.285 | 6.007 |

All light `onAccent` values are white. Minimal channel adjustments: Downloader dark
`onAccent` `#06152B` -> `#06132B` changes green by -2, raising its original pressed
contrast 4.459 -> 4.515. Downloader light base `#0284C7` -> `#027BC7` changes green
by -9, raising white text contrast 4.095 -> 4.506; its 10% subtle tint follows.
Dark pressed remains `#0284C7`; all other proposed accent states are retained.
These thresholds do not certify arbitrary text on transparent tints or every
surface. Use neutral text on subtle backgrounds and verify actual compositions.

## Icons

Canonical source SVGs: `design/icons/t-bridge.svg`, `t-downloader.svg`,
`t-ramcash.svg`, `t-desktree.svg`, `t-desk-viewer.svg`, `t-suite.svg`.
Every tile is 1024 square, radius 225.28 (22%), diagonal accent gradient, white
glyph and low-opacity white T notch. The family mark uses flagship gold to meet
the shared template. Bridge geometry stays within the glyph box. DeskTree cell
outlines have nonoverlapping gutters; small sizes use shared dividers. Fine details
are tagged for removal at small sizes; the notch is omitted at 16/20/24px and
main strokes become 2px. Android omits the notch and fits foreground geometry
inside the central 72dp diameter safe circle on a 108dp canvas.

Rebuild with existing Python 3 + Pillow and Edge, no installs:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File design/icons/build-icons.ps1 -Python C:\path\to\python.exe
# Optional: -Edge C:\path\to\msedge.exe
# Optional: -PillowPath C:\existing\site-packages
```

The optional Pillow path loads an already installed compatible Pillow without
changing the Python environment. Python always runs with `-I`. Edge discovery
also supports EdgeCore. SVGs are embedded in tiny transparent HTML wrappers;
Edge uses headless=new, disable-gpu and transparent background. On the installed
Windows EdgeCore build, CLI `--screenshot` with `--window-size=N,N` clips content
because window chrome reduces the viewport. `edge-render.ps1` therefore uses
Edge's built-in local debugging protocol to set exact N×N viewports and capture
native PNGs. It waits for document load and two animation frames; no image is
resized or cropped. This uses only PowerShell/.NET and one isolated Edge profile,
with no browser automation packages. Temporary wrappers/manifests are ignored by
Git. Pillow validates nonempty alpha, tile edges, dimensions and adaptive safe
zones, then packs all eight exact PNG frames into each ICO.

For **each** `design/icons/out/<app>/`:

| Target | Files to copy |
| --- | --- |
| Windows executable / WinForms / WPF | `app.ico` (16,20,24,32,48,64,128,256); set `<ApplicationIcon>Assets/app.ico</ApplicationIcon>` in the consuming project. |
| Desktop UI / web / RN images | `16.png`, `20.png`, `24.png`, `32.png`, `48.png`, `64.png`, `128.png`, `256.png`, `512.png`, `1024.png`; preserve transparency. |
| Android adaptive launcher | Copy `android/mipmap-*` into `app/src/main/res/`; includes `mipmap-anydpi-v26/ic_launcher.xml`. Set manifest `android:icon="@mipmap/ic_launcher"`. Foreground/background sizes: mdpi 108, hdpi 162, xhdpi 216, xxhdpi 324, xxxhdpi 432. For API <26 also supply a legacy `ic_launcher.png` in each density from the desktop export at the launcher's 48dp size. |
| Play listing | `play-512.png` (opaque square; Play applies corner treatment). |

## Shared components

Sizes are logical px on Windows/web and dp on Android; type sizes map to sp on
Android. Use the 4/8/12/16/24/32 spacing scale; radii 6/8/12/16; Segoe UI Variable
then Segoe UI on Windows, Roboto on Android. Type: caption 11/14 regular, body
13/18 regular, bodyStrong 13/18 semibold, title 16/22 semibold, headline 20/26
semibold, display 28/34 bold. Motion: 120/150/250ms, cubic-bezier(.2,0,0,1).
Disable scale/slide animation for reduced motion. Keyboard focus uses a 2px
focus ring with 2px offset. Disabled controls use surface3/textFaint and cannot
activate. Preserve accessible names, keyboard order and 48dp Android touch
targets, even where the visible control is smaller.
`shadow` preserves the theme-specific CSS shadow recipes; `elevation` provides
2/4/12/20dp small/medium/large/dialog levels for native elevation APIs.

### Title bar

Windows: 36px compact or 44px standard; reserve rightmost 138px for three 46px
caption slots. Use native caption hit-testing or web drag region; exclude
interactive controls from dragging. App icon 16px, title bodyStrong, surface1
background and 1px border separator. Inactive title uses textMuted (60% chrome
opacity only where readable). Min/max hover surface2, pressed surface3. Close
uses danger base/onState and a visibly darker pressed state. Android top app
bar: 56dp, 16dp horizontal padding, 24dp glyph and >=48dp action target.

### Buttons

Small: 28px height, 8px horizontal padding, caption semibold, 14px icon, radius6.
Medium: 36px/16px/bodyStrong/16px/radius8. Large: 44px/24px/title/20px/radius12.
Primary uses app base/onAccent, hover and pressed tokens; pressed scale .985.
Ghost uses transparent fill and neutral text, surface2 hover and surface3 press.
Danger uses state danger base/**onState**, rather than the proposal's hard-coded
white (white on dark danger `#EF4444` is only 3.763:1). Keep onState if darkening
the danger fill; verify its contrast or choose an independently verified pair.
Focus remains visible; disabled uses surface3/textFaint and no action.

### Card

Surface1, 1px border, radius12, 16px padding (12 dense/24 spacious), small shadow.
Clickable cards hover surface2/borderStrong/medium shadow, press scale .992,
transition background/border over 150ms. Selected uses a 2px accent border and
10% accent subtle fill; use neutral text. Keep border sizing stable to avoid jumps.

### Segmented control

32px Windows / 40dp Android visual height, surface2, 1px border, radius8, 2px
inset. Thumb: surface1, radius6, shadow 0 1px 3px rgba(0,0,0,.25); slide/resize
150ms with shared easing. Body text, muted inactive, semibold text active;
hover text and focus ring; disabled textFaint. Implement tab/arrow-key selection
and expose selected state; Android touch hit regions remain 48dp.

### Toast

Windows bottom-right 24px from edges; Android bottom-center 16dp above gestures.
Width 280–360px, shrink to viewport minus margins; radius8, borderStrong,
surface2 at 92% opacity with 16px blur and large shadow. Leading state icon20;
bodyStrong title and caption/muted detail. Ghost action28 and trailing × close
glyph16 with accessible name. A 2px state-colored progress bar spans 4000ms;
pause on hover and keyboard focus. Announce status without moving keyboard focus.

### Update banner

Below title bar, full width, >=44px, padding8/16, accent subtle fill and 30%
accent bottom border. Leading badge16 and body release message; small Primary
“Restart & Update”, Ghost “View Notes”, trailing 24px visual × close control.
Below 480px, move actions to a second row. Keep update actions keyboard reachable.

### Invite card/dialog

Keep the **feature button always visible** in navigation/sidebar footer: height36,
surface2/border/radius8, family icon16, “T-Suite Family” and subtle “5 Free” badge;
hover surface3/borderStrong. Dismissing the invite never hides this entry point.
Dialog: centered 460px max width, viewport-safe margins, scrollable body,
radius16, surface1, borderStrong, dialog shadow; scrim black65% and blur4px.
Header: title16 semibold, caption/muted subtitle. Dedicated top-right **×**
Ghost28 closes on click; Escape also closes. Trap focus and return it to the
feature button. Missing-app rows have icon36, bodyStrong title, caption feature
description and Small “Install” / “Get for Android”. Footer separator, verified
license reassurance and “Install All” action. Show a useful empty state if all
companions are installed; the feature button remains visible.

### About window

Centered parent-owned modal, nominal 440×520, radius16; adapt/scroll at large
text scales and small viewports. Header: centered icon64, headline app name,
caption version/build/architecture and success subtle status pill. Roster lists
all five apps with detected versions and Installed / Current App / Not Installed
/ Available on Google Play status, with download actions where appropriate.
Legal section: actual GPL-3.0/license details and accurate privacy statement;
links to source, third-party notices, privacy. Footer: “Copy System Diagnostics”
(OS/build/architecture), “Report an Issue”, Primary36 “Close”. Provide ×/Escape
dismissal and restore focus. Versions and privacy claims in the proposal are
examples; display live facts and the actual app policy.
