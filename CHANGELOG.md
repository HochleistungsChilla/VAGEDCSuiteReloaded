# Changelog

All notable changes to this fork. This project follows
[Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH`.

The version number lives in `Properties/AssemblyInfo.cs` and is shown in the
splash screen and the About box. Change it with `tools/bump-version.py`, which
also keeps this file in step. A release is a commit plus an annotated git tag
`vX.Y.Z`.

## [2.0.0] - 2026-11-13

First public release of the Reloaded fork. Upstream stopped at 1.3.5 (2013).

### Added

- 256 KB EDC15V-5 support: `Edc15V256Layout` describes the three data blocks
  (0x38000, 0x00000, 0x08000), their reserved ranges and checksum fields;
  `EDC15VM256Checksum` verifies and reseals all three checksums.
- Target-block writes: a map change can be replicated to the same relative
  offset in block 1, 2 and 3, with all affected checksums resealed
  (`TargetBlockWriter`). Writes into the code region (0x10000-0x37FFF),
  the reserved block headers or the checksum fields are refused.
- Binary merge and split forms (`frmBinmerger`, `frmBinSplit`, `ChipMergeSplit`)
  for 128 KB chip pairs.
- Map label import and export (`MapLabelFile`) with the generator and verifier
  in `tools/`.
- Tests: `tools/TargetBlockTest.cs` (60 checks), `WriteGuardTest`,
  `ChecksumVariantTest` and `ChecksumVariantFixTest`.
- Reproducible build on Linux under Wine (`tools/build-wine.sh`,
  `tools/mkrsp.py`, `tools/mkresources.py`), including a stub for the
  Office-dependent Excel exporter.
- English documentation in `Docs/`: checksums, map labels, target blocks and
  the 256 KB file format.
- `tools/bump-version.py` and this changelog.

### Changed

- All source comments and documentation are in English.
- `README.md` rewritten: what the fork adds, requirements, build, tests,
  map label data and known limitations.
- `.gitignore` covers the build outputs, the third-party assemblies in `refs/`
  and the DAMOS-derived `.labels` files.
- `VAGSuite.csproj` is the single source of truth for the source list;
  `tools/mkrsp.py` generates the `csc` response file from it.

### Fixed

- Duplicate `EDC15VM256Checksum.cs` entry and three missing `Compile` items in
  `VAGSuite.csproj`.
- `MissingManifestResourceException` under Wine: the `.resx` files are now
  converted with Wine's own `resgen` and embedded with `csc /resource`.

[2.0.0]: https://github.com/HochleistungsChilla/VAGEDCSuiteReloaded/releases/tag/v2.0.0
