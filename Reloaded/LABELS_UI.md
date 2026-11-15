# Load labels (external `.labels` file)

Status: 2026-11-14.  A new **File > Labels > Load labels** action loads an
external `.labels` file and renames the loaded image's map tree with it, on
top of the label file embedded in the executable.

## What changed

| File | Change |
|---|---|
| `Main.cs` | new File-tab group **Labels** with the button **Load labels**; `LoadLabelsFile()` / `ApplyLabelFile()`; report line; `--list-maps --labels=<file>` test seam |
| `MapList.cs` | `GetMaps(file, labels, out MapLabelApplyResult)` and `LoadLabelFile(path, softwareId, imageSize, out error)` |
| `locales/en.json`, `locales/de.json` | 9 new keys (`group_labels`, `file_labels*`, `labels_*`) |

Not touched: `Theming.cs`, `I18n.cs`, `HexViewerControl.cs`, `Gdi3dSurface.cs`,
`MapViewForm.cs`, `MergeSplitForm.cs`, repo-root sources, `build.sh`
(no new file), `Reloaded.csproj`, `Labels/*`.  Nothing committed or pushed.

## UI

File tab -> **Labels** group -> **Load labels** (sub-label ".labels file",
tooltip: "Applies an external .labels file on top of the embedded default
labels. A DAMOS file is converted with tools/damos_import.py.").

The button is disabled until an image is loaded.  It opens a normal
`OpenFileDialog` titled *Load map labels (\*.labels)* filtered to
`*.labels|*.labels`, then applies the file to the **currently loaded** image:

* success -> the tree is rebuilt and the counters are appended to the Blocks
  report area, e.g.
  `Labels ga-external.labels: 3987 rows - 3D 15, curves 0, scalars renamed 0, added 0, skipped 198, already named 11748, rejected 0; maps in tree: 606`
  (the same line also goes to `Console.WriteLine`);
* failure (unreadable file, wrong software id, wrong image size, no label
  rows) -> a warning box with the technical reason, the map tree and the
  previously loaded labels stay untouched;
* no image loaded -> an information box instead of a dialog.

The label choice belongs to the image: **Reload** (`Inspect` on the same path)
keeps and re-applies it, opening a different binary drops it (`_externalLabels`
is cleared), `Close` clears it too.

## Label-application path reused

No new naming scheme.  `MapList.GetMaps(file, labels, out result)` does
exactly what the parser does for the embedded resource:

1. `new EDC15VFileParser().parseFile(file, out codeBlocks, out axisHelpers)` —
   this already applies the embedded `Labels/EDC15V-5_SG1623.labels`;
2. `MapLabelStore.Apply(allBytes, symbols, codeBlocks, labels, suffix, block)`
   with the **same two providers** `EDC15VFileParser.ApplyMapLabels` passes:
   `DetermineNumberByFlashBank` (renamed `MapList.LabelBankSuffix`) and
   `DetermineCodeBlockByByAddress` (`MapList.LabelCodeBlock`);
3. `SymbolTranslator.TranslateSymbolToHelpText` for every symbol, because the
   parser also translates descriptions *after* its label pass — renamed symbols
   therefore get the same description text as with the embedded file.

Matching is therefore unchanged: address plus the block deltas
(`BuildDeltas`), dimension and length checks, axis plausibility, only generic
names ("3D ...", "2D ...", empty, "Generated...") are replaced unless the
row's `source` ends in `-FIX`; 3-D rows only rename, 1-D/0-D rows may create
symbols (category `Detected maps`, subcategories `Labeled curves` /
`Labeled values`).

The external file is validated with `MapLabelStore.Parse(reader, softwareId,
imageSize)` — the same header check (`# software-id=`, `# image-size=`) that
protects the embedded file, so a label set for another software stand can
never be applied silently.  `softwareId` is `ExtractPartnumber(image)` (for
example `038906018P`), `imageSize` the byte length of the loaded file.

## net40 / XP / theme / i18n

* C# 4 only: no `var`-less novelties, no `nameof`, no interpolation, no
  `File.Replace`; only `System.IO`, `System.Windows.Forms` plus the assemblies
  already referenced.  `OpenFileDialog` is the classic common dialog (XP-safe).
* Every user-facing string is an I18n key (`en.json` + `de.json`); the file
  dialog title, filter and the label texts were checked in German.
* The ribbon button/group use the same `CreateGroup`/`CreateButton`/palette
  path as the other File groups; the icon is a generated "labels" glyph in
  `CreateIcon`, so light and dark themes keep working (screenshot below).
  The report box is the existing Krypton report; no palette change.

## Build

```
cd Reloaded && bash build.sh      # exit 0
Compilation succeeded - 55 warning(s)
built: Reloaded.exe (1081856 B)
```

Same 55 warnings as before (legacy parser files); neither `MapList.cs` nor the
new `Main.cs` code adds a warning.

## Verification

Image: `/tmp/ga-work/ga.stage1` (256 KB, part number `038906018P`, SG1623).

### 1. Headless: default labels re-applied (map count must not change)

`Reloaded.exe --list-maps <image> [--labels=<file>] --report=<file>` (new
`--labels` seam in `Program`, same `MapList` calls as the UI):

| Run | Report | MAP_COUNT | MAP lines |
|---|---|---|---|
| baseline (embedded only) | `evidence/labels-baseline.txt` | **11980** | 11980 |
| `Labels/EDC15V-5_SG1623.labels` (the repo default) | `evidence/labels-default.txt` | **11980** | identical, `diff` = 0 lines |
| modified external file (`evidence/ga-external.labels`) | `evidence/labels-external.txt` | **11980** | exactly 3 lines renamed |
| label file with `# software-id=999999999X` | `evidence/labels-rejected.txt` | – | `LABELS_ERROR=the file does not match this image ...` |
| garbage file | `evidence/labels-garbage.txt` | – | `LABELS_ERROR=the file contains no label rows` |

Re-applying the same labels gives `LABELS_3D=15`, `LABELS_ALREADY_NAMED=11748`,
`LABELS_SKIPPED=198` and **zero changed map names** — the 15 are the five
`-FIX` rows of the file, which are renamed again to the identical string
(5 rows x 3 block deltas).  `evidence/labelcheck-default-embedded.txt`
(throwaway harness `evidence/LabelCheck.cs`) confirms `BEFORE=11980`,
`AFTER=11980`, `CHANGED=0`, `ADDED=0`, `REMOVED=0`.

The modified external file (`evidence/ga-external.labels`: the `-FIX` row at
`0x01608` renamed to `ZZ external label test`) proves the overlay reaches the
tree; the two other block copies follow automatically:

```
MAP|ZZ external label test [flashbank 0]||0x01608|128|8x8|block 0
MAP|ZZ external label test [flashbank 0]||0x09608|128|8x8|block 0
MAP|ZZ external label test [flashbank 3]||0x39608|128|8x8|block 0
```

`evidence/labelidentity.txt` (harness `evidence/LabelIdentity.cs`) shows the
same 15 `-FIX` renames by object identity; `evidence/MapProbe.cs` shows the
renamed symbol keeps category `Detected maps` / subcategory `Limiters`, so it
stays in the tree.

### 2. GUI on a dedicated Xvfb (`:96`, light; `:95`, dark; not `:0`, no `wineserver -k`)

`Xvfb :96 -screen 0 1300x900x24`, `wine explorer /desktop=Reloaded96,...`,
`Reloaded.exe --theme=light Z:\tmp\ga-work\ga.stage1`; the dialog was driven
with `xdotool` (`Z:\tmp\ga-work\ga-external.labels`; wine's common dialog
rejects `/`-paths).

| Evidence | Observed |
|---|---|
| `evidence/labels-ui-00-loaded.png` | initial state: tree `Karten (606)`, status `606 Symbole` |
| `evidence/labels-ui-01-filetab.png` | File tab: new **Labels** group, button *Labels laden / .labels-Datei* |
| `evidence/labels-ui-02-dialog.png` | dialog *Kartenlabels laden (\*.labels)*, filter `*.labels` |
| `evidence/labels-ui-06-tooltip.png` | button tooltip: "Applies an external .labels file on top of the embedded default labels. A DAMOS file is converted with tools/damos_import.py." (German table) |
| `evidence/labels-ui-03-tree.png` | after loading: `Unterkategorie: Limiters (9)` expanded, first row **`ZZ external label test [flashbank 0] @0x1608 (8x8)`**, plus `@0x9608` and `@0x39608` — tree header still `Karten (606)`, status `606 Symbole` |
| `evidence/labels-ui-04-report.png` | Blocks report: `Labels ga-external.labels: 3987 Zeilen - 3D 15, Kurven 0, Skalare umbenannt 0, neu 0, übersprungen 198, bereits benannt 11748, abgelehnt 0; Karten im Baum: 606` |
| `evidence/labels-ui-05-dark.png` | `--theme=dark`, File tab: Labels group with dark chrome, readable text |

Map count before/after the apply is identical (606 leaves; the 11 370 added
scalars never become tree leaves — the tree only shows symbols with more than
one row or column, as before).

## Caveats

* The overlay runs **after** `parseFile`, exactly like an "additional" label
  set.  A name already set by the embedded file is kept unless the external
  row is a `-FIX` row; a 3-D map the heuristics never found cannot be invented
  (only 1-D/0-D rows create symbols).  This is `MapLabelStore.Apply`
  semantics, not something this UI adds.
* `MapLabelStore.Apply` runs on the parsed `SymbolCollection`; the parser's
  later steps (`MatchAxis`, `RemoveNonSymbols`) already ran, so newly created
  curves keep the axis data the label row supplied (`ApplyAxisFields`) and are
  not re-matched against the heuristic axis list.
* The report line is appended to the Blocks report box (View > Blocks), which
  is the app's report area; it is intentionally not a modal box.  Under wine
  that Krypton text box painted only after a click/scroll in the test
  screenshot (same behaviour as the checksum report) — a repaint quirk, the
  text is there.
* `tools/damos_import.py` stays the way to turn a DAMOS file into `.labels`;
  the UI only mentions it in the button tooltip.
