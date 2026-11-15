# Write path: target blocks, encode, apply/save

Status: 2026-11-14, branch `reloaded-ui`.
Files touched: `Reloaded/Main.cs`, `Reloaded/MapViewForm.cs`, this note.
Nothing is committed; `build.sh`, `Reloaded.csproj`, `Theming.cs`, `I18n.cs`,
`MapList.cs`, `HexViewerControl.cs` and all repo-root files are unchanged.

The cell editor used to be viewer-only (`CELLEDITOR.md`). It can now write its
grid back into the file: into a temp copy by default, into the opened file only
after an explicit confirmation, replicated into the selected target blocks of a
256 KB EDC15V-5 image, with the three block checksums resealed and verified.

## 1. Target block selection (ribbon)

View tab, new group **Zielblock / Target block**, four check buttons in two
triples (Krypton allows at most three items per triple):

| Button | Mask | Window |
|---|---|---|
| Alle Blöcke / All blocks | `0x7` | blocks 1 + 2 + 3 |
| Block 1 | `0x1` | `0x38000` |
| Block 2 | `0x2` | `0x00000` |
| Block 3 | `0x4` | `0x08000` |

* `GroupButtonType.Check`, mutually exclusive: `SyncTargetBlockButtons()` pins
  all four states from the stored mask after every click and after
  `ApplyTexts()`.
* Choosing **All blocks** asks first ("Kartenänderungen in alle drei
  Datenblöcke schreiben? …", default button *Nein*); declining restores the
  previous check state.
* Persistence: `HKCU\Software\VAGEDCSuiteReloaded`, DWORD `TargetBlockMask` —
  the same key `ThemeStore` (`Theme`) and `I18n` (`Language`) use. Written on
  every change by `TargetBlockStore.Save()`; a missing value or `0` means the
  default **all three blocks**.
* The cell editor's hint line repeats the active choice
  (`… | Zielblock: Block 2`), so a screenshot shows which target is armed.

Bit order is the existing one from `Edc15V256Layout`/`AppSettings`
(bit 0 = block 1 … bit 2 = block 3), so the value is interchangeable with the
original app's setting.

## 2. Encoding the grid

`MapCellGrid.Encode(out int changedCells)` is the exact inverse of the decoder
and mirrors `MapViewerEx.GetDataFromGridView(false)` (`MapViewerEx.cs:2828`):

| map width | original code | our code |
|---|---|---|
| 16 bit | `cellvalue.ToString("X8").Substring(4,2)` then `Substring(6,2)` | `(ushort)value` → high byte, low byte |
| 8 bit | `Convert.ToByte((int)Math.Floor(v))` for `v >= 0` | `(byte)(value & 0xFF)` |

* The cell value is a signed `int`; the cast to `ushort` is the two's
  complement wrap the format needs (`-500` → `0xFE0C` → bytes `FE 0C`).
* The buffer is **high byte first**, which is what
  `Tools.savedatatobinary` expects: that method applies
  `Tools.reverseEndian` (16-bit swap) before writing, producing the
  little-endian bytes the file holds.
* The buffer starts as the map bytes read with the same call the viewer used
  (`Tools.readdatafromfile`, which already swaps). Cells outside the table
  therefore keep their file bytes instead of being zeroed (the original
  leaves them `0`).
* `changedCells` is counted against that baseline; `0` means "nothing to do".

Example (16-bit): value `1234` = `0x04D2` → encoded `04 D2` → file bytes
`D2 04`. Verified on disk (see §6).

## 3. Apply and Save

`MapCellGrid` has two buttons under the table:

* **Übernehmen (Temp-Kopie)** → `ApplyToTempCopy(TargetBlockStore.Mask)`.
* **Datei speichern …** → same pipeline, then a Yes/No prompt naming the file
  and the target blocks; only a verified temp copy is copied over the
  original (`File.Copy(temp, original, true)`). A failed verification never
  touches the original.

`MapWritePath.ApplyToCopy` (`MapViewForm.cs`) does, on a copy:

1. temp path `%TEMP%\reloaded-apply-<source file name>` (fixed per source, so a
   test can find it);
2. odd lengths are refused up front — `reverseEndian` returns a zeroed buffer
   for them, exactly why `TargetBlockWriter` refuses them;
3. `Tools.Instance.IsEdc15V256KImage` decides whether the block layout
   applies (256 KB **and** the known marker pair at all three block ends);
4. plan (`TargetBlockWriter.Plan`): `DescribeProblem` on source and on every
   `AddressInBlock(target, address)`; mask → `BlocksFromMask`, empty mask =
   source block only. `MapList.GetMaps` probes whether a symbol of the same
   length exists at the same relative offset in each target (warning only,
   like the original);
5. write each target with `Tools.Instance.savedatatobinary(address, length,
   encoded, temp, false, "Reloaded: <map>", EDCFileType.EDC15V)` — the
   original writer, its byte swap and its 256 KB `RejectUnsafeWrite` guard;
6. compare the three 32 KB windows before/after for the "changed" report;
7. reseal only the written blocks: `EDC15VM256Checksum.FixSelected(bytes,
   fields)` with `fields[Edc15V256Layout.ChecksumIndex(block)] = true`;
8. `EDC15VM256Checksum.Verify` + `CountOkFields`; the dialog ends with
   **"Prüfsumme OK"** or the failure, and lists every target address.

Non-256 KB files keep the old behaviour: one write at the map address, no
replication, no checksum step.

## 4. Reused methods (nothing invented)

| What | Where | Used for |
|---|---|---|
| `Tools.Instance.readdatafromfile` | `Tools.cs:1199` | baseline bytes (incl. `reverseEndian`) |
| `Tools.Instance.savedatatobinary` | `Tools.cs:664` | raw write, byte swap, write guard |
| `Tools.Instance.reverseEndian` | `Tools.cs:1151` | (inside the writer) |
| `Tools.Instance.IsEdc15V256KImage` | `Tools.cs` | layout detection |
| `Tools.Instance.SuppressDialogOnRejectedWrite` | `Tools.cs` | no modal during the write |
| `Edc15V256Layout.BlockOf/RelativeOffset/AddressInBlock` | `Edc15V256Layout.cs` | block arithmetic |
| `Edc15V256Layout.DescribeProblem/BlocksFromMask/ChecksumIndex/BlockNames` | same | validation, reporting |
| `EDC15VM256Checksum.FixSelected/Verify/CountOkFields/BlockCount` | `EDC15VM256Checksum.cs` | reseal + verify |
| `MapViewerEx.GetDataFromGridView(false)` | `MapViewerEx.cs:2828` | encoder rule (mirrored, see §2) |
| `MapViewerEx.gridView1_ValidatingEditor` | `MapViewerEx.cs:5055` | already in the grid (78643 / 255) |
| `TargetBlockWriter.Plan/Execute` | `TargetBlockWriter.cs` | flow (mirrored, see note below) |
| `MapList.GetMaps` | `Reloaded/MapList.cs` | symbol probe |

`TargetBlockWriter.cs` is **not** compiled into `Reloaded`: `build.sh` and
`Reloaded.csproj` list their sources explicitly and `MapViewForm.cs` cannot
`using` a type that is not in the compilation. `MapWritePath` therefore repeats
the same steps with the same repo-root helpers.

**Verified 1:1 (2026-11-14):** the helper set is identical — both paths use
`Edc15V256Layout.BlockOf/RelativeOffset/AddressInBlock/DescribeProblem/
BlocksFromMask/ChecksumIndex/BlockNames`, `EDC15VM256Checksum.Verify/CountOkFields/
FixSelected/BlockCount`, and `Tools.savedatatobinary` (TargetBlockWriter receives
it as a delegate, `MapWritePath` calls `Tools.Instance.savedatatobinary`
directly). This is **deliberate duplication with a delegation option**: if
`TargetBlockWriter.cs` is ever added to `build.sh`, `MapWritePath.ApplyToCopy` can
delegate to `TargetBlockWriter.Plan/Execute` unchanged. No behavioural divergence
was found.

## 5. Notes and limits

* Everything is still `net40`/XP-safe C# 4 (no LINQ, no tuples, no
  interpolation); the grid, chart, undo stack and theming are unchanged.
