#!/usr/bin/env bash
# Build EDCSuite_merged.exe with Wine's csc, embedding the WinForms resources.
#
# Why this exists: csc, unlike MSBuild, never converts .resx to .resources.
# Wine ships a working Mono resgen, so the resources are converted here and
# passed to csc with /resource:<file>,<manifest-name>.
#
# The source list and the assembly references are taken from VAGSuite.csproj by
# tools/mkrsp.py; the Office-dependent Excel exporter is replaced with
# tools/wine/ExcelInterface.stub.cs, because Microsoft.Office.Interop.Excel is
# not available under Wine. ExcelInterface.cs itself stays untouched.
#
#   tools/build-wine.sh            # application + nl satellite assembly
#   tools/build-wine.sh --no-sat   # application only
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

CSC="C:\\windows\\Microsoft.NET\\Framework\\v4.0.30319\\csc.exe"
RESGEN="C:\\windows\\mono\\mono-2.0\\lib\\mono\\4.5\\resgen.exe"
OUT="EDCSuite_merged.exe"
BUILD=".resbuild"

# resgen must resolve the DevExpress/Nevron types named by typed .resx entries.
export MONO_PATH="Z:$(printf '%s' "$ROOT/refs" | tr '/' '\\')"
export WINEDEBUG=-all

command -v wine >/dev/null || { echo "wine is not installed" >&2; exit 1; }
[ -d refs ] || { echo "refs/ is missing - see README (third-party assemblies)" >&2; exit 1; }

rm -rf "$BUILD"
mkdir -p "$BUILD"

# --- 1. source list + references from the project file ----------------------
python3 tools/mkrsp.py --wine-stub --out "$BUILD/b.rsp"
python3 tools/mkresources.py > "$BUILD/names.txt"

# --- 2. .resx -> .resources ------------------------------------------------
neutral_args=()
culture_pairs=()   # "resx|manifest-name"

while read -r resx _arrow name; do
    [ -z "$resx" ] && continue
    case "$resx" in *".nl.resx") culture_pairs+=("$resx|$name"); continue;; esac
    out="$BUILD/$name"
    if ! wine "$RESGEN" "$resx" "$out" >"$BUILD/${resx}.log" 2>&1; then
        echo "resgen failed for $resx:" >&2
        cat "$BUILD/${resx}.log" >&2
        exit 1
    fi
    [ -s "$out" ] || { echo "resgen produced no $out" >&2; exit 1; }
    neutral_args+=("/resource:$out,$name")
done < "$BUILD/names.txt"

echo "$(( ${#neutral_args[@]} )) neutral .resx embedded, ${#culture_pairs[@]} culture resources"

# --- 3. main assembly ------------------------------------------------------
wine "$CSC" /nologo /target:winexe /out:"$OUT" "${neutral_args[@]}" "@$BUILD/b.rsp"
echo "built: $OUT ($(stat -c %s "$OUT") B)"

# --- 4. nl satellite assembly (ResourceManager only looks there) ------------
if [ "${1:-}" != "--no-sat" ] && [ "${#culture_pairs[@]}" -gt 0 ]; then
    satargs=()
    satcs="$BUILD/satellite.cs"
    printf '[assembly: System.Reflection.AssemblyCulture("nl")]\n' > "$satcs"
    for pair in "${culture_pairs[@]}"; do
        resx="${pair%%|*}"; name="${pair##*|}"
        out="$BUILD/$name"
        if ! wine "$RESGEN" "$resx" "$out" >"$BUILD/${resx}.log" 2>&1; then
            echo "resgen failed for $resx:" >&2; cat "$BUILD/${resx}.log" >&2; exit 1
        fi
        satargs+=("/resource:$out,$name")
    done
    mkdir -p build/nl
    wine "$CSC" /nologo /target:library /out:"build/nl/EDCSuite_merged.resources.dll" \
        "$satcs" "${satargs[@]}"
    echo "satellite: build/nl/EDCSuite_merged.resources.dll (copy next to the exe for nl)"
fi
