# Reloaded UI integration — i18n + map list

Status: done on branch `reloaded-ui`. Only `Reloaded/Main.cs`,
`Reloaded/locales/en.json`, `Reloaded/locales/de.json` and this note were
changed. No commit, no push.

## 1. i18n wiring

### Startup

`Program.Main` calls `I18n.Initialize()` after
`Application.SetCompatibleTextRenderingDefault(false)` and before the
`MainForm` is constructed, so the first `ApplyTexts()` already reads the
persisted language table.

### `ApplyTexts()`

All static text lives in one method in `MainForm`:

* window title (`window_title`),
* both ribbon tabs (`tab_file`, `tab_view`),
* every ribbon group `TextLine1`/`TextLine2`,
* every ribbon button `TextLine1`/`TextLine2`,
* the info strip's initial text (`info_open_binary`),
* the block grid column headers (`block`, `address_range`, `gearbox`,
  `code`),
* the language check-button states.

The ribbon's leading application tile (Krypton's own `RibbonAppButton`, the
blue "File" square before the tabs) is also localized through
`RibbonAppButton.AppButtonText = I18n.T("tab_file")`; it is not one of our
tabs and was previously stuck on "File" in German.

To let `ApplyTexts()` reach the ribbon controls, the former locals
`fileTab`, `viewTab`, `fileGroup`, `themeGroup`, `viewsGroup`, `ribbonGroup`
and `openButton` became private readonly fields (`_fileTab`, `_viewTab`,
`_fileGroup`, `_themeGroup`, `_viewsGroup`, `_ribbonGroup`, `_openButton`).
The existing `_lightButton`, `_darkButton`, `_hexButton` and
`_minimizeButton` were already fields.

`ApplyTexts()` is called

1. at the end of the constructor (grids do not exist yet; header assignment
   is null-guarded),
2. in `OnShown` after both grids are created, and
3. on every language change.

### Language group

`View > Language` (`language` / `language_sub`) holds two
`GroupButtonType.Check` buttons whose labels stay literal: **English** and
**Deutsch**. A click calls

```csharp
I18n.SetLanguage("en" | "de");  // applies + persists to HKCU
ApplyTexts();
if (_lastPath != null) Inspect(_lastPath);
```

`ChangeLanguage()` also restores the check states via `ApplyTexts()` when
`SetLanguage()` fails. `_lastPath` is set at the top of `Inspect()`, so the
info strip, block rows and report are re-rendered in the new language for
the already loaded file (including the read-error path).

### Dynamic strings in `Inspect`

* read error: `I18n.T("info_read_error", ex.Message)`
* info line: `I18n.T("info_file_format", name, length, part)` — three
  arguments for `{0}`, `{1}`, `{1:X}`, `{2}`
* gearbox: `gearbox_automatic`, `gearbox_manual`, `gearbox_manual_fwd`
  (block 2), `gearbox_4x4` (block 3)
* block names: `block_1` … `block_3`
* non-256-KB suffix/report: `info_not_256kb`, `report_not_256kb`
* checksum header: `I18n.T("checksum_report", ok, total)`, then the
  `"  (" + err + ")"` suffix is appended separately (untranslated technical
  detail)
* open dialog: `I18n.T("dialog_filter")`

## 2. Map list view

* `View > Views` now has a second check button, **Maps** (`maps` /
  `maps_sub`), next to **Hex**.
* `_mapGrid` is a second `KryptonDataGridView`, created in `OnShown` with the
  same construction pattern as `_grid` (Krypton calls `CreateGraphics()` in
  its constructor; wine-mono fails with "Error creating window handle" before
  the message loop). It is `Dock = Fill`, hidden by default, columns:
  `Varname`, `Description`, `Address` (`0x` + hex), `Length`, `XLen`, `YLen`,
  `Units` (from `XUnits`), `Category`.
* `ShowMapsGrid(bool)` keeps the two Fill surfaces mutually exclusive:
  Maps on → `_mapGrid.Visible = true`, `_grid.Visible = false`; Maps off →
  the block grid returns.
* `Inspect()` clears both grids. For a known 256 KB image it calls
  `FillMapGrid(path)`:
  * `MapList.GetMaps(path)`,
  * keep only `XLen > 1 || YLen > 1` (axis length 1 means a scalar label,
    not a Kennfeld),
  * sort by `Address`,
  * one row per map.
* The `MapList` call is wrapped in `try/catch`: on failure the map grid stays
  empty and the report gets a short `Maps: <message>` note; the block grid,
  checksum report and hex view remain usable.

