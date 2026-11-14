# Map list (Kennfelder) — parser wiring

Status: 2026-11-14.  The EDC15V map-list parser (`EDC15VFileParser`) is now
compiled into the Reloaded build and exposed through `Reloaded/MapList.cs`.
The API is UI-free: no Krypton, OxyPlot, DevExpress or WinForms control type
appears in its signature or implementation (only `System.Windows.Forms` is
still referenced by the BCL-level `Tools.ShowMessage` fallback).

## API

```csharp
namespace VAGSuite
{
    public static class MapList
    {
        public sealed class Map
        {
            public string Varname, Description, Category, Subcategory;
            public string XAxis, YAxis, XUnits, YUnits;
            public int Address, Length, XLen, YLen, CodeBlock;
        }

        public static List<Map> GetMaps(string filename); // wraps parseFile
        public static int Count(string filename);
    }
}
```

`GetMaps` creates one `EDC15VFileParser`, calls
`parseFile(filename, out codeBlocks, out axisHelpers)` and projects every
`SymbolHelper` to a `Map`.  Order is the parser's own order (sorted by
`Flash_start_address`).  `Address` is `Flash_start_address` when set (> 0),
otherwise `Internal_address`.

## (a) Files added to the build

`Reloaded/build.sh` (array `CORE_SOURCES`) and `Reloaded/Reloaded.csproj`
(`<Compile Include="..\...">` with `<Link>`) now contain, besides the
existing `../Edc15V256Layout.cs` and `../EDC15VM256Checksum.cs`:

```
../Core/Enums.cs                 ../IEDCFileParser.cs
../SortableCollectionBase.cs     ../Tools.cs
../MapSelector.cs                ../EDC15PFileParser.cs
../CodeBlock.cs                  ../EDC15P6FileParser.cs
../AxisHelper.cs                 ../EDC15CFileParser.cs
../SymbolHelper.cs               ../EDC15MFileParser.cs
../SymbolCollection.cs           ../EDC15VFileParser.cs
../SymbolTranslator.cs           ../EDC16FileParser.cs
../SymbolAxesTranslator.cs       ../EDC17FileParser.cs
../ChecksumResultDetails.cs      ../MSA15FileParser.cs
../TransactionEntry.cs           ../MSA6FileParser.cs
../TransactionCollection.cs      ../EDC15P_checksum.cs
../TransactionLog.cs             ../EDC15VM_checksum.cs
../ProjectLog.cs                 ../MSA15_checksum.cs
../partNumberConverter.cs
../MapLabelFile.cs
```

New local file: `Reloaded/MapList.cs`.

BCL reference added: `System.Data` (needed by `PartnumberCollection`'s
`DataTable`; `System.Xml` was **not** needed).  In `build.sh` the label data
set is embedded exactly like `VAGSuite.csproj` does:

```
/resource:../Labels/EDC15V-5_SG1623.labels,EDC15V-5_SG1623.labels
```

### Decoupling change required by the compiler

`EDC15VFileParser` (and the other parsers in the closure) used the nested
enum `MapViewerEx.AxisIdent`; `MapViewerEx` is a DevExpress/Nevron
`XtraUserControl` and cannot be compiled UI-free.  The three-value enum was
moved to `Core/Enums.cs` as a top-level `VAGSuite.AxisIdent`
(X_Axis = 0, Y_Axis = 1, Z_Axis = 2) and the parser references were updated
(`EDC15VFileParser.cs`, `EDC15PFileParser.cs`, `EDC15P6FileParser.cs`,
`EDC16FileParser.cs`, `MSA15FileParser.cs`, `MSA6FileParser.cs`).
`MapViewerEx.AxisIdent` itself is unchanged, and `frmMain.cs` keeps using it.
No behavior change: same enum values, same call sites.

### Why the other parsers and checksums are in the list

`Tools.cs` is a monolith: `Tools.GetParserForFile` instantiates every parser
(EDC15P/P6/C/M/V, EDC16, EDC17, MSA15/6) and `Tools.UpdateChecksum`
references `EDC15P_checksum`, `EDC15VM_checksum`, `MSA15_checksum`,
`Edc15V256Layout` and `EDC15VM256Checksum`.  C# compiles all members of
`Tools`, so the compiler demands those files even though `MapList` only calls
`Tools.Instance.findSequence`, `readdatafromfileasint`,
`ExtractBoschPartnumber`, `IsEDC16Partnumber` and the
`m_codeBlock5ID/6ID/7ID` / `m_currentfilelength` fields.  All of them are
UI-free (no DevExpress/WinForms control types).  A later cleanup could split
`Tools.cs` into a pure byte-helper part and a parser/checksum part; that would
shrink the Reloaded closure but was out of scope here.