* Apply always writes the whole map (not only changed cells) — the allowed
  alternative in the task; the report shows the number of differing cells.
* The `Verify` report numbers blocks in *field* order (`0x07FFC`,
  `0x0FFFC`, `0x3FFFC`), while the messages name `Edc15V256Layout` blocks
  (block 1 = `0x38000`). Both lines are printed; they are the same three
  fields, just differently ordered.
* Replication is byte-based: a target block gets the source block's table
  content, even if that block held a different calibration at the same offset
  (visible on `ga.stage1`: block 1 at `0x399DC` differs from block 2). That is
  why "All blocks" asks first.
* `Datei speichern` does not reload the app's own status bar/checksum display;
  the fields it shows were computed at load time. Use File → Reload after a
  save.
* No transaction-log entry is written (the Reloaded app has no project log);
  `savedatatobinary` is called with `DoTransActionEntry = false`.
* 8-bit maps: `readdatafromfile` byte-swaps pairs and the grid decodes that
  buffer, so an even-length 8-bit map round-trips unchanged; odd lengths are
  refused (see §3). In this label file 8-bit symbols are 1x1 scalars, i.e.
  length 1, so they are not writable — the same limitation as before.
* Diagnostics in `Program.Main` for headless runs: `--apply-test` (§6) and
  `--list-maps --report=<file> <binary>` (dumps the parser's map list with
  varname, address, length, XxY).