New locale keys (both files):

| key            | en        | de         |
|----------------|-----------|------------|
| `language`     | Language  | Sprache    |
| `language_sub` | Options   | Optionen   |
| `maps`         | Maps      | Karten     |
| `maps_sub`     | list      | Liste      |

## 3. Verification

### Build

```
cd Reloaded && bash build.sh
```

Result: `Compilation succeeded - 55 warning(s)` / `built: Reloaded.exe
(984576 B)`, exit code 0. (The warnings are the pre-existing core-parser
warnings.)

### Headless run

Dedicated display, own Wine processes only (the prefix is shared with a
foreign session; `wineserver -k` was not used):

```
Xvfb :97 -screen 0 1200x800x24
DISPLAY=:97 WINEDEBUG=-all wine explorer /desktop=Reloaded,1000x700 \
  Reloaded.exe --theme=dark "Z:\tmp\ga-work\ga.stage1"
DISPLAY=:97 import -window root shot.png
```

The window is 960x680 at (120,60) inside the 1000x700 desktop; screenshots
were cropped to the window and stored in `Reloaded/evidence/` (gitignored):

| file | what it shows |
|------|---------------|
| `integration-blocks-en.png` | English UI, View tab: 3 block rows (`Block 1..3`, `Automatic` / `Manual, FWD` / `4x4`), `Checksum: 3/3 fields OK` + per-block checksum lines |
| `integration-maps-en.png` | English UI, **Maps** checked: filtered, address-sorted Kennfeld list (Varname / Address `0x11B0…` / Length / XLen / YLen / Units / Category) |
| `integration-maps-de.png` | German UI, Maps still checked: `Teilenummer`, `Prüfsumme: 3/3 Felder OK`; map data itself is parser text and stays as-is |
| `integration-blocks-de.png` | German UI, Maps off: `Block` / `Adressbereich` / `Getriebe` / `Code`, `Automatik`, `Schaltgetriebe, Frontantrieb`, `Allrad` |

Checks performed by clicking the ribbon with `xdotool` (window-relative
coordinates from the screenshots):

1. launch with `--theme=dark` and the 256 KB file → block grid + green
   `Checksum: 3/3 fields OK` report (pass);
2. View tab → Maps → `_mapGrid` replaces the block grid, rows sorted
   ascending by address, only maps with XLen/YLen > 1 (pass);
3. View tab → Deutsch → ribbon (`Datei`, `Ansicht`, `Design`, `Ansichten`,
   `Menüband`, `Sprache`), info strip (`Teilenummer`), block headers
   (`Adressbereich`, `Getriebe`) and gearbox values change; Maps on/off
   still works (pass);
4. Deutsch → English → all texts return to English and the registry value
   is back to `en` (pass).

Language is persisted in `HKCU\Software\VAGEDCSuiteReloaded\Language`
(I18n's `SetLanguage`), so the last selected language survives a restart.
The final test run was left on English.

## 4. Wine-specific notes

* **Grid construction must stay in `OnShown`.** `KryptonDataGridView` calls
  `CreateGraphics()` in its constructor; wine-mono throws "Error creating
  window handle" if that happens before the message loop. `_mapGrid` follows
  the exact same pattern as `_grid`.
* **Pre-existing ribbon label clipping.** The small group captions are
  clipped by Krypton's group width (`Ribbo` for "Ribbon", `Menüba` for
  "Menüband"). This is not caused by this change: the same clipping is
  visible in the earlier evidence image `evidence/hexview-light.png`
  (`Ribbo`). Forcing `PerformLayout()`/`Invalidate()` did not change it, so
  no workaround was left in the code.
* **Wine log noise only.** The run log contains only
  `MESA-EGL: ... DRI3 error ...` warnings (software rendering under Xvfb);
  no application exception, and the UI renders correctly.
* **Shared prefix.** A foreign Wine session shares the prefix, so the test
  script never ran `wineserver -k`; it killed only the PIDs it had started
  and its own `Xvfb :97`.
* The app button tile is now localized too; without
  `RibbonAppButton.AppButtonText` it stayed "File" in the German UI.

## 5. Known limits

* Map column headers (`Varname`, `Description`, …) are fixed literals as
  specified; there are no locale keys for them.
* `Description` is empty and `Category` is parser text
  (`Detected maps` / `Unknown maps`), so those cells are not translated.
* The Maps check button is a plain toggle: it does not auto-check when the
  map grid is populated, and Hex + Maps can both be checked; the two Fill
  grids never show at once because Maps controls their visibility.
