# POLISH - Reloaded UI, matched to the original VAGEDCSuite

This pass turns the Reloaded main window from "rough" into the original's
dense, professional layout: a map tree on the left, a map viewer on the
right, a one-line info strip, a collapsed office-style ribbon and a status
bar that carries the block/gearbox summary.  Only these files were touched:

| file | change |
|------|--------|
| `Reloaded/Main.cs` | layout, ribbon, icons, status bar, inline map viewer |
| `Reloaded/locales/en.json` | 24 new keys (tabs, groups, map viewer, status) |
| `Reloaded/locales/de.json` | the same keys in German |
| `Reloaded/POLISH.md` | this document |

`build.sh`, `Reloaded.csproj`, `MapViewForm.cs`, `Theming.cs`,
`HexViewerControl.cs`, `I18n.cs`, `MapList.cs` and every repo-root file are
unchanged.  No commit, no push.

## Before / after

Before (`evidence/lookmatch-blocks-dark.png`, from the previous pass):

* a 3-row block table was the default Fill surface, with the checksum report
  underneath and a large blank area below both;
* the ribbon had one tab ("File"/"Datei") and one group, with a big empty
  band to the right;
* the group caption was clipped (`Ribbo` instead of `Ribbon`, or "Datei"
  with the descender cut off);
* the map tree was only reachable through `View > Views > Maps`;
* the whole window read as sparse and utilitarian.

After (`evidence/polish-light.png`, `evidence/polish-dark.png`):

* map tree fixed on the left (300 px, Fill height), map viewer Fill on the
  right with a centred empty state;
* ribbon collapsed to its tab strip by default (File / View / Help) with
  seven real groups behind it;
* no clipped caption in the tab strip, the expanded ribbon or its popup;
* block table + checksum report behind `View > Views > Blocks`;
* one-line status bar with `file | AUT MAN 4WD | Checksum Ok. | identity |
  N symbols`.

## A. Why the group labels were clipped

Krypton sizes a ribbon group from its contents.  A `KryptonRibbonGroupTriple`
holding a single button is only about 50 px wide, and the group caption is
drawn into that width with `TextRenderer` and **no ellipsis**.  A two-line
caption ("Ribbon" / "Layout") wrapped and the second line was cut off by the
group bottom, which produced the stray clipped glyph the user saw.

Fixes in `Main.cs`:

* **Single-line group captions.**  `ApplyTexts` sets `TextLine1` and clears
  `TextLine2` for every group (`SetGroupText`).
* **Explicit minimum group widths.**  `CreateGroup` sets
  `MinimumWidth = 110`; the View groups get larger values (130-270) so the
  band is distributed across the ribbon instead of clustering at the left.
  The window has a 1000 px minimum width so the expanded band always fits.
* **Ribbon text/background palette overrides.**  The Sparkle/Office black
  bases draw the tab captions with a near-white colour meant for a coloured
  header, and use a bright yellow checked-button highlight that made white
  text unreadable.  `ApplyRibbonColors` pins the tab and group text to the
  chrome text colour, the tab/group surfaces to the chrome header/surface,
  and checked buttons to the accent-blue selection tint.

Verified in the tab strip, the expanded ribbon and the minimized popup
(`evidence/polish-ribbon-popup-light.png`): every caption and sublabel is
fully visible.

## B. Tree on the left, viewer on the right (default view)

The centre surface is now a `SplitContainer` with `FixedPanel = Panel1`:

```
+----------------------+---------------------------------------------+
| Maps (606)           | Map viewer                                  |
| Kategorie: ... (393) |                                             |
|   Unterkategorie ... |             Karte auswählen                 |
|     N146 ... @0x4F28 |  Doppelklick auf eine Karte im Baum ...     |
|     ...              |                                             |
+----------------------+---------------------------------------------+
| status: file | AUT MAN 4WD | Prüfsumme OK. | identity | 606 Symb.|
+--------------------------------------------------------------------+
```

