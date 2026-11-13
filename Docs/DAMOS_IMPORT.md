# DAMOS `.DAM` import

Status: 2026-11-13. `tools/damos_import.py` reads a Bosch DAMOS text export
and produces JSON or a v2.1 label file; `tools/damos_verify.py` checks the
result object by object against the existing CSV export of the same stand.

Reference data set: `038906018P_038906018P_0498_1,9l R4 EDC SG  1629`
(EDC15V, stand 0498 / SG 1629, MSA15.5 header).

| File | Size | Content |
|---|---|---|
| `…1629.DAM` | 741 958 B, 33 423 lines | the DAMOS export (ISO-8859-1) |
| `…1629.csv` | 3 619 892 B, 12 157 lines | semicolon export, ground truth |
| `…1629.bin` | 262 144 B | 256 KiB ECU image of the same stand |

Nothing in this doc was guessed: every statement below is either read from the
three files or counted over all 4053 records by the two scripts.

```
python3 tools/damos_import.py FILE.DAM                  # JSON on stdout
python3 tools/damos_import.py FILE.DAM --json out.json
python3 tools/damos_import.py FILE.DAM --bin IMG.bin \
        --labels out.labels --software-id 038906018P --software-stand SG1629
python3 tools/damos_import.py FILE.DAM --stats
python3 tools/damos_verify.py FILE.DAM FILE.csv --bin IMG.bin
```

No third-party packages, Python 3 only. Both scripts only read their inputs.

## 1. The file

Plain text, ISO-8859-1, one record per line, CRLF-free. There is exactly one
non-tag prose line (`Stand generiert:  7-OCT-1996 09:55:28.45`), everything
else starts with `/<TAG>`.

The file has four record families, in this order:

| Family | Lines | Shape |
|---|---|---|
| header tags | 1 … 42 | `/TAG,value` — some with `{…}` values |
| objects | 43 … 32466 | `N,/SPZ,name,{description},type` + sub-tags, terminated by `/ABL` |
| controllers | 32467 … 32706 | `N,/REG,name,{description},…` + one `/REP,…;` line |
| sources | 32707 … 33423 | `N,/SRC,name,{description},$ADDR,…;` — one line each |

Header tags and their observed values (`/MCK` appears 9 times, everything else
once):

| Tag | Value | Meaning (as used here) |
|---|---|---|
| `/EPR` | `P030.V88.VA09, A0300988.DAM:, DAMPAR_03` | export/project id, source file |
| `/EAD` | `$FFFF` | end address |
| `/EPK` | `{}` | — |
| `/PNR` | `{VAR09}` | project number |
| `/KNR` | `{VW}` | customer |
| `/PRO` | `{MSA15.5}` | processor family |
| `/YNR` | `{A030.V88}` | variant |
| `/BEA` | `{J. ZUERN}` | editor |
| `/DST` | `{MAX_KLKF},{ 7-OCT-96},{A0300988.HEX maximale Anzahl KL- /KF-Stuetzstellen}` | table/project description |
| `/TEL` | `{0711-811-5381}` | phone |
| `/STA` | (empty) | — |
| `/VAD` | `$0C32` | pointer into the object pointer table at the start of block 2 (see §4) |
| `/SPC` | `4053` | number of `/SPZ` objects |
| `/UMR` | `120` | — |
| `/UTB` | `0` | — |
| `/SRC` | `717` | number of `/SRC` records |
| `/UMV` | `0` | — |
| `/UP` | `{S80166}` | — |
| `/SGB` | `{}` | — |
| `/ABL` | `2` | — |
| `/RFG` | `2` | — |
| `/DAB` | `4` | — |
| `/PBA` | `$38000h` | base of code block 1 in the image |
| `/MCK` | 9 lines (`PROT_TYP MCMESS`, `KOMKEN 8`, `BLKZ_W 20`, …) | MCMESS channel config |
| `/SND` | 9 lines (`CHNNL1_ZS $E7E0`, `DISTAB_WS $38B80h`, `WERTE_WS 20`, …) | channel/table layout |
`/DST` and `/SND` exist only in the header; they are **not** per-object tags.
`/REP` (120 lines) belongs to the `/REG` family, see §7.

### Per-object record

