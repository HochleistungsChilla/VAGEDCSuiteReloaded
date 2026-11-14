# LOOKMATCH - Reloaded UI matched to the original VAGEDCSuite

This change makes the DevExpress-free Reloaded UI look like the original
VAGEDCSuite main window: a full ECU identity in the title bar, a
Category -> Subcategory -> map tree on the left, and a one-line status bar at
the very bottom.

Only `Reloaded/Main.cs` and the two locale files were modified:

| file | change |
|------|--------|
| `Reloaded/Main.cs` | map tree, ECU title, status bar, palette hooks |
| `Reloaded/locales/en.json` | 11 new keys (`status_*`, `tree_*`) |
| `Reloaded/locales/de.json` | the same keys in German |

Nothing else was touched: `build.sh`, `Reloaded.csproj`, `MapViewForm.cs`,
`Theming.cs`, `I18n.cs`, `MapList.cs` and all repo-root files are unchanged.
No commit and no push was made.

## 1. Map list as a tree

The flat `KryptonDataGridView _mapGrid` is gone.  The alternative Fill
surface is now a plain `System.Windows.Forms.TreeView` (`_mapTree`), because
Krypton has no tree control.

Structure:

```
Category: Detected maps (393)
  Subcategory: Fuel (6)
    N146 Pump voltage map [flashbank 0] @0x4F28 (16x17)
    Start of injection (N108 SOI) 1 [flashbank 0] @0x7404 (16x13)
    ...
  Subcategory: Labeled curves (207)      (collapsed)
  Subcategory: Labeled maps (159)        (collapsed)
  Subcategory: Limiters (9)              (collapsed)
  Subcategory: Misc (6)                  (collapsed)
  Subcategory: Turbo (6)                 (collapsed)
Category: Unknown maps (213)             (collapsed)
```

* Categories and subcategories are sorted alphabetically
  (`StringComparer.OrdinalIgnoreCase`); maps inside a subcategory are sorted
  by flash address.
* Leaves use the map's `Varname` plus `@0x{Address:X}` and
  `(XLen x YLen)`, e.g. `N146 Pump voltage map [flashbank 0] @0x4F28
  (16x17)`.  The `Varname` is preferred over `Description` because the label
  file supplies long paragraph descriptions, not short captions; the full
  description (and the codeblock number) is the leaf's tooltip.
* The leaf's `Tag` is the `MapList.Map` record, so the Varname needed by the
  viewer stays associated with the node.
* Double-click on a leaf calls the existing
  `MapViewForm.ShowMap(path, map.Varname, this)`.  The old try/catch shape is
  kept: a parser/viewer failure is appended to the report box as
  `Map view: ...` and does not break the UI.
* Only real tables are inserted: a symbol whose X and Y axes are both length
  1 is a scalar label, not a map.  The compiled label resource adds 11,370
  such scalars, which would otherwise flood the tree.
* The first category and its first subcategory are expanded on load; the
  rest starts collapsed.
* `View > Views > Maps` still toggles the tree and still excludes the block
  grid (`ShowMapsView` sets `_mapTree.Visible` / `_grid.Visible`).

## 2. Title bar

After a successful file read, `Inspect` computes the identity with the
compiled parser:

```csharp
EDC15VFileParser parser = new EDC15VFileParser();
part     = parser.ExtractPartnumber(image);
software = parser.ExtractSoftwareNumber(image);
bosch    = parser.ExtractBoschPartnumber(image);
info     = parser.ExtractInfo(image);
```

then adds the make and ECU type from
`partNumberConverter.ConvertPartnumber(bosch, image.Length)` (the same source
`frmMain.DetectMaps` uses for its `barAdditionalInfo`), and sets `Text` to

```
{filename} {partnumber} {software} {bosch} {CarMake} {EcuType} {info}
```

with empty fields omitted.  If the `Extract*` block throws, the title is left
untouched (the previous/window title stays); if only the converter throws, the
make/type fields stay empty and the title still carries the four extracted
values.  Because `ChangeLanguage` calls `Inspect` again, the title survives a
language switch.

For `/tmp/ga-work/ga.stage1` the title is:

```
ga.stage1 038906018P SG1623 0281001720 Volkswagen EDC15V-5.29 1,9l R4 EDC
```

## 3. Status bar

A plain `StatusStrip` (no Krypton status strip; the plain one is simpler to
pin to the palette) is docked at the very bottom, below the checksum report,
with a single `ToolStripStatusLabel`:

```
{filename} | Checksum Ok. | {partnumber} {software} {bosch} {CarMake} {EcuType} {info} | {N} symbols
```

* `Checksum Ok.` comes from the already-computed
  `EDC15VM256Checksum.CountOkFields(image)`; when not all three fields are OK
  it shows `Checksum {ok}/{total} fields OK`, and a non-256 KB image shows
  `Checksum n/a`.
* `N` is the number of map leaves actually inserted into the tree (606 for
  this image).
* The strip is updated at the end of `Inspect`, and on the non-256 KB and
  read-error paths.
* The strip and its label take `BackColor`/`ForeColor` from the active
  palette (`FormMain` background + `LabelNormalPanel` text), like the hex
  panel, so dark mode is readable.  `RenderMode = System` makes the system
  renderer honour `BackColor`.

## 4. i18n

New keys, present in **both** `en.json` and `de.json`:

