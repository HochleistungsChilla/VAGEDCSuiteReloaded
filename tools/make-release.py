#!/usr/bin/env python3
"""Assemble a release archive for the current build.

Default archive (safe to publish):
    EDCSuite_merged.exe, README.md, CHANGELOG.md, Docs/

With --with-dlls the third-party assemblies from refs/ are added as well.
Those are commercial (DevExpress, Nevron) and must not be published; the
option exists for a private, self-hosted download only.

Usage:
    tools/make-release.py                 build dist/<name>.zip
    tools/make-release.py --with-dlls     also pack refs/*.dll
    tools/make-release.py --skip-build    pack the existing executable
"""

import os
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "dist")
EXE = os.path.join(ROOT, "EDCSuite_merged.exe")
REFS = os.path.join(ROOT, "refs")
NAME = "VAGEDCSuiteReloaded"


def version():
    text = open(os.path.join(ROOT, "Properties", "AssemblyInfo.cs"), encoding="utf-8").read()
    match = re.search(r'AssemblyInformationalVersion\("([^"]+)"\)', text)
    if match:
        return match.group(1)
    match = re.search(r'AssemblyVersion\("(\d+\.\d+\.\d+)', text)
    return match.group(1) if match else "0.0.0"


def build():
    script = os.path.join(ROOT, "tools", "build-wine.sh")
    print("building ...")
    result = subprocess.call(["bash", script], cwd=ROOT)
    if result != 0:
        raise SystemExit("build failed (exit %d)" % result)


def add_file(archive, path, arcname):
    archive.write(path, arcname)


def main(argv):
    with_dlls = "--with-dlls" in argv
    skip_build = "--skip-build" in argv

    if not skip_build:
        build()

    if not os.path.exists(EXE):
        raise SystemExit("%s not found - build first" % EXE)

    tag = version()
    base = "%s-%s-win32%s" % (NAME, tag, "-with-dlls" if with_dlls else "")
    target = os.path.join(DIST, base + ".zip")
    os.makedirs(DIST, exist_ok=True)
    if os.path.exists(target):
        os.remove(target)

    count = 0
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        add_file(archive, EXE, base + "/EDCSuite_merged.exe")
        count += 1

        for name in ("README.md", "CHANGELOG.md"):
            path = os.path.join(ROOT, name)
            if os.path.exists(path):
                add_file(archive, path, base + "/" + name)
                count += 1

        docs = os.path.join(ROOT, "Docs")
        if os.path.isdir(docs):
            for name in sorted(os.listdir(docs)):
                if name.lower().endswith(".md"):
                    add_file(archive, os.path.join(docs, name), base + "/Docs/" + name)
                    count += 1

        satellite = os.path.join(ROOT, "build", "nl", "EDCSuite_merged.resources.dll")
        if os.path.exists(satellite):
            add_file(archive, satellite, base + "/build/nl/EDCSuite_merged.resources.dll")
            count += 1

        if with_dlls:
            if not os.path.isdir(REFS):
                raise SystemExit("refs/ not found - cannot pack the third party assemblies")
            for name in sorted(os.listdir(REFS)):
                if name.lower().endswith(".dll"):
                    add_file(archive, os.path.join(REFS, name), base + "/refs/" + name)
                    count += 1

    size = os.path.getsize(target)
    print("%d files, %.1f MB -> %s" % (count, size / (1024.0 * 1024.0), target))
    if with_dlls:
        print()
        print("WARNING: this archive contains DevExpress and Nevron assemblies.")
        print("         Do not publish it. The licence does not cover redistribution.")
    else:
        print()
        print("The archive runs only when the assemblies from refs/ (DevExpress,")
        print("Nevron, HexBox, CsvHelper) are copied next to the executable.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