* `CELLEDITOR.md` still says "no file is written / out of scope"; this note is
  the current description of the save path.

## 6. Verification (2026-11-14)

Build:

```
cd Reloaded && bash build.sh      # Compilation succeeded - 55 warning(s)
```

(55 = the pre-existing legacy warnings; no new ones.)

Headless self-test on a dedicated Xvfb `:90` (not `:0`, no `wineserver -k`),
input `/tmp/ga-work/ga.stage1` (256 KB, P markers) copied per run:

```
wine Reloaded.exe --apply-test --map="Driver's Wish [flashbank 0]" \
     --cell=2,0,1234 --target=<mask> --out=Z:\tmp\ga-work\ga.apply-all.bin \
     --report=Z:\tmp\ga-work\report-all.txt Z:\tmp\ga-work\ga.stage1
```

`MapViewForm.FromBinary` → `MapCellGrid.SetCellValue` (the committed-edit path)
→ `ApplyToTempCopy(mask)`. Reports: `evidence/apply-report-*.txt`.

| run | targets | changed blocks | checksums | result |
|---|---|---|---|---|
| mask 7 (all) | 1,2,3 | 1,2,3 | 3/3 → 3/3 | OK |
| mask 1 (block 1) | 1 | 1 | 3/3 → 3/3 | OK |
| mask 7, value `-500` | 1,2,3 | 1,2,3 | 3/3 | OK (wrap `0C FE`) |

Byte-level diff against the original (`evidence/apply-diff-summary.txt`):

* map at `0x019DC` (block 2), 256 bytes; cell index 2 (`col=2,row=0`) at
  `0x019E0`: `54 0B` → `D2 04` (= 1234) in blocks 1/2/3 at
  `0x399E0`/`0x019E0`/`0x099E0`;
* checksum fields `0x07FFC`, `0x0FFFC`, `0x3FFFC` rewritten;
* with mask 1 only the block 1 window and `0x3FFFC` change — blocks 2/3 stay
  byte-identical, and `Verify` is still 3/3.
* nothing else in the 256 KB changes (191 bytes total for mask 7, all inside
  the three map windows).

GUI on the same Xvfb (`explorer /desktop=Reloaded90`), final binary:

| step | evidence | observed |
|---|---|---|
| ribbon group | `evidence/targetblock-ribbon.png` | View tab shows *Zielblock* with *Alle Blöcke / Block 1 / Block 2 / Block 3* |
| default | `evidence/targetblock-default-all.png` | with no HKCU value, *Alle Blöcke* is checked |
| choose Block 2 | `evidence/targetblock-block2-checked.png` | Block 2 checked, others clear; `HKCU\…\TargetBlockMask = 0x2` |
| confirm All | `evidence/confirm-all-blocks.png` | clicking *Alle Blöcke* asks before arming it; *Nein* keeps Block 2 (registry still `0x2`) |
| open map | `evidence/viewer-block2-open.png` | `Start of injection (N108 SOI) 3` @`0x3F404` (block 1), 13×16 cells; hint `… \| Zielblock: Block 2` |
| edit cell | `evidence/viewer-block2-edited.png` | first cell 50 → 1234; status `Spalte 1 (X = 0) Zeile 2 (Y = 800) Wert = 50 \| Änderungen: 1` |
| Apply | `evidence/apply-dialog-block2.png` | Quelle Block 1 `0x3F404` 416 Bytes; `Block 2 -> 0x07404`; Geändert/Neu versiegelt: Block 2; Prüfsummen 3/3 → 3/3; **Prüfsumme OK**; Zelldifferenzen 1 |
| after Apply | `evidence/viewer-block2-applied.png` | status `Übernommen: Block 2, Prüfsumme 3/3 OK` |
| bytes | `evidence/apply-diff-gui.txt` | 20 bytes total: first cell `0x07404` `32 00 → D2 04` (= 1234), 14 further data bytes where block 1's table differs from block 2's at the same offset, and `0x07FFC-0x07FFF`; blocks 1 and 3 byte-identical |

Independent byte check of that run:

```
$ xxd -s 0x7404 -l 2 ga.apply-gui3.bin                       # 3200  (file bytes 32 00)
$ xxd -s 0x7404 -l 2 ~/.wine/.../Temp/reloaded-apply-ga.apply-gui3.bin
                                                            # d204  (file bytes d2 04)
$ xxd -s 0x7FFC -l 4 ga.apply-gui3.bin                       # efae25ec
$ xxd -s 0x7FFC -l 4 .../reloaded-apply-ga.apply-gui3.bin    # 9f6a9c68
$ xxd -s 0x3F404 -l 2 .../reloaded-apply-ga.apply-gui3.bin    # 3200  (source block 1 untouched)
```

The original file was never written in either test: all writes went to
`%TEMP%\reloaded-apply-*.bin`; `Datei speichern …` was not confirmed.
