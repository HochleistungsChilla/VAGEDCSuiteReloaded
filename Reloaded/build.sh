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
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

CSC='C:\windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
export WINEDEBUG=-all

command -v wine >/dev/null || { echo "wine is not installed" >&2; exit 1; }

wine "$CSC" /nologo /target:winexe /out:Reloaded.exe \
    /r:System.dll \
    /r:System.Core.dll \
    /r:System.Drawing.dll \
    /r:System.Windows.Forms.dll \
    /r:System.Design.dll \
    /reference:lib/Krypton.Toolkit.dll \
    /reference:lib/Krypton.Ribbon.dll \
    /reference:lib/OxyPlot.dll \
    /reference:lib/OxyPlot.WindowsForms.dll \
    ../Edc15V256Layout.cs \
    ../EDC15VM256Checksum.cs \
    Main.cs \
    Theming.cs \
    HexViewerControl.cs

# Copy the private assemblies next to the .exe (the .NET Framework CLR, and
# Mono's loader, probe the application base directory — the CopyLocal step an
# MSBuild build would perform via <Private>true</Private>).
cp lib/*.dll .

echo "built: Reloaded.exe ($(stat -c %s Reloaded.exe) B)"
