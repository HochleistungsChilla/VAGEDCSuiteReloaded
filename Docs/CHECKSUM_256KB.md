# 256 KB checksum (EDC15V-5, 038 906 018 P / GA)

Checksum procedure for the three data blocks of a 256 KB EDC15V-5 file,
implemented in `EDC15VM256Checksum.cs` and wired into `Tools.cs`.
Two variants are evidenced: **P** (SG 1623, marker `27 F3`) and **GA**
(SG 3102, marker `84 F1`). Both use the same computation and the same
block layout; they differ exclusively in the marker pair.

## Status

| Item | Result |
|---|---|
| Fields against 8 clean P reference files | **24/24 correct** (`agroff_raw` 0/3 as expected) |
| GA original (`…_038906018GA_…_D60C.Original`) | **3/3 correct** |
| GA Stage1 (`…_038906018GA_…_6891.Stage1`) | **3/3 correct** |
| `Fix(agroff_raw)` vs. `agroff_checked` | **byte-identical**, 3/3 fields correct afterwards |
| `Fix(GA-Original)` without changes | **byte-identical** to the original |
| Differential test against the Python reference | 60/60 values (20 randomly mutated images, P and GA mixed) |
| End-to-end via `Tools.UpdateChecksum` | GA 3/3, mutated GA after `Fix` 3/3, P `agroff_raw` byte-identical |
| Build (Wine/csc) | successful, 2 067 968 bytes, 163 warnings, **0 errors** |

## Variants

| Variant | Vehicle/ECU | Marker at `block end − 6` | Init A | Init B | Final −A | Final ^B |
|---|---|---|---|---|---|---|
| `VariantP` | 038 906 018 P, SG 1623 | `27 F3` | `0x8631` | `0xEFCD` | `0x8631` | `0xDF9B` |
| `VariantGA` | 038 906 018 GA, SG 3102 (Bosch 0 281 010 170) | `84 F1` | `0x8631` | `0xEFCD` | `0x8631` | `0xDF9B` |

The GA file is an ordinary data sheet of the same family: Bosch
0 281 010 170, VAG 038 906 018 GA, 256 KB. Its marker `84 F1` sits at exactly
the same three block ends (`0x08000`, `0x10000`, `0x40000`), and the three
stored fields (`6BD9A517`, `38967E7F`, `74A296C9`) are exactly reproducible
with the **P constants**. The earlier assumption "GA needs different
constants" is thereby refuted: the computation is identical, only the marker
detection was too narrow (it looked exclusively for `27 F3`).

This is evidenced by 6 fields from two files (original 3/3, Stage1 3/3;
the third field is the same in both files because the tuning stage does not
touch block 3). 96 bits of agreement in the original plus 64 bits in Stage1
are strong, but **two** GA files remain – not a sample across many software
versions.

How the marker comes about is not clarified. It is a byte pair in the data
stream and is evidently carried along with the software version by the
manufacturer software; it has no influence on the computation.

## Layout

File exactly 256 KB (`0x40000`). Three blocks, each begins `0xB80` bytes after
the start of its 32 KB window; the checksum field is the last four bytes of
the window. Identical for P and GA.

| Block | Data range (words, 16 bit LE) | Field (4 bytes LE) |
|---|---|---|
| 1 | `0x00B80` … `0x07FFB` | `0x07FFC` |
| 2 | `0x08B80` … `0x0FFFB` | `0x0FFFC` |
| 3 | `0x38B80` … `0x3FFFB` | `0x3FFFC` |

The field itself is **not** part of the computation. The three ranges are
disjoint; a correction of the first field does not affect the following
blocks.

Marker plausibility: a known marker pair at `block end − 6`, searched in the
last 4 KB of the window on an `0x80` grid. That is a layout hypothesis, not
proof – an image can look plausible and still carry wrong fields.
`CheckMarkers()` checks this separately and never changes a result.

## Algorithm