```
4,/SPZ,anwADF_KL,{ADF - Linearisierung     P_ATM = f(U_ADF)},2
$0C36,$6A9E
/SPW,62,-2,  0., 3500.
/SPX,74,-2,  0., 5000.,21
/SPY,0,0,  0.,  0.,0
/FKX,2,0,0
/FKY,0,0,0
/ABL,2;
```

| Part | Meaning |
|---|---|
| `4` | object number, 1 … 4053, contiguous |
| `/SPZ` | record start; `anwADF_KL` is the DAMOS symbol name, `{…}` the German description, the last field the type code |
| `$0C36,$6A9E` | two addresses: slot in the address table, object address (see §4) |
| `/SPW,code,width,min,max` | value conversion code, signed storage width in bytes, display range |
| `/SPX`, `/SPY` | same four fields plus a fifth: the STA id (see §2.3) |
| `/FKX`, `/FKY` | point count of the X/Y axis, then two flags that are `0` in every one of the 4053 records |
| `/ABL,n;` | record terminator; `n` is 0 (3921 records) or 2 (132). Meaning unknown, nothing in the CSV correlates with it. |

Tag counts over the whole file: `/SPZ`, `/SPW`, `/SPX`, `/SPY`, `/FKX`,
`/FKY` 4053 each, `/ABL` 4054 (4053 records + the header `2`).

### Type codes

| Code | Count | Meaning | CSV `Type` |
|---|---|---|---|
| 1 | 3856 | single value, 1 × 1 | `eEinzel` |
| 2 | 69 | curve, `FKX` points | `eEindim` |
| 3 | 62 | map, `FKX` × `FKY` | `eZweidim` |
| 6 | 1 | curve over the whole STA table (`xcwFSPKSTV`) | — (not in the CSV) |
| 7 | 59 | `eByte` curve, one point per value of the axis range (`xcwFSPK*` error-class tables) | `eEindim` |
| 12 | 6 | ASCII string, one byte per character (`xcwBHardNr`, `xcwDatum`, …) | `eEindim` |

Codes 6, 7 and 12 do not appear in the CSV as separate types; the exporter
writes all three as `eEindim`.

## 2. Field semantics

### 2.1 `/SPW` — the value

```
/SPW,62,-2,  0., 3500.
      |  |   |    |
      |  |   |    +-- display maximum
      |  |   +------- display minimum
      |  +----------- signed width in bytes (-2 = 2 bytes signed)
      +-------------- conversion code
```

* **The sign of the width field is the signedness.** `digits < 0` ⇔ CSV
  `bSigned = 1`, with no exception in 4052 objects (1086 signed, 2966
  unsigned). The absolute value is the storage width: 2 → `eLoHi`, everything
  else → `eByte`. The CSV agrees on all 4052 objects.
* **The conversion code is a lookup key, not a formula.** The .DAM carries no
  unit, factor or offset. Code `62` means *factor 1, offset 0, unit `hPa`*,
  code `113` means *factor 50, offset 0, unit `hPa/s`*, code `72` means
  *factor 0.1, offset −273.14, unit `degree C`*. A copy of that table is
  embedded in `damos_import.py` (`CONVERSION_CODES`, 118 entries). Evidence
  that the code really is a table key: for all 4052 exported objects one code
  maps to exactly one (factor, offset, unit, precision) tuple — zero codes
  with two different factors, zero with two different units. The CSV even
  preserves the table's original number formatting (`0,01000000` for code 55,
  `0,01` for code 49), which is why the table keeps strings instead of floats.
* The display min/max are **not** exported to the CSV (there is no such
  column) and are not used by the importer.

### 2.2 `/FKX`, `/FKY` — declared geometry

`FKX` is the DAMOS X count, `FKY` the DAMOS Y count. For a curve `FKY = 0`.
The two remaining fields are always 0.

The Suite stores maps **transposed**, so the label/CSV meaning is:

```
rows    (suite Y, CSV "Rows")    = DAMOS X = FKX
columns (suite X, CSV "Columns") = DAMOS Y = FKY
```

For a curve the single axis is the X axis, so `columns = FKX, rows = 1`. The
importer reports `columns`/`rows` in this CSV/Suite sense and keeps the raw
declaration in `declared.x` / `declared.y`.