* Left panel: bold tree header with the live leaf count, 300 px wide,
  `Fill` height, resizable splitter, `Panel1MinSize = 190`.
* Right panel: bold header (`Kartenansicht`, or the selected map's
  `name · 0xADDR · XxY`), centred empty-state title + hint.
* Double-click a leaf opens the heat map **inline** in the right panel.
  `OpenMapInline` builds the existing `MapViewForm` (the OxyPlot decoding is
  unchanged) and reparents its `PlotView` into the viewer panel; the shell
  form is disposed.  If reparenting ever fails, it falls back to the proven
  floating `MapViewForm.ShowMap` window.  Enter on a selected leaf does the
  same.
* The new `View > Map` group exposes the same action as a button
  (`Anzeigen` / `Show heat map`) and `Schließen` (`Close viewer`).  Opening
  from the ribbon also highlights the leaf in the tree.
* The block table is no longer the default.  `View > Views > Blocks` shows
  the grid plus the checksum report, which now scrolls to its first line
  instead of the last.  `Maps` returns to the tree/viewer; the two surfaces
  are mutually exclusive, so neither leaves a blank band.
* The tree keeps its category > subcategory > leaf structure with
  `@0xADDR (XxY)`, sorts categories/subcategories alphabetically and leaves
  by address, and gives each leaf the full label + description as a tooltip.
  Category nodes are bold.  The tree starts with no selected row.

## C. Ribbon

The ribbon is **collapsed by default** (`MinimizedMode = true` after the
window handle exists) - the original is dense, and the tab strip reads much
cleaner than an expanded band.  It has three tabs and seven groups:

| tab | groups | buttons |
|-----|--------|---------|
| File | File | Open |
| File | Image | Reload, Close |
| View | Views | Maps, Blocks, Hex |
| View | Map | Show heat map, Close viewer |
| View | Theme | Light, Dark |
| View | Language | English, Deutsch |
| View | Ribbon | Minimize |
| Help | Help | About |

* The ribbon starts on the **View** tab (where the layout controls live), so
  expanding it shows the full band; the File tab only needs two groups.
* Every button gets a generated 32 px GDI+ icon (`CreateIcon`): folder,
  reload arc, X, 3x3 map grid, block bars, `0x`, 4x4 heat grid, sun, moon,
  chevrons, EN/DE and an info dot.  This replaces Krypton's blue
  placeholder puzzle piece that all buttons shared before.
* Expand/collapse stays available: the `Minimize` check button, the ribbon's
  own chevron and double-clicking a tab (`AllowMinimizedChange`) all work.
* **wine-mono workaround:** in this Krypton build the minimized *popup* is
  laid out about 24 px too short and clips the button sublabels and group
  captions.  `MinimizedModeChanged` adds 24 px bottom padding while the
  ribbon is minimized and clears it again on expand.  Verified both ways:
  no clipped captions in the popup, no extra gap in the expanded band.
* `RibbonAppButton.AppButtonVisible = false` removes the duplicate leading
  "File" tile, so the first tab is the File tab.

## D. Polish details

* One-line 26 px info strip (`file | size (0x...) | part number`), with a
  hairline bottom border.
* Status bar, as separate labels so each fragment is themed and the symbol
  count stays right-aligned: `ga.stage1 | AUT MAN 4WD | Prüfsumme OK. |
  038906018P ... | 606 Symbole`.  The block summary is derived from
  `Edc15V256Layout.DescribeGearbox` (AUT / MAN / 4WD) and has a localized
  tooltip with the full per-block gearbox names.
* The status strip, split panels, header strips, tree and placeholder all
  take their colours from the active palette (`ApplyChromePalette`): light =
  white surfaces with `#F0F0F0` headers, dark = `#1E1E1E` surfaces with
  slightly lighter headers.  The checked-button yellow was replaced by a
  blue selection tint in both themes.