Per block, over 16-bit words little endian; the constants come from the
selected variant:

```
Sa = InitA            (P/GA: 0x8631)
Sb = InitB            (P/GA: 0xEFCD)
cnt = Blockstart & 0xFFFF
if cnt == 0 or (cnt & 0x8000): cnt = (cnt + 0x8000) & 0xFFFF

Cycle over four words (w1..w4), stop as soon as the field position is reached:

  w1:  Sa ^= w1
       cnt += 2
       n = Sb & 0x0F
       if n != 0: Sa = ROL16(Sa, n); carry = Sa & 1   else carry = 0

  w2:  Sb = (Sb - w2 - carry) & 0xFFFF
       cnt += 2
       Sb ^= Sa
       --> stop after w2 if field position reached

  w3:  Sa = (Sa - w3) & 0xFFFF
       cnt += 2
       Sa = (Sa + cnt - 1) & 0xFFFF

  w4:  Sb ^= w4
       cnt += 2
       n = Sa & 0x0F
       if n != 0: Sb = ROR16(Sb, n)
       --> stop after w4 if field position reached

Final:
  Sa = (Sa - FinalSubA + cnt + 1) & 0xFFFF    (P/GA: 0x8631)
  Sb = Sb ^ FinalXorB                          (P/GA: 0xDF9B)
  Wert = (Sb << 16) | Sa
```

Peculiarities that easily lead to errors:

* **Four-word cycle, two stop points.** A block contains 14 878 words
  (`(0x7FFC − 0xB80) / 2`), i.e. 3 719 full cycles plus two words. The
  last cycle ends after `w2`. A cycle with only one stop point at the end
  produces different values.
* **`carry`** comes from bit 0 of `Sa` *after* the rotation and only enters the
  subtraction of `w2`. The value set in `w4` is not used further.
* **The counter** starts block-dependent: `0xB80` stays, `0x8B80` and `0x38B80`
  are folded to `0x0B80` by the `0x8000` bit.
* **Rotation width 0** means no rotation (not "16 bits").

## API

```csharp
// detect the variant from the markers; null + diagnosis when none matches
EDC15VM256Checksum.Variant DetectVariant(byte[] bin, out string info);

// compute values, buffer unchanged; order = block 1..3
bool TryCompute(byte[] bin, out uint[] values, out string fehler);

// same with an explicitly selected variant
bool TryCompute(byte[] bin, Variant variant, out uint[] values, out string fehler);

// recompute fields and write back little endian; unchanged on error
bool Fix(byte[] bin, out string fehler);

// marker check (every known variant, all three block ends)
bool CheckMarkers(byte[] bin, out string info);

// compare only, write nothing
VerifyResult Verify(byte[] bin);   // .AllOk, .Report
```

`TryCompute(bin, …)` detects the variant itself. If it finds no marker, it
continues computing with `DefaultVariant` (P) – that is lossless because both
known variants have the same constants; the markers remain a plausibility
question for `CheckMarkers`. An image with a shifted marker thus still
delivers correct values instead of being rejected (behavior as before the
variant extension).

`TryCompute`/`Fix` reject anything that is not exactly `0x40000` bytes in
size (`null`, 128 KB, 512 KB, 1 MB). `Fix` first computes all three values and
only then writes – an error cannot leave a half-corrected image behind.

## Integration in `Tools.cs`

Unchanged compared with the P introduction; the public API has not changed.
Two places:

1. `CalculateEDC15VMChecksum` – before the gate, it branches on
   `EDC15VM256Checksum.ImageSize` and calls
   `CalculateEDC15VM256KChecksum`. The 512 KB/1 MB path
   (V4.1/V4.1v2/2002) stays unchanged.
2. `UpdateChecksum`, `case MSA15/MSA12/MSA11` – the part number
   `038 906 018 P` is recognized by `partNumberConverter` as `EDC15V-5.29`,
   and `DetermineFileType` derives `EDC15FileType.MSA15` from it (not
   `EDC15V`). For 256 KB the new implementation is used there.

