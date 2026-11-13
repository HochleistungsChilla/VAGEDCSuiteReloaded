#!/usr/bin/env python3
"""Build the csc response file from VAGSuite.csproj.

The project file is the single source of truth for the source list and the
assembly references; MSBuild is not available in the Wine build, so this
script reproduces exactly the two things csc needs:

  * every <Compile Include="..."/> item, in project order
  * every <Reference> that has a HintPath into refs/, plus the BCL assemblies

With --wine-stub the WinForms Excel exporter (ExcelInterface.cs) is replaced
by tools/wine/ExcelInterface.stub.cs: the real implementation needs
Microsoft.Office.Interop.Excel, which neither Wine nor Mono provide. The
repository keeps the real file untouched.

Usage:
    python3 tools/mkrsp.py                      # print to stdout
    python3 tools/mkrsp.py --out .resbuild/b.rsp
    python3 tools/mkrsp.py --wine-stub --out .resbuild/b.rsp
"""

import argparse
import os
import sys
import xml.etree.ElementTree as ET

NS = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}

# BCL assemblies the project actually uses. csc resolves these from the
# reference assembly directory, so no path is needed.
FRAMEWORK_REFS = [
    "System.dll",
    "System.Core.dll",
    "System.Xml.dll",
    "System.Xml.Linq.dll",
    "System.Data.dll",
    "System.Data.DataSetExtensions.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.Design.dll",
]

STUB = "tools/wine/ExcelInterface.stub.cs"
REPLACED = "ExcelInterface.cs"


def norm(path):
    return path.replace("\\", "/")


def resolve(path):
    """Resolve a project-relative path to the real on-disk name.

    The project was written on Windows, where `frmMain.designer.cs` and
    `frmMain.Designer.cs` are the same file. On a case-sensitive filesystem
    they are not, so every component is matched case-insensitively and the
    real spelling is returned.
    """
    cur = "."
    for part in norm(path).split("/"):
        candidate = os.path.join(cur, part)
        if os.path.exists(candidate):
            cur = candidate
            continue
        try:
            entries = os.listdir(cur)
        except OSError:
            return None
        matches = [e for e in entries if e.lower() == part.lower()]
        if len(matches) != 1:
            return None
        cur = os.path.join(cur, matches[0])
    return cur if os.path.isfile(cur) else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", default="VAGSuite.csproj")
    ap.add_argument("--out", default="-")
    ap.add_argument("--wine-stub", action="store_true",
                    help="compile the Excel stub instead of the Office implementation")
    args = ap.parse_args()

    root = ET.parse(args.project).getroot()

    sources, refs = [], []
    for item in root.findall("m:ItemGroup/m:Compile", NS):
        inc = item.get("Include")
        if inc:
            sources.append(norm(inc))

    for item in root.findall("m:ItemGroup/m:Reference", NS):
        hint = item.find("m:HintPath", NS)
        if hint is not None and hint.text:
            refs.append(norm(hint.text.strip()))

    if args.wine_stub:
        if REPLACED not in sources:
            sys.exit(f"mkrsp: {REPLACED} is not a Compile item in {args.project}")
        sources[sources.index(REPLACED)] = STUB

    problems = []
    for i, src in enumerate(sources):
        real = resolve(src)
        if real is None:
            problems.append(src)
        else:
            sources[i] = real[2:] if real.startswith("./") else real
    for i, ref in enumerate(refs):
        real = resolve(ref)
        if real is None:
            problems.append(ref)
        else:
            refs[i] = real[2:] if real.startswith("./") else real
    if problems:
        sys.exit("mkrsp: missing files:\n  " + "\n  ".join(problems))

    lines = [f'"{s}"' for s in sources]
    lines += [f"/r:{f}" for f in FRAMEWORK_REFS]
    lines += [f'/r:"{r}"' for r in refs]
    text = "\n".join(lines) + "\n"

    if args.out == "-":
        sys.stdout.write(text)
    else:
        os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
        with open(args.out, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
        print(f"mkrsp: {len(sources)} sources, {len(refs)} refs -> {args.out}")


if __name__ == "__main__":
    main()
