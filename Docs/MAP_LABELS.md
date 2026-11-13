# Label files for EDCsuite

Status: 2026-11-12. Supplements the parsers' heuristics with a data-driven
set of names, factors and axes for **one** software version. No build executed
in this repo; verification runs via a Python model of the same rules
(`tools/verify_map_labels.py`).

## Files

| File | Purpose |
|---|---|
| `Labels/EDC15V-5_SG1623.labels` | Data set for 038906018P / EDC15V-5.29, version SG1623 (image 256 KiB) |
| `MapLabelFile.cs` | `MapLabel`, `MapLabelStore` – load, check, apply |
| `tools/generate_map_labels.py` | generates the label file from DAMOS export + comparison CSV |
| `tools/verify_map_labels.py` | offline model of the application, before/after table |
| `Docs/MAP_LABELS_VERIFY.md` | generated result table |
| `refs/` | reference assemblies for the build (copies, see Build) |

Wired into `VAGSuite.csproj`: `MapLabelFile.cs` as `Compile`,
`Labels\EDC15V-5_SG1623.labels` as `EmbeddedResource`.

## Format v2.1

UTF-8, one record per line, `;`-separated, `#` lines are comments.
Header lines:

```
# EDCsuite map labels v2.1
# software-id=038906018P
# software-stand=SG1623
# ecu-type=EDC15V-5.29
# image-size=0x40000
# block-base=0x00C00
# type;addr;name;unit;factor;offset;len;rows;axis_min;axis_max;axis_unit;axis_factor;axis_offset;axis_name;source;bytes;axis_addr
```

| Field | Meaning |
|---|---|
| `type` | `3d` map table, `1d` curve, `0d` single value |
| `addr` | absolute file address of the **block 2** instance |
| `name` | display name (DAMOS name; `;` is forbidden in the name) |
| `unit` | unit of the value, `-` = none |
| `factor`, `offset` | display: `value = raw * factor + offset` |
| `len` | `3d`: suite `X_axis_length` (= DAMOS `rows`); `1d`: number of support points; `0d`: 1 |
| `rows` | `3d`: suite `Y_axis_length` (= DAMOS `cols`); `1d`/`0d`: 1 |
| `axis_min`, `axis_max` | raw end values of the 16-bit axis (only `1d`, otherwise empty) |
| `axis_unit`, `axis_factor`, `axis_offset`, `axis_name` | axis metadata (only `1d`) |
| `source` | origin; suffix `-FIX` = deliberately overwrite the existing heuristic name |
| `bytes` | storage width of a value: 1 or 2 |
| `axis_addr` | file address of the support points, set only for 16-bit axes |

Examples:

```
3d;0x0128C;air loss moment-Map At 100% Last;Nm;0.1;0.0;4;4;;;;1.0;0.0;;DAMOS-038906018P-0498-SG1629;2;
1d;0x011B0;Gate-array answers = f(Gate-array questions);-;1.0;0.0;2;1;0;15677;-;1.0;0.0;acceptance of fixed values from 0 to 65535;DAMOS-038906018P-0498-SG1629;2;0x011AC
0d;0x0118A;EEPROM Initialization DSV;-;1.0;0.0;1;1;;;;1.0;0.0;;DAMOS-038906018P-0498-SG1629;2;
```

Older lines are still read: the old 8-column form
(`addr;name;unit;factor;offset;cols;rows;source`) counts as `3d`, the short
v2 form without `bytes`/`axis_addr` as 16-bit.

## Address model

`addr` is the file address in the block 2 image. The other two code blocks
contain the same object at `addr + 0x8000` (block 3, 4x4) and
`addr + 0x38000` (block 1, automatic). The loader forms the candidates from the
detected `CodeBlock` boundaries; if the parser finds no blocks, it uses exactly
these two fixed distances. `axis_addr` is shifted by the same delta.

Evidenced: all 4052 DAMOS objects (62 × `eZweidim`, 134 × `eEindim`,
3856 × `eEinzel`) lie in all three blocks at these distances (0 deviations),
and no two objects overlap when the storage width from the DAMOS is taken into
account.

## Orientation

DAMOS and the suite are rotated by 90° for map tables (DAMOS 16×12 appears as
suite 12×16). Hence `len = DAMOS rows`, `rows = DAMOS cols`. Curves are
**not** rotated: the suite holds them as `X_axis_length = support points`,
`Y_axis_length = 1`, support points in `X_axis_address` (this is also what
`EDC17FileParser` does). Single values are 1×1.

## Storage width

Decisive is `DataOrg` (width of the object itself) in the DAMOS CSV:
`eByte` = 1 byte, `eLoHi` = 2 bytes. **Not** `AxisX.DataOrg` – that is the
width of the axis and is `eByte` even for objects without an axis; anyone who
takes it for the value regards all single values as 8 bit (wrong).

Result for this version (block 2, 4052 objects):

- 62 map tables: 2 bytes
- 134 curves: 69 × 2 bytes, 65 × 1 byte (8-bit tables such as the
  XCOM conversion errors, 6 ASCII identifiers)
- 3856 single values: 3123 × 2 bytes (addresses on the 2-byte grid), 733 × 1
  byte (addresses byte-wise, e.g. `0x11A4`, `0x11A5`, `0x11A6`)

The map viewer reads 16 bit by default. Therefore the loader sets
`SymbolHelper.ValueBytes`; `frmMain` passes it to `ShowTable(columns, sixteenBits)`
and also computes the difference view for 8-bit objects byte-wise.
All symbols created by the heuristic stay at `ValueBytes = 2`; the behavior
without a label file is unchanged.

