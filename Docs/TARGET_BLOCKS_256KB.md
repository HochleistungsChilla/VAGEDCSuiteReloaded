# Target block selection (256 KB EDC15V-5)

When writing a map change into a 256 KB EDC15V-5 file
(038 906 018 P / SG 1623, marker `27 F3`; 038 906 018 GA, marker `84 F1`),
it is possible to choose into which of the three data blocks the write goes.
Only these images are affected: the family is detected by the file size
`0x40000` **and** by a known marker pair at all three block ends
(`Edc15V256Layout.HasKnownMarkers`). Other 256 KB files (e.g.
`REF_A4B5_AFN_028906021CE…`) remain untouched.

## Block numbers and addresses

The numbering follows the parser's `CodeID` values (`CodeBlock`), not the
order in the file:

| Block | Window | Data range | Checksum field | Parser `CodeID` |
|---|---|---|---|---|
| 1 | `0x38000`–`0x3FFFF` | `0x38B80`–`0x3FFFB` | `0x3FFFC` | 1 |
| 2 | `0x00000`–`0x07FFF` | `0x00B80`–`0x07FFB` | `0x07FFC` | 2 |
| 3 | `0x08000`–`0x0FFFF` | `0x08B80`–`0x0FFFB` | `0x0FFFC` | 3 |
| Code | `0x10000`–`0x37FFF` | – | – | – |

A block is a 32 KB window (`0x8000`). The data range begins at
`+0xB80`; before it lies the vector/reserved range. The four bytes before the
window end are the checksum field and do not belong to the computation.

### Computation when writing multiple blocks

The **relative offset within the window** stays the same, not the absolute
address:

```
relativ    = adresse & 0x7FFF
ziel       = blockbasis + relativ          (base 0x38000 / 0x00000 / 0x08000)
```

Example, map at `0x01A20` (block 2), selection "all":

```
relativ = 0x01A20 & 0x7FFF = 0x1A20
Block 1 -> 0x38000 + 0x1A20 = 0x39A20
Block 2 -> 0x00000 + 0x1A20 = 0x01A20
Block 3 -> 0x08000 + 0x1A20 = 0x09A20
```

Using the absolute address `0x01A20` in block 1 or 3 would be wrong and is
never done.

## Write path (inventory)

The real path of a map change, traced in the code:

1. `MapViewerEx` saves via `onSymbolSave` and passes
   `new SaveSymbolEventArgs(m_map_address, …)`. `m_map_address` is the
   **absolute** file address of the symbol, set in `frmMain` from
   `sh.Flash_start_address` (parser offset, e.g. `t + 8 + …`).
2. `frmMain.tabdet_onSymbolSave` → `SaveDataIncludingSyncOption(fileName,
   varName, address, length, data, useNote, note)`.
3. Until now, `Tools.savedatatobinary(address, length, data,
   fileName, useNote, note, type)` wrote directly to exactly this absolute
   address there (`FileStream`/`BinaryWriter`, position = `address`).

There was **no** explicit block decision. Block membership existed only
implicitly in two places:

- `SymbolHelper.CodeBlock` = `DetermineCodeBlockByByAddress(...)`, filled from
  `codeBlockList` (for this file: `0xC00..0x8BFF` = CodeID 2,
  `0x8C00..0x10BFF` = CodeID 3, `0x38C00..0x40BFF` = CodeID 1).
- the optional "CodeBlockSync" (`m_appSettings.CodeBlockSyncActive`) in
  `SaveDataIncludingSyncOption`: it copies a change to symbols with the
  same length and `(adresse & 0x0FFFF)` equality or the same
  code block offset. That is a 64 KB mask, not a block selection, and it
  writes only into symbols that the parser already knows.

Measurement on the reference image (`A4B5_038906018P_SG1623_Original.bin`),
parser run headless: 729 symbols, 3 code blocks, distribution **169 / 168 / 168**
over blocks 1/2/3. 496 symbols have a counterpart at the same relative position
in all three blocks (495 of them with the same length); 22 symbols lie in the
reserved range before `+0xB80` and are heuristic hits.

## New logic

| File | Content |
|---|---|
| `Edc15V256Layout.cs` | layout, block numbers, `RelativeOffset`, `AddressInBlock`, `RangeFitsDataArea`, `DescribeProblem`, `HasKnownMarkers`, masks |
| `TargetBlockWriter.cs` | `Plan` (validate, determine targets) and `Execute` (write, compare, seal) – without UI, raw write as delegate |
| `frmTargetBlocks.cs` | dialog with Block 1 / Block 2 / Block 3 / "Alle drei Blöcke" (all three blocks) |
| `Tools.cs` | `IsEdc15V256KImage`, `RejectUnsafeWrite` (lock in both `savedatatobinary` overloads) |
| `frmMain.cs` | menu item "Zielblock" (target block) (ribbon group *General*), `TrySaveToSelectedTargetBlocks` |
| `AppSettings.cs` | `TargetBlockMask` (bit 0/1/2 = block 1/2/3), registry key `TargetBlockMask` |

Flow when saving (`SaveDataIncludingSyncOption`,
`SaveAxisDataIncludingSyncOption`, Excel import):