**FKX/FKY are maxima, not the stored geometry.** In 33 of the 131 curves and
maps they are larger than the data that is actually in the image; the export
uses the stored count. See §5.

### 2.3 `/SPX`, `/SPY` — the axes

Same field layout as `/SPW`, plus a fifth field, the STA id — an index into
the DAMOS *Stützstellenverteilung* (*breakpoint distribution*). All 59 type-7
records share `3430`; `xcwFSPKSTV` (type 6) has `717`, which is exactly the
number of `/SRC` records. The meaning of the other ids is not known (§9).
An axis block of all zeros (`/SPX,0,0, 0., 0.,0`) means "no axis".

The axis conversion code resolves through the same table: `/SPX,74,…` is
`mV`, factor 4.8875855327…, i.e. 5000/1023 — a 10-bit ADC axis.

### 2.4 Type-specific geometry

| Code | columns | rows |
|---|---|---|
| 1 | 1 | 1 |
| 2 | `FKX` | 1 |
| 3 | `FKY` | `FKX` |
| 6 | STA id (717) | 1 |
| 7 | `raw_max − raw_min + 1` (0…7 → 8) | 1 |
| 12 | string length (the width field, 5/9/11/12/25) | 1 |

Types 6 and 7 have no usable `FKX`. For type 7 the axis is the value range
itself (`0…7` → 8 points), for type 6 the point count is the STA length.
Both are marked in the JSON (`dims_source`, and a warning for type 6).

## 3. DAMOS → CSV column mapping

