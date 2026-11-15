# Merge / Split chips in the Reloaded build

Wires the existing UI-free `ChipMergeSplit` helper (repo root) into the
DevExpress-free Reloaded app and adds a small dialog for it.

## What changed

| File | Change |
|---|---|
| `../ChipMergeSplit.cs` | **unchanged**, now compiled into Reloaded |
| `MergeSplitForm.cs` | new Krypton/WinForms dialog (merge + split) |
| `Main.cs` | new File > Chips group with the "Merge/Split" ribbon button |
| `build.sh` | `../ChipMergeSplit.cs` in the source list, `MergeSplitForm.cs` in the compile line |
| `Reloaded.csproj` | matching `<Compile>` entries (linked root source + new form) |
| `locales/en.json`, `locales/de.json` | one new group/button key pair plus the dialog strings |

Not touched: `Theming.cs`, `I18n.cs`, `MapList.cs`, `HexViewerControl.cs`,
`Gdi3dSurface.cs`, `MapViewForm.cs`, repo-root files. Nothing committed or
pushed.

## UI

File tab -> **Chips** group -> **Merge/Split** (sub-label "128 + 128 KB").
One dialog, two sections (no tabs: both features stay visible at once):

**Merge (two chips -> one image)**
* LOW (even offsets) / HIGH (odd offsets) file boxes, a **Swap** button.
* Output box, pre-filled with `ChipMergeSplit.SuggestMergedPath(low)`; the
  suggestion is only replaced while the user has not picked a target by hand.
* Preview box: `ChipMergeSplit.Preview` of both halves and
  `ChipMergeSplit.PreviewMerged` of the interleaved start.
* Status line: both sizes equal and 128 KB -> "ready to merge", otherwise the
  exact reason (differing sizes, unexpected size).
* **Merge** confirms overwrite when needed, refuses a target that is one of
  the inputs, warns (Yes/No) when the halves are not 128 KB, then reports the
  result size and whether `038906018P` was found (`FindPartnumber`, hint only).

**Split (one image -> two chips)**
* Image box (256 KB), output folder box (auto-filled from the image).
* `lowIsEven` choice as two radio buttons: even offsets -> LOW (standard) and
  odd offsets -> LOW (swapped).
* Target line shows both file names from `ChipMergeSplit.SuggestHalfName`
  (a trailing `_merged` is dropped, so `flashed_merged.bin` yields
  `flashed_low.bin` / `flashed_high.bin`).
* Status line mirrors the merge checks (odd length, wrong size).
* **Split** confirms overwrite, refuses halves that would overwrite the input,
  warns on a non-256 KB file, writes both halves and reports their sizes.

### Safety

* Both actions build the complete byte array first; `WriteAtomic` writes a
  temp file in the target directory and only then replaces the target. A
  failure never leaves a truncated output file (the temp file is deleted).
* Every action body is wrapped in try/catch; failures show the exception
  message in a themed message box.
* The byte order lives only in `ChipMergeSplit`; the dialog never touches
  bytes itself.

### Theming

The form takes the opening window's `KryptonPalette` and derives its own
background, text, muted and border colours from it (same rule as
`MainForm.ApplyChromePalette`). Verified in light and dark theme
(`evidence/mergesplit-ui-07-dark.png`); the Krypton text boxes, buttons and
radio buttons follow the palette by themselves. No change to `Theming.cs`.

### net40 / XP

C# 4 only: no auto-properties with initialisers, no `nameof`, no string
interpolation, no interpolation of null-conditional operators. Only
`System.Windows.Forms`, `System.Drawing`, `Krypton.Toolkit` and the already
referenced assemblies. No `File.Replace` (wine-mono support differs): the
target is deleted and the fully written temp file moved over it.

## Build

```
cd Reloaded && bash build.sh
Compilation succeeded - 55 warning(s)
built: Reloaded.exe (1076736 B)
```

Exit code 0. The 55 warnings are the pre-existing legacy ones; neither
`MergeSplitForm.cs` nor the changed `Main.cs` produces one.