1. Mask 0 → previous path, unchanged (only the block from which the map
   was loaded).
2. Mask ≠ 0 and 256 KB EDC15V-5 → `TargetBlockWriter.Plan`:
   - the source must lie in a data block, `relativ ≥ 0xB80`,
     `relativ + length ≤ 0x7FFC`;
   - each target is validated with the same check (no code region);
   - if a target block lacks an equally long symbol at the same
     relative position, the application asks before writing.
3. `Execute` writes each target block via `Tools.savedatatobinary` (so that
   `TransactionEntry` and the project logbook are filled as before),
   compares the three windows before/after and calls
   `EDC15VM256Checksum.FixSelected` **only** for the target blocks.
4. Report (message box/status bar): written blocks, actually
   changed blocks, newly sealed fields, checksums before/after.

The old `CodeBlockSyncActive` logic is kept for mask 0 and is skipped when
target block selection is active (otherwise writing would happen twice).

## Safety

- Code region `0x10000`–`0x37FFF`: never a write target, neither as source nor
  as target. `RejectUnsafeWrite` additionally rejects it in
  `Tools.savedatatobinary` independently of the target block selection.
- Reserved range `< +0xB80`, checksum field and ranges beyond the
  window end are rejected (with a reason in the dialog).
- Odd lengths are rejected: `reverseEndian` returns a null buffer for them,
  which would destroy data.
- Multiple selection warns before writing (dialog) and in the result
  ("all three blocks").

## Tests

`tools/TargetBlockTest.cs`, built and run headless via Wine csc
(`TargetBlockTest.exe <bindir>`), 60 checks:

| Case | Result |
|---|---|
| only block 2 → blocks 1/3 byte-identical including field, only block 2 sealed | OK |
| all three → value at `0x39A20`/`0x01A20`/`0x09A20`, 3/3 | OK |
| block 1 + 3 → block 2 byte-identical | OK |
| mask 0 → only source block | OK |
| code region `0x20000` → rejected, file unchanged | OK |
| reserved range `0x400` → rejected | OK |
| `0x7FF8 + 8` (over the field) → rejected | OK |
| odd length → rejected, file unchanged | OK |
| 8 clean P references | **24/24** fields |
| `agroff_raw` | 0/3 as expected, `Fix` → byte-identical to `agroff_checked` |
| `Fix` on a correct image | byte-identical (idempotent) |
| GA variant (marker `84 F1`, synthetically replaced) | detected, 3/3 after `Fix` |

## Build and check

```
# application incl. .resx (builds csc + resgen + /resource:, see tools/build-wine.sh)
tools/build-wine.sh

# target block tests (without UI)
csc /nologo /out:build\\TargetBlockTest.exe /r:System.dll /r:System.Core.dll \
    Edc15V256Layout.cs TargetBlockWriter.cs EDC15VM256Checksum.cs tools/TargetBlockTest.cs

# write lock + transaction log (full source tree without Program.cs)
csc /nologo /target:exe /main:GuardProbe /out:build\\WriteGuardTest.exe \
    @rsp-ohne-Program.cs tools/WriteGuardTest.cs
```

Since this change, `b.rsp` additionally contains `Edc15V256Layout.cs`,
`TargetBlockWriter.cs` and `frmTargetBlocks.cs`; the duplicate entry for
`EDC15VM256Checksum.cs` is removed.

### Embedding resources (GUI start)

`csc` does not convert `.resx` itself — only MSBuild does that.
`tools/build-wine.sh` does it in two steps:

1. `tools/mkresources.py` derives the manifest name of each `.resx` from
   `RootNamespace` + file path (`frmMain.resx` → `VAGSuite.frmMain.resources`)
   and checks it against the type name in the code.
2. The `resgen.exe` supplied with Wine
   (`C:\windows\mono\mono-2.0\lib\mono\4.5\resgen.exe`) generates
   `.resources` from them; `csc` receives them as `/resource:<datei>,<name>`.
   `MONO_PATH` must point to `refs/`, otherwise `resgen` fails on typed
   entries (Nevron/DevExpress).

38 neutral resources end up in the EXE; the four `.nl.resx` go into the
satellite `build/nl/EDCSuite_merged.resources.dll`.

## Limits

- Without the embedded resources the UI does not start
  (`MissingManifestResourceException`). With `tools/build-wine.sh` it starts
  under Wine; checked: main window, ribbon button "Zielblock" (target block)
  (Actions → General) and the dialog itself.
- The `.nl` translations need the satellite next to the EXE; without it the UI
  falls back to the neutral (German) texts.
- The logic was checked headless, and the
  DevExpress wiring (`Items.Add` + `ItemLinks.Add` + `ItemClick`) in a
  separate runtime test.
- Only 256 KB EDC15V-5 images. For 512 KB / 1 MB and other families everything
  stays with the previous behavior. If the selection is set but the file is
  256 KB without a known marker pair, a one-time hint explains that the
  target block logic does not apply.
- The marker pair itself lies in the data range (`block end − 6`). A
  faulty write that overwrites it makes the image "foreign" for target block
  detection; the checksum procedure is unaffected by this.
- Whether a map table has the same meaning *in content* in another block is
  not checked by the application; it compares only offset and length.
