# Theming — dark mode & modern look for Reloaded

This document describes how the Krypton-based Reloaded UI is themed, and the
Wine-specific caveats that had to be worked around.  The two core files
(`Edc15V256Layout.cs`, `EDC15VM256Checksum.cs`) are untouched; everything here
lives in `Main.cs` and `Theming.cs`.

## How the theme is wired

The app has **two `KryptonPalette` instances** (built in `ThemePalettes`):

| Theme | Base palette | Renderer | Rounded feel |
|---|---|---|---|
| Light | `PaletteMode.SparkleBlue` | Office 2013 ("Sparkle") | Sparkle is Krypton's modern, rounded Office-2013 look |
| Dark  | `PaletteMode.Office2010Black` | Office 2010 | explicit `Border.Rounding` on buttons + input controls |

Both palettes then **tune a handful of colours** so the form, panels, labels,
input controls and the DataGridView read from one coherent scheme (see
`ThemePalettes` — `DarkBg`, `DarkSurface`, `DarkHeader`, `DarkText`,
`DarkSelection`, …).  The grid is themed through the palette's `GridStyles`
entries (`GridCommon`/`GridList`/`GridSheet`: background, header, cell text and
selection), the textbox through `InputControlStyles`, the label through
`LabelStyles`, and the ribbon through the `Ribbon` section.

**Applying the palette** (`MainForm.ApplyPalette`):

1. `KryptonManager.GlobalPaletteMode = PaletteModeManager.Custom;`
2. `KryptonManager.GlobalPalette = palette;` — makes the palette the global
   one (the grid follows this).
3. The **form and ribbon are pinned explicitly** (`Palette` + `_ribbon.Palette`)
   because Krypton 5.550 does not reliably re-resolve a custom `GlobalPalette`
   for controls that were already created (see caveats below).
4. The form's client background is pinned directly
   (`StateCommon.Back.Color1` + `BackColor`) for the same reason.

Switching themes is then a single `ApplyTheme(ThemeKind)` call that re-runs the
four steps above and updates the ribbon buttons' checked state.

## Theme toggle & persistence

- A **"View" ribbon tab** contains a **"Theme"** group with two check buttons,
  **Light** and **Dark** (`KryptonRibbonGroupButton`, `GroupButtonType.Check`).
- The choice is persisted under **`HKCU\Software\VAGEDCSuiteReloaded`**, value
  `Theme` = `"Light"` / `"Dark"` (see `ThemeStore`).  A registry failure falls
  back silently to the light theme.
- A startup override exists for scripting/screenshots: `--theme=light` or
  `--theme=dark` (used by the evidence captures).  The positional argument
  remains the 256 KB image path, exactly as in the skeleton.

Verified: writing `Theme=Dark` to that key and starting the app **without** the
`--theme` flag loads the dark theme (form background `#1E1E1E`, ribbon
`#2B2B2B`).

## Rounded / modern look

- Control-level corner rounding only, via the palette: `RoundControls` sets
  `Border.Rounding = 5` on the button styles and input-control styles.
- Window corners are deliberately left **square** — rounding the window chrome
  needs DWM / Windows 11 and would break Windows XP SP3.

## Wine-specific caveats (all verified against the running app)

1. **`KryptonDataGridView` must be created after the message loop starts.**
   Its constructor calls `CreateGraphics()`, which wine-mono answers with
   `Win32Exception: Error creating window handle` when no handle exists yet.
   The grid is therefore constructed lazily in `OnShown` (and re-parented to
   dock correctly), instead of in the form constructor.
2. **A custom `GlobalPalette` does not reliably reach the form and ribbon.**
   The grid follows `KryptonManager.GlobalPalette`, but the form and ribbon
   keep their creation-time palette under wine-mono.  They are pinned
   explicitly (see above).
3. **The form's client background does not re-resolve from the palette.**
   `StateCommon.Back` (a redirect to `BackStyle = FormMain`) ignores the
   palette change under wine-mono, so the background colour is pinned directly
   and mirrored into `BackColor`.
4. **Krypton 5.550's "Black" modes are not actually dark.**  `Office2010Black`,
   `Office2010BlackDarkMode`, `Office365Black` and `Office365BlackDarkMode` all
   resolve the ribbon group area to a mid grey (`94,94,94`) and the form to
   `113,113,113` (verified by reading the palette from the assembly).  The
   ribbon is therefore re-coloured by hand in `ApplyDarkRibbon`.
5. **`PaletteMode.VisualStudioDark` crashes** (`NullReferenceException` inside
   `KryptonPalette.SetPalette`) in this build, so it was not used as the dark
   base.
6. **The window title bar and the Open dialog are not themed** — they are the
   wine/system chrome, left as-is (custom Krypton form chrome is off by
   default, which also keeps XP compatibility).

## Evidence

- `evidence/theme-light.png` — light theme (Sparkle ribbon, white grid, report).
- `evidence/theme-dark.png` — dark theme (dark ribbon, dark grid + report).

Both were captured from the running app under Wine's virtual desktop
(`/desktop=Reloaded,1000x700`) with `ga.stage1` loaded.