## Verification

### 1. Headless byte-level check

Throwaway harness `evidence/MergeSplitCheck.cs` (compiled against
`../ChipMergeSplit.cs` only, run under wine; the exe and its output are in the
gitignored `evidence/`):

```
cd Reloaded/evidence
wine "C:\windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo \
     /target:exe /out:MergeSplitCheck.exe MergeSplitCheck.cs ..\..\ChipMergeSplit.cs
wine MergeSplitCheck.exe \
     /home/fran/Dokumente/ecu-a4b5/bins/A4B5_frone2023_low.bin \
     /home/fran/Dokumente/ecu-a4b5/bins/A4B5_frone2023_high.bin \
     /home/fran/Dokumente/ecu-a4b5/bins/A4B5_038906018P_SG1623_frone2023_merged.bin \
     mergesplit-work mergesplit-check.txt
```

`evidence/mergesplit-check.txt` (exit 0, `RESULT=PASS`):

| Check | Result |
|---|---|
| LOW / HIGH input | 131072 / 131072 bytes |
| `Merge(low, high)` | **262144 bytes** (256 KB) |
| merged vs. reference `..._frone2023_merged.bin` | byte-identical (sha256 `933c2185…af5b59a7`) |
| `Split(merged, lowIsEven: true)` | **131072 / 131072 bytes** |
| round-trip halves vs. originals | byte-identical (`8fdf6e91…74467b` / `1b2597bc…18cda05`) |
| `Split(merged, lowIsEven: false)` | halves exactly swapped |
| part number `038906018P` | found at `0x02F88` |
| unequal halves | rejected with `ArgumentException` (nothing written) |

### 2. GUI run on a dedicated Xvfb

`Xvfb :97` (not `:0`, no `wineserver -k`), `wine explorer /desktop=...`;
screenshots in `evidence/`:

| Evidence | Observed |
|---|---|
| `mergesplit-ui-00-ribbon.png` | File tab, new **Chips** group with "Merge/Split / 128 + 128 KB" |
| `mergesplit-ui-03-filled.png` | two halves selected, LIVE preview `55 55 00 00 56 56 33 33 …`, status "LOW/HIGH je 131072 Byte (128 KB) - bereit zum Merge." |
| `mergesplit-ui-04-merge-done.png` | "Merge abgeschlossen. Ergebnis: 262144 Byte (256 KB) / Teilenummer 038906018P: gefunden bei 0x02F88" |
| `mergesplit-ui-06-split-done.png` | target names `flashed_low.bin` / `flashed_high.bin`, "Länge 262144 Byte (256 KB) - bereit zum Split." |
| message after Split | "LOW: 131072 Byte (128 KB) / HIGH: 131072 Byte (128 KB)" |
| `mergesplit-ui-07-dark.png` | dialog in dark theme, palette-derived background, readable labels |

The files written by the GUI were checked on disk: merged image
`262144` bytes, sha256 `933c2185…af5b59a7` (identical to the reference);
`flashed_low.bin` / `flashed_high.bin` `131072` bytes each, sha256 identical
to `A4B5_frone2023_low.bin` / `_high.bin`.

## Caveats

* **No checksum validation.** The old DevExpress dialog additionally ran
  `EDC15VM256Checksum.Verify` after a merge. The Reloaded dialog only reports
  size and the part-number hint; the checksum report is available through the
  normal "Blocks" view once the merged file is opened. Deliberate: merging
  chips is a byte operation, a checksum mismatch is not a merge error.
* A merge of two halves that are not 128 KB is allowed after an explicit
  Yes/No warning (same behaviour as `frmBinmerger`), so the result is then not
  a 256 KB image.
* GUI clicks in the verification were done with `xdotool`; `Ctrl+A` inside a
  Krypton text box did not clear the box in that setup (test-harness quirk,
  not an app path), so the split test was driven with an auto-filled folder.
* `SuggestMergedPath` is next to the LOW file; on a read-only source directory
  the user must pick another target (the dialog reports the write error and
  writes nothing).
