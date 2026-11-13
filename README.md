# VAGEDCSuiteReloaded

A continued fork of [VAGEDCSuite](https://github.com/Blackfrosch/VAGEDCSuite) — the open
VAG/EDC15 ECU tuning suite by Blackfrosch, whose last upstream commit dates from 2013.

This fork keeps the original WinForms application working and extends it for **256 KB
EDC15V-5 images**: block-aware checksum correction, a target-block dialog so a map write
only touches the block you selected, map label import, bin merge/split, and a reproducible
Linux/Wine build that embeds the WinForms resources correctly.

> Writing maps into an ECU image changes engine management behaviour. Work on copies, keep
> the original, and never flash a file you have not verified. Everything here is used at
> your own risk.

## What this fork adds

| Area | Change |
|---|---|
| 256 KB images | EDC15V-5 layout detection (block 1/2/3 at `0x08000`, `0x10000`, `0x40000`), `EDC15VM256Checksum` |
| Checksums | Block-wise correction for the P and GA variants; `Fix` is idempotent |
| Map writes | Target-block dialog (`Actions → General → „Zielblock: Quelle"`), writes sealed per block, code region and reserved range refused |
| Map labels | `.labels` import/export (`MapLabelFile`); label data is not shipped, see below |
| Bin handling | Merge/split of partial dumps (`ChipMergeSplit`, `frmBinSplit`) |
| Build | `tools/build-wine.sh`: source list from the project file, `.resx` → `.resources` via Wine's resgen, so the GUI starts under Wine |
| Tests | `TargetBlockTest` 60/60 including 24/24 P fields and 3/3 GA fields |

Detailed notes live in [`Docs/`](Docs/):

- [`Docs/OPEN_EDC15V_256KB.md`](Docs/OPEN_EDC15V_256KB.md) — image layout and detection
- [`Docs/CHECKSUM_256KB.md`](Docs/CHECKSUM_256KB.md) — checksum fields and variants
- [`Docs/TARGET_BLOCKS_256KB.md`](Docs/TARGET_BLOCKS_256KB.md) — target block selection, tests, build
- [`Docs/MAP_LABELS.md`](Docs/MAP_LABELS.md) — label file format
- [`Docs/MAP_LABELS_VERIFY.md`](Docs/MAP_LABELS_VERIFY.md) — label verification

## Requirements

- .NET Framework 3.5 (the project targets it; Wine's .NET 4.0 `csc` builds it fine)
- Visual Studio / MSBuild for the normal Windows build, or Wine for the Linux build
- The third-party assemblies in `refs/` — **not part of this repository**:
  DevExpress v11.2 (XtraBars, XtraCharts, XtraEditors, XtraGrid, XtraLayout, XtraNavBar,
  Data, Utils, Printing, Charts, BonusSkins), Nevron (Chart, Chart.WinForm, Presentation,
  System, GraphicsGL), `Be.Windows.Forms.HexBox`, `CsvHelper` and the original
  `EDCSuiteBaseLibrary` / `EDCSuiteChecksums` / `EDCSuiteSettingsManager`.
  Copy them into `refs/` from an installed VAGEDC15Suite; the upstream `README.txt`
  describes the same procedure.

## Build

Windows / MSBuild:

```
msbuild VAGSuite.csproj
```

Linux / Wine:

```
tools/build-wine.sh            # EDCSuite_merged.exe + build/nl satellite
tools/build-wine.sh --no-sat   # application only
```

The script takes the source list and the references from `VAGSuite.csproj`
(`tools/mkrsp.py`), converts every `.resx` with Wine's `resgen` and passes it to `csc` as
`/resource:<file>,<manifest-name>` — `csc` alone never converts resources, which is what
made the GUI fail at startup with `MissingManifestResourceException`. `MONO_PATH` must point
at `refs/`, otherwise `resgen` cannot resolve the typed DevExpress/Nevron entries.

The Office-dependent `ExcelInterface.cs` is replaced for that build by
`tools/wine/ExcelInterface.stub.cs`; the repository keeps the real implementation.

## Tests

```
csc /nologo /out:build\\TargetBlockTest.exe /r:System.dll /r:System.Core.dll \
    Edc15V256Layout.cs TargetBlockWriter.cs EDC15VM256Checksum.cs tools/TargetBlockTest.cs

wine build/TargetBlockTest.exe <directory-with-reference-bins>
```

`TargetBlockTest` works on copies in a temp directory and never touches the reference files.
Result on the current reference set: **60/60**, including 24/24 fields on the eight clean P
references, 0/3 on `agroff_raw` as expected (and 3/3 after `Fix`), and 3/3 on the GA variant.

## Versioning

The fork uses `MAJOR.MINOR.PATCH` (Semantic Versioning). The number lives in
`Properties/AssemblyInfo.cs` (`AssemblyVersion`, `AssemblyFileVersion`,
`AssemblyInformationalVersion`) and is what the splash screen and the About box
show. Releases are annotated git tags `vX.Y.Z` on the release commit; the
history is listed in `CHANGELOG.md`.

```bash
tools/bump-version.py 2.1.0     # set the version, prepare the changelog heading
tools/bump-version.py --check   # compare AssemblyInfo with the tag on HEAD
tools/bump-version.py --show    # version plus `git describe`
```

`--check` exits non-zero when a tagged commit and the assembly version disagree,
so it can gate a release script.

## Map label data

`MapLabelFile` reads and writes the `.labels` text format. The 038 906 018 P label set
(`EDC15V-5_SG1623.labels`, ~460 KB) is **not** part of this repository: it is generated
from a DAMOS export, which is vendor data and must not be redistributed. Generate your
own copy locally:

```
python3 tools/generate_map_labels.py --damos <dir> --notes <dir> --bin <image> \
    --out Labels/EDC15V-5_SG1623.labels
python3 tools/verify_map_labels.py --labels Labels/EDC15V-5_SG1623.labels
```

The generator documents the CSV inputs it expects in its header; `*.labels` is ignored
by git.

## Known limitations

- Excel import/export is disabled in the Wine build (`PlatformNotSupportedException`).
- The Dutch resources are a separate satellite assembly; copy
  `build/nl/EDCSuite_merged.resources.dll` next to the executable, otherwise the UI stays German.
- `tools/mkrsp.py` matches source file names case-insensitively, because the project was
  written on Windows where `frmMain.designer.cs` and `frmMain.Designer.cs` are the same file.

## Upstream and credits

Original work and most of the code: Blackfrosch and the VAGEDCSuite contributors.
Upstream states the suite is freeware; the repository itself carries no licence file, so
this fork makes no licence claim beyond that.

Repositories:

- Upstream: <https://github.com/Blackfrosch/VAGEDCSuite>
- This fork: <https://github.com/HochleistungsChilla/VAGEDCSuiteReloaded>