**Exception curves:** `CheckMap()` always reads a curve as a 16-bit pair
(axis 2 bytes per support point, data 2 bytes). An 8-bit curve object would
have to be bent in the symbol (length halved, axis address shifted) – that is
risky and affects only 65 objects (58 XCOM conversion errors, 6 ASCII
identifiers, 1 alias table). These lines stay in the file (`bytes=1`); the
loader skips them with a log line. Single values, by contrast, are created in
both widths (`Length` 1 or 2).

## Application

`ApplyMapLabels()` runs in `MSA15FileParser` and `EDC15VFileParser` directly
after `NameKnownMaps()`. Rules:

1. **3d** only renames: the match via address, `X_axis_length`/`Y_axis_length`
   and `Length` must fit, both axes monotonic and within the image. A 3-D map
   table that the heuristic did not find is **not** invented (a wrong grid
   would be worse than no name).
2. **1d/0d** create a symbol when none exists at the address. The range must be
   free (no map table, no axis, no earlier label), the name unique. For curves
   the axis is additionally checked: `axis_min`/`axis_max` against the
   first/last raw values, otherwise monotonicity.
3. Already named symbols stay untouched, unless `source` ends in `-FIX`
   (the five corrections).
4. Names are unique: if DAMOS repeats a name, each further instance gets
   ` [0xADDR]`. When creating, the block suffix ` [1]`/`[2]`/`[3]` is added.
5. If the file is missing or the header does not fit (`software-id`,
   `image-size`), nothing happens; exceptions are caught and logged
   (`[labels] ...`).

`software-id` is checked against the 12-character part-number text
(`ExtractPartnumber` → `038906018P`), **not** against
`ExtractSoftwareNumber` – which returns `SG1623` for this image.

## Verification

```
python3 tools/generate_map_labels.py
python3 tools/verify_map_labels.py --markdown-out Docs/MAP_LABELS_VERIFY.md
```

Result (model, all three blocks):

- 62/62 map tables, 69/69 curves, 3637/3856 single values per block
  (65 8-bit curves are skipped; 733 of the single values are 8 bit and are
  created. Of the 3856 values per block, ~219 lie in an object detected by the
  heuristic and are not created twice.)
- Runtime (MSA15, real binary): 150 map tables renamed, 195 curves
  renamed, 10915 single values created, 48 already named, 653 skipped
  (address lies in a detected object), **0 rejected**
- Model check: 0 rejected, all five `-FIX` corrections take effect
- Symbol count MSA15: 730 before, 11645 after; EDC15V: 574 before, 11980 after

## Build

The reference assemblies lie locally in `refs/` (copies of
`/mnt/nas/Software/Auto/ECU Remapping/EDC Suite v1.0.0/`, read read-only,
SHA-256 identical). `VAGSuite.csproj` references them via `HintPath`.

Windows:

1. Visual Studio 2010 (or MSBuild 4.0) and .NET Framework 3.5 installed.
2. Leave `refs/` in the repo; the paths are relative (`refs\...`).
3. Office PIAs (`Microsoft.Office.Interop.Excel`, `Microsoft.Office.Tools.*`)
   are **not** in the NAS folder and not in `refs/` either – they come from an
   Office 2007 PIA installation. Without them `ExcelInterface.cs` breaks.
4. `msbuild VAGSuite.sln /t:Rebuild /p:Configuration=Release`

Linux (Wine + csc, verified on 2026-11-12):

1. Repo copy to `/tmp/edcsuite-build`, `ExcelInterface.cs` replaced by a
   stub (Office PIA missing; only in the build copy, not in the repo).
2. `wine ~/.wine/drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe
   /nologo /target:winexe /out:EDCSuite.exe @build.rsp`
   with the `refs/` assemblies and
   `/resource:Labels/EDC15V-5_SG1623.labels,VAGSuite.Labels.EDC15V-5_SG1623.labels`.
3. Result: **0 errors, 164 warnings**, `EDCSuite.exe` 1396736 bytes
   (of which ~465 KiB embedded label file). Without labels it was 913408 bytes.

In addition, a console harness (`Harness.cs`, only in `/tmp`) is built against
the same sources; it calls `parseFile()` headless and writes the symbol table
as TSV. This is how the runtime numbers above are measured, not estimated.

## Risks

- **Wrong software version:** The header binds to part number and file size.
  In addition, every match checks dimensions, length and (for map tables) the
  monotonicity of both axes; for curves the axis end values. Violations are
  counted and logged, the symbol stays unnamed.
- **Expected value (MSA15):** `[labels] applied 150 3D, 195 1D, 0 scalar renames,
  10915 scalars added (48 already named, 653 skipped, 0 rejected)`.
  EDC15V: `162 3D, 207 1D, 11370 scalars added (24 already named, 198 skipped,
  0 rejected)`. If the number deviates, the addresses or the version do not fit.
- **Same part number, shifted addresses:** could pass all checks by coincidence.
  Cross-check at `0x04F28`, `0x07404`, `0x03E52` in the hex viewer.
- **Write path:** For 8-bit objects the editor path (saving) is not tested;
  the display is (byte-wise). Before writing back into an ECU image, check on
  a copy.

## Open points

- The 12 map tables that `NameKnownMaps` already names correctly keep their
  heuristic factors; a factor audit against DAMOS is outstanding.
- For 8-bit curves (58 tables, 6 identifiers) the axis is only named, not
  wired: the suite's axis reader reads 16-bit words.
- `EDC15VFileParser` does not detect `0x048AC` (`0xFC` is missing in its
  `isAxisID`); the active MSA15 path detects it. The label is present.
- XDF export and comparison view for 8-bit objects are not part of this
  change.
