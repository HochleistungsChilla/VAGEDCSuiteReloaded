# Opening a 256 KB EDC15V-5: cause of the "kein EDC15" (no EDC15) message

Affects: 038906018P, SG1623 (1.9 l R4 EDC 15V-5), 256 KB (0x40000).
The file exists in six variants in the inventory (`A4B5_038906018P_*`), all
262 144 bytes.

## Cause

The message did **not** come from the file size but from a hard-wired family
check in `frmMain.DetectMaps`:

```csharp
// before (upstream)
if (!info.EcuType.StartsWith("EDC15P") &&
    !info.EcuType.StartsWith("EDC15VM") &&
    info.EcuType != string.Empty && showMessage)
{
    new frmInfoBox("No EDC15P/VM file [" + info.EcuType + "] " + Dateiname);
}
```

For 038906018P, `partNumberConverter.ConvertPartnumber` returns the type
`EDC15V-5.29`. It starts neither with `EDC15P` nor with `EDC15VM` → message,
although the file was correctly detected as EDC15V-5 and read by the MSA15
parser. The file size was only apparently to blame.

Size checks in the code (all **not** involved in opening):

| Place | Check | Effect |
| --- | --- | --- |
| `Tools.CalculateEDC15PChecksum` | `Length != 0x80000 → return` | only EDC15P checksum |
| `Tools.CalculateEDC15VMChecksum` | allows 0x80000/0x100000 | only checksum |
| `partNumberConverter` fallback | `0x40000 → EDC15V`, `0x80000 → EDC15P` | only when the part number is not in the table and begins with `028` |
| `MSA15_checksum` | address table up to 0x40000 | checksum routine (currently disabled) |
| `HexViewer` | takes the real file length | no fixed size |

`Tools.DetermineFileType` already assigns `EDC15V-5` to the MSA15 parser
(`info.EcuType.Contains("EDC15V-5")`), which is why parsing including the
label file already worked before.

## Fix

1. **Family check extended** (`frmMain.DetectMaps`): accepted are
   `EDC15*`, `MSA15*`, `MSA12*`, `MSA11*`, `MSA6*`. This makes EDC15V-5
   (and EDC15V, EDC15C, EDC15M, MSA15.5) message-free.
2. **Helpful message instead of "kein EDC15" (no EDC15)**: new method
   `Tools.DescribeUnknownFile(...)`. Header line in short format
   `Unsupported ECU type [<Typ>] <datei> (<bytes> bytes, partnumber <nr>)`;
   below it file name, size in bytes/KB/hex,
   detected Bosch number, VAG number, software version, detected type and parser,
   list of supported families and – for unusual size – a
   size hint. It appears for an unknown family **and** for an empty
   `EcuType` (previously no message came there at all).
3. **No parser / no map tables**: dedicated messages with the same details,
   instead of an empty list without explanation.
4. **Open errors no longer swallowed**: the empty `catch` in
   `OpenFile` now shows file, size and error text.
5. **Status bar**: for file types without a checksum routine it shows
   `Checksum: n/a` instead of the misleading `Checksum failed`
   (`TypeResult == ChecksumType.Unknown`).
6. **Command line**: `EDCSuite.exe <datei.bin>` opens the file via
   the same path as the "Open file" button (used for the test below).
   "letzte Datei automatisch laden" (load last file automatically) is skipped
   in the process (`frmMain.StartupFile`), otherwise the last opened file was
   additionally parsed - the `[labels] applied` block appeared twice in the log.

## Test (Wine, Xvfb, real GUI run)

`EDCSuite.exe A4B5_038906018P_SG1623_frone2023_merged.bin`
(256 KB, 038906018P, SG1623):

- exactly **one** window, no dialog
- status bar: `Checksum: n/a | OK | 038906018P SG1623 | 0281001720 Volkswagen EDC15V-5.29_1,9l R4 EDC`
- `11645 symbols`, category "Detected maps", subcategory "Labelled curves (195)"
- log: `[labels] applied 150 3D, 195 1D, ... 0 rejected, 3987 label rows`

Cross-check with 256 KB of random data: the dialog appears and shows
`262144 Bytes (256 KB, 0x40000)`, all numbers `(nicht gefunden)` (not found),
`Erkannt: (unbekannt) / EDC16FileParser` (Detected: (unknown) / EDC16FileParser).

## Build

`/tmp/edcsuite-build`, csc v4.0.30319 under Wine, references from `refs/`,
`ExcelInterface.cs` stubbed only there. Form resources generated with the
supplied `resgen` (otherwise the EXE does not start under Wine):

```
for r in *.resx; do resgen.exe "$r" "${r%.resx}.resources"; done
# per file: /resource:<name>.resources,VAGSuite.<name>.resources
```

Result: 0 errors, 163 warnings, `EDCSuite.exe` 2 044 928 bytes,
SHA-256 `f72566e6654177bc...`. A runnable copy needs all DLLs from
`refs/` next to the executable.