| CSV column | Source in the .DAM |
|---|---|
| `Name` | *not present* — the .DAM has only the short symbol name (`/SPZ` field 2) and a German description. The exporter's English name is DAMOS-internal long text. |
| `IdName` | `/SPZ` field 2 |
| `FolderName` | *not present* (DAMOS project tree) |
| `Type` | `/SPZ` type code → `eEinzel`/`eEindim`/`eZweidim` |
| `ViewMode`, `RWin`, `Radix` | not present (UI settings: `eViewText`, `eBars` except 6 `eHex` objects, radix 10) |
| `DataOrg` | sign/magnitude of `/SPW` field 2: 2 → `eLoHi`, else `eByte` |
| `bSigned` | `/SPW` field 2 < 0 |
| `bReciprocal`, `bDelta`, `bPercent`, `bOriginal`, `bOriginalValues`, `Leading`, `SkipBytes`, `LineSkipBytes` | not present; all 0 in the export |
| `Columns`, `Rows` | `FKY`/`FKX` for maps, `FKX`/1 for curves; stored axis header when `--bin` is given |
| `Comment` | not present; empty in all 12156 rows |
| `Precision` | from the conversion code table |
| `Fieldvalues.Name` | *not present* (the exporter's own text, e.g. "conversion duty cycle") |
| `Fieldvalues.Unit` | conversion code table |
| `Fieldvalues.Factor` | conversion code table (factor) |
| `Fieldvalues.Offset` | conversion code table (offset) |
| `Fieldvalues.StartAddr` | second address of the record, plus the inline axis block (§4) |
| `AxisX.*` | `/SPY` for maps, `/SPX` for curves |
| `AxisY.*` | `/SPX` for maps, absent for curves |

The transposition in the axis columns follows from the map transposition:
the memory order of a map block is *X-header, X-breakpoints, Y-header,
Y-breakpoints, values*, and the Suite calls the first array its Y axis.

## 4. Addresses, and how they relate to the CSV

Each object record carries two addresses, `$slot,$data`:

* `data` (the second one) is the start of the object's block in the image.
* `slot` is the address of the pointer that references it. For the 191 objects
  whose two addresses differ, `slot` is an entry of a 16-bit pointer table at
  the start of block 2 (`$0C00`, 768 entries, header `/VAD,$0C32` points into
  it); the word at `slot` is exactly `data` in all 191 cases. Example:
  `anwADF_KL` has `$0C36,$6A9E`, and `$0C36` contains `0x6A9E`. 706 of the 768
  entries are object starts. For the other 3862 objects the record simply
  gives the same address twice; no pointer entry exists for them. The table is
  not needed for the import; it explains the pair.

### Block frame

The image is 256 KiB and holds the same objects three times. Block 2 starts at
file offset `0x0C00` (the label header's `block-base=0x00C00`); the first
object sits at `0x118A`, after the 768-entry pointer table. Object addresses
run up to `0x7AEC`.

| CSV block | Address | Extent in the image |
|---|---|---|
| Block 2 | `addr` | `0x0C00 … 0x8BFF` |
| Block 3 | `addr + 0x8000` | `0x8C00 … 0x10BFF` |
| Block 1 | `addr + 0x38000` | `0x38000 … 0x3FFFF` (`/PBA $38000h`) |

Verified for all 4052 objects × 2 foreign blocks: `damos_verify.py` checks
`Block 3 = Block 2 + 0x8000` and `Block 1 = Block 2 + 0x38000` and reports
`ok`. There is **no constant offset between the DAMOS addresses and the CSV
`StartAddr`**:

| Relation | Objects |
|---|---|
| `CSV StartAddr == data address` | 3921 |
| `CSV StartAddr == data + 4 + columns*2` (curve) | 69 |
| `CSV StartAddr == data + 8 + (rows+columns)*2` (map) | 62 |
| unexplained | 0 |

The non-zero differences are the inline breakpoint blocks, not a code-block
offset:

```
curve:  header(id, n)       n breakpoints (n*2 bytes)   values
map:    header(id, rows)    X breakpoints (rows*2)
        header(id, cols)    Y breakpoints (cols*2)     values
```

`header` is two little-endian words: an STA id (`0xDF42`… in this file) and
the stored point count. The value array therefore starts after the
breakpoints; for single values, strings and type-7 tables there is no inline
axis and `StartAddr` equals the data address directly (3921 objects).

## 5. Validation numbers

`damos_verify.py`, Block-2 rows of the CSV, 4052 matched objects, with
`--bin`:

```
field                            checked  mismatch
  start address                     4052         0
  type                              4052         0
  columns                           4052         0
  rows                              4052         0
  data org width                    4052         0
  factor                            4052         0
  offset                            4052         0
  unit                              4052         0
  signed                            4052         0
  AxisX unit                         190         0
  AxisX factor                       190         0
  AxisX offset                       190         0
  AxisX address                      131         0
  AxisX width                        190         0
  AxisY unit                          62         0
  AxisY factor                        62         0
  AxisY offset                        62         0
  AxisY address                       62         0
  AxisY width                         62         0

PASS: 0 mismatches in 37669 checked fields (of 4053 objects)
```

Coverage: 4053 objects parsed, 4052 matched, 1 object without a CSV row
(`#3430 xcwFSPKSTV`, type 6), 0 CSV rows without an object, block offsets ok.

Without `--bin` (declared geometry) the same run reports **33 columns, 9 rows
and 33 start-address** mismatches and exits 1 — exactly the 33 objects where
`/FKX`/`/FKY` exceed the stored geometry (24 differ in columns only, 9 in
both). The stored header (and with it the CSV) is right: e.g. `anwNX1_KL`
declares `FKX=10`, the stored header says 2, and the image at the axis address
holds two increasing breakpoints (`164, 249`)
followed by map data (`3231, 3231`). `mrwBWT_KF` shrinks from 16×16 to 11×5.
The full list is printed by the script.

## 6. Labels

`--labels` writes format v2.1 (`Docs/MAP_LABELS.md`), the format
`MapLabelFile.cs` parses: `type;addr;name;unit;factor;offset;len;rows;
axis_min;axis_max;axis_unit;axis_factor;axis_offset;axis_name;source;bytes;
axis_addr`, one line per object, Block-2 address.

* `0d` — single value, `len=rows=1`.
* `3d` — `len = columns` (suite X), `rows = rows`.
* `1d` — `len = columns` (points), `rows = 1`, `axis_min`/`axis_max` are the
  raw first/last breakpoints read from the image, `axis_addr` the address of
  the breakpoint array. `MapLabelFile` verifies that header: the word before
  the array is the count, the word before that must be non-zero — both hold
  for every emitted curve.
* `source` defaults to `DAMOS-IMPORT` (`--source`), the name comes from the
  short symbol name (`--name-field description` uses the German text; the
  English `Name` column of the CSV does not exist in the .DAM).
* `--bin` is required for correct `1d`/`3d` addresses and for the axis
  endpoints; without it the addresses are derived from the declared geometry
  (wrong for the 33 objects of §5), `axis_min`/`axis_max` stay empty and the
  script says so on stderr. The breakpoint address of a curve is always
  emitted, because it follows from the block layout alone.
* 65 of the emitted curves are 8-bit (`eByte`); `MapLabelFile.Apply1D`
  rejects 8-bit curves by design ("not representable"), so those lines are
  written but will be skipped by the loader. Single values support both
  widths.

## 7. `/REG` and `/SRC`

Both families are parsed and kept in the JSON, their semantics are not fully
resolved:

```
1,/REG,ALPHA,{Drehbeschleunigung},6,0,{(U/min)/s},6,7,  0., 505921.1
/REP, 0.129536,  0.,  0., 1.,  0.,  0.;
1,/SRC,Source1,{Hilfssource 1},$E024,-2,15,2,3;
```

`/REG` looks like a controller/parameter definition (name, description, two
numbers, unit, two numbers, min, max) with a `/REP` line of six coefficients.
The first `/REP` value of `ALPHA` is `0.129536`, which is `1/7.71986166…` —
the factor of conversion code 1. `/SRC` lists 717 RAM sources
(address, then four small integers; the last two are `2,3` in 700 records and
`2,2` in 17, meaning unknown); the header `/SRC,717` and the type-6 STA id
`717` point at the same table.
Neither family appears in the CSV, so neither can be validated. They are
excluded from the JSON object list (separate arrays `sources`, `controllers`).

## 8. Known limitations

1. **The unit/factor/offset table is external.** The .DAM stores only a code.
   `CONVERSION_CODES` was recovered from the reference CSV export of the same
   stand. For this file the round trip is exact (all 118 used codes known, 0
   unknown), but an importer for a foreign .DAM needs that table from the
   DAMOS installation; unknown codes yield `null` and a warning, never a
   guess. Codes 12, 58 and 75 do not occur in this file.
2. **Types 6 and 7 are special-cased.** Type 7's point count is derived from
   the axis range and type 6's from the STA id; both rules are consistent
   with everything observable here but are not documented DAMOS behaviour.
3. **`xcwFSPKSTV` is unverified.** The single type-6 object has no CSV row.
4. **`--bin` changes the dims.** Without the image the importer reports the
   DAMOS declaration, which is a maximum for 33 objects. This is a property
   of the data, not of the parser.
5. **`/ABL` is unexplained.** 0/2 with no correlate in the CSV.
6. **STA ids are unexplained.** They behave like ids into the breakpoint
   distribution table (values 1…609, 717, 3430) but no table is present in
   the .DAM.
7. **The DAMOS display range (`/SPW` min/max) is dropped.** The CSV has no
   column for it; the values are informational and sometimes exceed what the
   factor and the storage width can represent (e.g. `max = 6.5535E8` for a
   1-byte value).
8. **`Name`, `Fieldvalues.Name`, `FolderName`, `ViewMode`, `RWin` and the
   `b*` flags are not in the .DAM.** They come from the DAMOS project/UI and
   cannot be reconstructed from the text export.
9. **`/REG`/`/REP`/`/SRC` fields are raw.** Only the obvious structure
   (index, name, description, address, unit) is decoded.
10. **No image write, no block mapping.** The importer never writes to the
    image and does not decide which block a patch should target; it reports
    Block-2 addresses, as the label format expects.

## 9. Reproducing the numbers

```
$ python3 tools/damos_import.py "…1629.DAM" --stats
objects parsed    : 4053
source records    : 717
controller records: 120
types             : eEinzel=3856, eKennfeld=62, eKennlinie=135
storage           : eByte=799, eLoHi=3254

$ python3 tools/damos_verify.py "…1629.DAM" "…1629.csv" --bin "…1629.bin"
PASS: 0 mismatches in 37669 checked fields (of 4053 objects)
```

(`eByte` 799 includes the type-6 object that is missing from the CSV; the
export itself has 798 8-bit objects, matching the note in
`tools/generate_map_labels.py`.)