* Placeholder text is truly vertically centred (title + hint as one block).
* About dialog on the new Help tab.
* i18n: 24 new keys in both locales; the language switch still re-runs
  `Inspect`, so the tree, viewer header, placeholder, info strip and status
  bar all change language at runtime.  Map names and parser data stay
  untranslated, as before.
* `Reload` re-inspects the current file; `Close` returns to the empty state
  and clears the tree, grid, report, hex viewer and inline map.

## Verification

Build (the running app on display :0 locks `Reloaded.exe`; it was left
untouched - the file was renamed once and the build wrote a fresh exe):

```
cd Reloaded && bash build.sh
```

Result: `Compilation succeeded - 55 warning(s)` / `built: Reloaded.exe
(1009152 B)`, exit code **0**.  The 55 warnings are the pre-existing
core-parser warnings; `Main.cs` has none.

Headless run on the dedicated display `:92` (no `:0`, no `wineserver -k`;
only the PIDs started for each shot were stopped):

```
Xvfb :92 -screen 0 1280x900x24 &
DISPLAY=:92 wine explorer /desktop=Reloaded92,1140x800 \
  Reloaded.exe --theme=light "Z:\tmp\ga-work\ga.stage1"
```

Screenshots in `Reloaded/evidence/` (gitignored):

| file | what it proves |
|------|----------------|
| `polish-light.png` | default light: tree left, viewer placeholder right, status bar |
| `polish-dark.png` | same in dark theme, readable tab strip and info strip |
| `polish-map-light.png` | double-click / Map > Show: inline OxyPlot heat map, header with name/address/size |
| `polish-blocks-light.png` | View > Views > Blocks: 3-row table + checksum report from line 1 |
| `polish-ribbon-popup-light.png` | minimized ribbon popup: group captions and sublabels fully visible |
| `polish-expanded-light.png` | expanded ribbon after Minimize: full band, no extra gap |
| `polish-english-light.png` | runtime language switch: File/View/Help, Maps (606), Map viewer, Category, Subcategory, status in English |

Checks performed with `xdotool` + screenshots:

1. Default = tree left / viewer right; no block table or checksum report in
   the centre; no large blank areas.  Pass (light and dark).
2. Group captions and button sublabels fully visible in the tab strip, the
   expanded band and the popup.  Pass.
3. Popup -> Minimize expands the ribbon; no clipped row, no padding gap.
   Pass.
4. Double-click a map leaf renders the heat map inside the right panel.
   Pass.
5. Map > Show opens the first leaf when nothing is selected; Map > Close
   returns to the placeholder.  Pass.
6. Blocks shows the table + checksum (report starts at "Checksum: 3/3
   fields OK").  Pass.
7. Light/Dark buttons switch the whole window, including the ribbon, at
   runtime.  Pass.
8. English/Deutsch switch re-renders the static and dynamic texts.  Pass.

## Caveats

* **Minimized popup padding.**  The 24 px bottom padding is a wine-mono
  workaround for this Krypton version.  It only affects the minimized popup;
  the collapsed ribbon itself stays 27 px tall.  On Windows it is harmless.
* **Long tree labels** are wider than the 300 px tree, so a horizontal
  scrollbar appears (the original tree does the same).  The full label,
  description and code block are in the node tooltip.
* **OS-drawn tree glyphs and scrollbar** do not follow the Krypton palette;
  they stay the platform colours in both themes.
* **Inline plot chrome** (title, axis titles, margins, white background)
  comes from `MapViewForm`/OxyPlot, which was not modified.  A map with a
  very long description can clip that description inside the plot; the
  viewer header above it always shows the full name/address/size.
* **Symbol count** is the number of map leaves shown (606 for this image),
  not the original's raw symbol count (729 in the reference screenshot);
  scalar labels are filtered out before the tree is built.
* **Title bar** still carries the full ECU identity from the previous pass;
  it is intentionally long because that is what the original caption shows.