| key | en | de |
|-----|----|----|
| `status_no_file` | No file loaded | Keine Datei geladen |
| `status_read_error` | {0} \| could not read file | {0} \| Datei konnte nicht gelesen werden |
| `status_checksum_ok` | Checksum Ok. | Prüfsumme OK. |
| `status_checksum_partial` | Checksum {0}/{1} fields OK | Prüfsumme {0}/{1} Felder OK |
| `status_checksum_na` | Checksum n/a | Prüfsumme n/a |
| `status_bar` | {0} \| {1} \| {2} \| {3} symbols | {0} \| {1} \| {2} \| {3} Symbole |
| `tree_category` | Category: {0} ({1}) | Kategorie: {0} ({1}) |
| `tree_subcategory` | Subcategory: {0} ({1}) | Unterkategorie: {0} ({1}) |
| `tree_map` | {0} @0x{1:X} ({2}x{3}) | {0} @0x{1:X} ({2}x{3}) |
| `tree_unknown_category` | Unknown maps | Unbekannte Karten |
| `tree_unknown_subcategory` | Unknown | Unbekannt |

The tree is rebuilt by `Inspect` on every language change, so the dynamic
labels (`Kategorie:`, `Unterkategorie:`, `Symbole`) follow the switch.  Parser
text (map names, `flashbank`, `Detected maps`) stays untranslated, as before.

## 5. Verification

Build (with the running app stopped, otherwise Wine locks the exe):

```
cd Reloaded && bash build.sh
```

Result: `Compilation succeeded - 55 warning(s)` / `built: Reloaded.exe
(993280 B)`, exit code **0**.  The 55 warnings are the pre-existing
core-parser warnings.

Headless run on a dedicated display (no `:0`, no `wineserver -k`; only the
PIDs started for this test were stopped):

```
Xvfb :93 -screen 0 1200x800x24 &
DISPLAY=:93 WINEDEBUG=-all wine explorer /desktop=Reloaded93,1000x700 \
  Reloaded.exe --theme=dark "Z:\tmp\ga-work\ga.stage1"
DISPLAY=:93 import -window root shot.png
```

The window is 960x680 at (120,60); screenshots were cropped to the window and
kept in `Reloaded/evidence/` (gitignored).

| file | what it shows |
|------|---------------|
| `lookmatch-blocks-dark.png` | default block grid, dark theme |
| `lookmatch-tree-dark.png` | Maps view: category/subcategory tree, dark theme, status bar with `606 symbols` |
| `lookmatch-tree-de-dark.png` | German: `Unterkategorie: Fuel (6)`, `Prüfsumme OK.`, `606 Symbole` |
| `lookmatch-tree-light.png` | light theme: same tree, `Kategorie: Detected maps (393)` |
| `lookmatch-hex-light.png` | Hex viewer still toggles with the tree and Maps toggle present |
| `lookmatch-title-status-dark.png` | title `ga.stage1 038906018P SG1623 0281001720 Volkswagen EDC15V-5.29 1,9l R4 EDC` and status `ga.stage1 \| Checksum Ok. \| ... \| 606 symbols` |

Checks performed with `xdotool` + screenshots:

1. Title bar after Inspect reads the full ECU identity (also verified with
   `xdotool getwindowname`) - pass.
2. View > Views > Maps shows the tree and hides the block grid - pass.
3. Tree has `Category: Detected maps (393)` -> `Subcategory: Fuel (6)` ->
   address-sorted leaves with `@0x...` - pass.
4. Double-clicking the first leaf opens the existing OxyPlot viewer
   (`Kennfeld: N146 Pump voltage map [flashbank 0]`) - pass.
5. German switch: `Kategorie:` / `Unterkategorie:` / `Prüfsumme OK.` /
   `606 Symbole`; English switch restores the English labels - pass.
6. Theme switch dark -> light: status strip 30,30,30/45,45,45 (dark) vs
   246,246,246/255,255,255 (light); tree text follows - pass.
7. Hex viewer still toggles while the tree is shown - pass.

## 6. Caveats / known limits

* **Where the original caption pieces come from (codegraph).**  In the
  indexed `frmMain.DetectMaps` (`frmMain.cs:259`), the original status bars
  are built as
  `barPartnumber = partNo + " " + softwareNumber` and
  `barAdditionalInfo = info.PartNumber + " " + info.CarMake + " " +
  info.EcuType + " " + ExtractInfo(allBytes)`, with `info =
  partNumberConverter.ConvertPartnumber(boschnumber, length)`.  Reloaded now
  follows the same composition (the converter supplies `Volkswagen
  EDC15V-5.29`).  The original `barSymCount` still counts raw
  `newSymbols.Count` (all 11,980 symbols here), while Reloaded counts the 606
  filtered map leaves it actually shows.
* **The exact caption is slightly shorter than the screenshot.**  The
  converter in this tree returns `EDC15V-5.29`, and `ExtractInfo` returns
  `1,9l R4 EDC`; the screenshot shows `EDC15V-5.29.1,1 R4 EDC` (newer
  converter/parser data, no space after the comma).  The wording and order
  match, only the version suffix and punctuation differ.
* **Counts differ from the original screenshot.**  For this image the
  compiled label resource yields 393 detected maps and 606 real map leaves
  (`606 symbols`), versus `Detected maps (69)` / `729 symbols` in the
  original screenshot.  The grouping logic is count-agnostic; the numbers
  come from `MapList.GetMaps` and the label file.
* **TreeView glyphs are OS-drawn.**  `BackColor`/`ForeColor` follow the
  palette in dark mode, but the expand/collapse boxes and the fixed border
  are drawn by the platform/Wine and do not recolour with the Krypton
  palette.  They are legible in the screenshots but slightly lighter than the
  surrounding dark surface.
* **Tree styling is otherwise plain WinForms.**  Selection uses the system
  highlight; there is no Krypton skinning for the tree (Krypton has no tree
  control).
* **Status count is the number of inserted leaves**, not the raw symbol
  count: scalar "Labeled values" are filtered out before the tree is built.
* **`Unknown maps` stays visible.**  It is a real parser category and is
  sorted/collapsed like any other; it is not hidden.