`CellHelper.cs`, `CellHelperCollection.cs`, `Lines.cs` and
`PartnumberCollection.cs` are **not** part of the closure (verified: build
compiles without them).

## (b) Smoke test

Throwaway harness in `/tmp/mlt` (not committed), compiled with the same Wine
csc, `/target:exe`, same references and the same source list as `build.sh`,
run under plain `wine` with no `DISPLAY`:

```
wine maplist-final.exe 'Z:\tmp\ga-work\ga.stage1'
```

Output (labels embedded):

```
[labels] ignoring 8 bit curves (not representable)
[labels] 198 object(s) skipped (address inside a detected symbol)
[labels] applied 162 3D, 207 1D, 0 scalar renames, 11370 scalars added (24 already named, 198 skipped, 0 rejected, 3987 label rows)
MAP COUNT: 11980
min addr ok; max addr=0x03FAEC max end=0x03FAEE
negative addr=0 zero-length=0 end>256K=0
  category: Detected maps = 11766
  category: Unknown maps = 214
EEPROM Initialization DSV [flashbank 0] | addr=0x00118A len=2 x=1 y=1 units=/ cb=0
EEPROM Initialization Balance value STA [flashbank 0] | addr=0x00118C len=2 x=1 y=1 units=/ cb=0
EEPROM Initialization Balance value BEG much. [flashbank 0] | addr=0x00118E len=2 x=1 y=1 units=/ cb=0
EEPROM Initialization Balance value BEG add. [flashbank 0] | addr=0x001190 len=2 x=1 y=1 units=/ cb=0
EEPROM Initialization Balance value LLR [flashbank 0] | addr=0x001192 len=2 x=1 y=1 units=/ cb=0
EEPROM Initialization Balance value ARF [flashbank 0] | addr=0x001194 len=2 x=1 y=1 units=/ cb=0
EEPROM Initialization Balance value VGK [flashbank 0] | addr=0x001196 len=2 x=1 y=1 units=/ cb=0
EEPROM Initialization Balance value SBR [flashbank 0] | addr=0x001198 len=2 x=1 y=1 units=/ cb=0
EEPROM Initialization filtered consumption [flashbank 0] | addr=0x00119A len=2 x=1 y=1 units=/ cb=0
EEPROM Initialization Fold-Test [flashbank 0] | addr=0x00119C len=2 x=1 y=1 units=/ cb=0
```

Sanity: 11 980 entries, all inside the 256 KiB image
(`max addr 0x03FAEC`, `max end 0x03FAEE` < `0x40000`), no negative
addresses, no zero-length entries.  Without the embedded label resource the
same call returns **574** heuristic maps with generic names
(`3D 00001312 E08A DCCA`, ...), all also inside the image.

Final app build:

```
cd Reloaded && bash build.sh
Compilation succeeded - 55 warning(s)
built: Reloaded.exe (973824 B)
```

The 55 warnings are the usual unused-local/unreachable-code warnings from the
legacy parser sources; there are no errors.

## (c) Caveats

- `MapList` returns **every** `SymbolHelper`, including the ~11 370 scalar
  entries the label file adds.  A UI that wants only tables/curves should
  filter on `XLen`/`YLen` (or `Length`), e.g. `XLen > 1 || YLen > 1`.
- Addresses are file addresses as the parser reports them.  For this image
  they start at 0x118A (block base 0x00C00 + data area), not at flash space.
- Labels are only applied when the `.labels` resource is embedded in the
  assembly.  `build.sh` now embeds `Labels/EDC15V-5_SG1623.labels`, matching
  `VAGSuite.csproj`.  Without it the list is the 574-name heuristic set.
- `Tools.Instance` is a process-wide singleton and the parser writes a few
  fields on it (`m_currentfilelength`, `m_codeBlock5ID/6ID/7ID`); calling
  `GetMaps` concurrently from several threads is not synchronized.
- `MapList` does not catch exceptions: a corrupt/unsupported file surfaces
  the parser's exception to the caller.
- `System.Windows.Forms` is still compiled (via `Tools.ShowMessage`'s
  `MessageBox` fallback); this is a BCL reference, not a DevExpress/Krypton
  dependency, and console-only use under `wine` needs no `DISPLAY`.
