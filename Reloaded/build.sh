#!/usr/bin/env bash
# Build the Reloaded sample with Wine's in-box .NET 4.0 C# compiler.
#
# Mirrors tools/build-wine.sh (same csc.exe). There is nothing to resgen here
# (no .resx), so a single source file is compiled against the extracted
# third-party assemblies under lib/.
#
# BCL assemblies are passed as bare names and resolved by csc from the
# .NET 4.0 reference-assembly directory (same approach as tools/mkrsp.py);
# the in-box csc.rsp is empty, so they must be listed explicitly.
#
# The repo-root map-list parser (EDC15VFileParser) is compiled in as well; it
# is UI-free but needs its core collections, Tools, and the parser/checksum
# classes Tools.GetParserForFile/UpdateChecksum reference.  See MAPLIST.md for
# the closure and the smoke test.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

CSC='C:\windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
export WINEDEBUG=-all

command -v wine >/dev/null || { echo "wine is not installed" >&2; exit 1; }

# Repo-root sources required by the map-list closure, in dependency order for
# readability (csc does not care about order).
CORE_SOURCES=(
    ../Core/Enums.cs
    ../SortableCollectionBase.cs
    ../MapSelector.cs
    ../CodeBlock.cs
    ../AxisHelper.cs
    ../SymbolHelper.cs
    ../SymbolCollection.cs
    ../SymbolTranslator.cs
    ../SymbolAxesTranslator.cs
    ../ChecksumResultDetails.cs
    ../TransactionEntry.cs
    ../TransactionCollection.cs
    ../TransactionLog.cs
    ../ProjectLog.cs
    ../partNumberConverter.cs
    ../MapLabelFile.cs
    ../IEDCFileParser.cs
    ../Tools.cs
    # Parsers/checksums referenced by Tools.GetParserForFile / UpdateChecksum.
    ../EDC15PFileParser.cs
    ../EDC15P6FileParser.cs
    ../EDC15CFileParser.cs
    ../EDC15MFileParser.cs
    ../EDC15VFileParser.cs
    ../EDC16FileParser.cs
    ../EDC17FileParser.cs
    ../MSA15FileParser.cs
    ../MSA6FileParser.cs
    ../EDC15P_checksum.cs
    ../EDC15VM_checksum.cs
    ../MSA15_checksum.cs
    ../Edc15V256Layout.cs
    ../EDC15VM256Checksum.cs
    # UI-free byte merge/split of the two 128 KB chip halves (MergeSplitForm).
    ../ChipMergeSplit.cs
)

wine "$CSC" /nologo /target:winexe /out:Reloaded.exe \
    /r:System.dll \
    /r:System.Core.dll \
    /r:System.Data.dll \
    /r:System.Drawing.dll \
    /r:System.Windows.Forms.dll \
    /r:System.Design.dll \
    /reference:lib/Krypton.Toolkit.dll \
    /reference:lib/Krypton.Ribbon.dll \
    /reference:lib/OxyPlot.dll \
    /reference:lib/OxyPlot.WindowsForms.dll \
    /resource:../Labels/EDC15V-5_SG1623.labels,EDC15V-5_SG1623.labels \
    "${CORE_SOURCES[@]}" \
    MapList.cs \
    Main.cs \
    MergeSplitForm.cs \
    Theming.cs \
    HexViewerControl.cs \
    I18n.cs \
    MapViewForm.cs \
    Gdi3dSurface.cs

# Copy the private assemblies next to the .exe (the .NET Framework CLR, and
# Mono's loader, probe the application base directory — the CopyLocal step an
# MSBuild build would perform via <Private>true</Private>).
cp lib/*.dll .

echo "built: Reloaded.exe ($(stat -c %s Reloaded.exe) B)"
