#!/usr/bin/env python3
"""Derive exact manifest resource names for every .resx and emit a csc arg file.

Convention (MSBuild): <RootNamespace>.<full type name minus root namespace>.resources
Culture variants (X.nl.resx) insert the culture before .resources.
The type is taken from the DependentUpon .cs file in VAGSuite.csproj, then its
`namespace` + `class <file base>` declaration. Ambiguities are reported, never guessed.
"""
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSPROJ = os.path.join(ROOT, "VAGSuite.csproj")

tree = ET.parse(CSPROJ)
ns = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}
root_ns_el = tree.find(".//m:RootNamespace", ns)
ROOT_NS = (root_ns_el.text or "").strip() if root_ns_el is not None else ""
if not ROOT_NS:
    sys.exit("RootNamespace not found in csproj")

# resx -> DependentUpon
dep = {}
for item in tree.findall(".//m:EmbeddedResource", ns):
    inc = item.get("Include")
    if not inc:
        continue
    d = item.find("m:DependentUpon", ns)
    dep[os.path.basename(inc)] = (d.text or "").strip() if d is not None else ""

def decl_of(cs_path):
    with open(cs_path, encoding="utf-8-sig", errors="replace") as fh:
        src = fh.read()
    ns_m = re.search(r"^\s*namespace\s+([\w.]+)", src, re.M)
    if not ns_m:
        return None, None
    return ns_m.group(1), src

def class_name_in(src, wanted):
    # any class/struct declaration whose name equals the file base (case-insensitive)
    names = re.findall(r"\b(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)", src)
    for n in names:
        if n.lower() == wanted.lower():
            return n
    return names[0] if len(names) == 1 else None

entries = []
notes = []
problems = []
resx_files = sorted(f for f in os.listdir(ROOT) if f.lower().endswith(".resx"))
for resx in resx_files:
    stem = resx[:-5]                       # strip .resx
    parts = stem.split(".")
    culture = None
    if len(parts) > 1 and re.fullmatch(r"[a-z]{2,3}(-[A-Za-z]{2,4})?", parts[-1]):
        culture = parts[-1]
        stem = ".".join(parts[:-1])
    cs = dep.get(resx, "")
    if not cs:
        cs = stem + ".cs"
        if not os.path.isfile(os.path.join(ROOT, cs)):
            cands = [f for f in os.listdir(ROOT) if f.lower() == cs.lower()]
            cs = cands[0] if cands else cs
    cs_path = os.path.join(ROOT, cs)
    if not os.path.isfile(cs_path):
        problems.append(f"{resx}: source {cs!r} not found")
        continue
    namespace, src = decl_of(cs_path)
    if not namespace:
        problems.append(f"{resx}: no namespace in {cs}")
        continue
    cls = class_name_in(src, stem)
    if not cls:
        problems.append(f"{resx}: no matching class in {cs}")
        continue
    # MSBuild names an EmbeddedResource by path under RootNamespace, not by the
    # class namespace (they coincide for every winforms item here).
    name = f"{ROOT_NS}.{stem}"
    if culture:
        name += f".{culture}"
    entries.append((resx, name + ".resources"))
    full = f"{namespace}.{cls}.resources"
    if full != name + ".resources":
        notes.append(f"{resx}: type full name is {full}")

for resx, name in entries:
    print(f"{resx:34s} -> {name}")
for n in notes:
    print("  note: " + n, file=sys.stderr)
if problems:
    print("\nPROBLEMS:", file=sys.stderr)
    for p in problems:
        print("  " + p, file=sys.stderr)
print(f"\n{len(entries)} resource(s), {len(problems)} problem(s)", file=sys.stderr)
sys.exit(1 if problems else 0)