Feedback to the UI: `ChecksumType.VAG_EDC15VM_256K`,
`NumberChecksumsTotal/Ok/Fail` = 3/3/0 or 3/x/(3−x). `verifyOnly` never
writes.

## Testing

Prerequisites: `refs/` in the repo, Wine prefix with csc (4.0) and Mono
reference assemblies, reference bins under `~/Dokumente/ecu-a4b5/bins`.

```sh
# build (build copy, ExcelInterface stub, generated .resources)
csc @build5.rsp /target:winexe /out:EDCSuite.exe

# field test P + GA (sources: tools/ChecksumVariantTest.cs and
# tools/ChecksumVariantFixTest.cs, compiled against EDC15VM256Checksum.cs)
csc /out:TestVariants.exe EDC15VM256Checksum.cs tools/ChecksumVariantTest.cs
wine TestVariants.exe '<bins>' '<ga.orig>' '<ga.stage1>'
# expectation: P 24/24 (agroff_raw 0/3), GA 3/3 each

# fix test GA
csc /out:TestFix.exe EDC15VM256Checksum.cs tools/ChecksumVariantFixTest.cs
wine TestFix.exe ga.orig ga.mut.bin

# end-to-end via Tools.UpdateChecksum (reflection, no GUI)
wine integrationtest.exe ga.stage1 fix
# expectation: TypeResult = VAG_EDC15VM_256K, 3/3 ok, 0 bytes changed
```

The independent comparison runs via `ecu-a4b5/tools/checksum.py` or the
routine there with the same constants (Python oracle, 60/60 in the
differential test).

## Limits

* **Only 256 KB of this family.** Other sizes are rejected, not guessed.
  The 512 KB/1 MB path remains untouched.
* **Two GA files.** Original and Stage1 of one software version. Further
  GA software versions (other `…_xxxx.Original` suffixes) are not checked.
* **Markers are no proof.** A deliberately modified file can keep markers and
  ranges; the checksum is then nevertheless wrong. Conversely, a rebuilt image
  can shift the markers without changing the ranges – then the field
  computation stays valid, but `CheckMarkers` complains.
* **Not every 256 KB file belongs to this.** `REF_A4B5_AFN_*` (028 906 021 …,
  Bosch 0 281 001 426/554) are also 256 KB. They carry the marker `91 A4`
  at the three block ends instead of `27 F3`/`84 F1` and are **not** detected
  by `DetectVariant`. Notably, the computation with the P constants still
  hits for them (2 files, 6 fields, one of them `C3C3C3C3` fill). That is
  **not** adopted as a variant – the files belong to a different part-number
  family, and adopting it would change `CheckMarkers` there from "not
  described" to "OK" without that being verified. Open question, not a result.
* **Verified statically only.** No flash, no ECU, no network. The reference
  set is 8 clean P files (24 fields), 2 GA files (6 fields), 20
  differential-test images (60 values) and one fix test.
* **`agroff_raw` is deliberately unclean.** 0/3 before the fix is the expected
  result, not a test failure.

## Extending

For a different size or ECU family:

1. Derive block start, window size and field position from the layout
   (`BlockStarts`, `WindowSize`, `BlockSkip`, `FieldOffsets`). The algorithm
   itself has no size dependency except the word count.
2. Enter a new variant as a `Variant` object in `Variants` (marker **and**
   constants). Marker and constants belong together; only when both are
   evidenced is it a variant and not a guess.
3. If the word count is not `4k+2`, check the stop points: the cycle stops
   after `w2` and after `w4`. With `4k` it ends after `w4`, with `4k+2` after
   `w2`; other remainders require an adjustment.
4. Allow the new size in `TryCompute`/`CheckImage`, check test files against an
   independent oracle, and only then branch in `Tools.cs`.
