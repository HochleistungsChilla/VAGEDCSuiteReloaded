#!/usr/bin/env python3
"""Set, check or show the assembly version of the fork.

The version lives in Properties/AssemblyInfo.cs (the three attributes
AssemblyVersion, AssemblyFileVersion and AssemblyInformationalVersion) and
in git tags.  This script keeps both in step and creates the matching
CHANGELOG.md heading.

Usage:
    tools/bump-version.py 2.0.0     set version, update the changelog heading
    tools/bump-version.py --check    compare AssemblyInfo with the tag on HEAD
    tools/bump-version.py --show     print the current version

Exit codes: 0 on success or match, 1 on a mismatch or an invalid version,
2 on a missing file.
"""

import datetime
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSEMBLY_INFO = os.path.join(ROOT, "Properties", "AssemblyInfo.cs")
CHANGELOG = os.path.join(ROOT, "CHANGELOG.md")

SEMVER = re.compile(r"^\d+\.\d+\.\d+$")


def read_text(path):
    with open(path, "r", encoding="utf-8", newline="") as handle:
        return handle.read()


def write_text(path, text):
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(text)


def current_version():
    """Return (major, minor, patch) of AssemblyVersion, or None."""
    text = read_text(ASSEMBLY_INFO)
    match = re.search(r'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)(?:\.\d+)?"\)', text)
    if not match:
        return None
    return tuple(int(part) for part in match.groups())


def set_version(version):
    text = read_text(ASSEMBLY_INFO)
    four = version + ".0"

    # Anchor on the attribute line: AssemblyInfo.cs also carries a commented
    # example ("// [assembly: AssemblyVersion("1.0.*")]") that must stay.
    text, count_a = re.subn(r'^\[assembly: AssemblyVersion\("[^"]*"\)\]',
                            '[assembly: AssemblyVersion("%s")]' % four,
                            text, flags=re.MULTILINE)
    text, count_b = re.subn(r'^\[assembly: AssemblyFileVersion\("[^"]*"\)\]',
                            '[assembly: AssemblyFileVersion("%s")]' % four,
                            text, flags=re.MULTILINE)

    if count_a != 1 or count_b != 1:
        raise SystemExit("bump-version: AssemblyVersion or AssemblyFileVersion "
                         "missing in %s" % ASSEMBLY_INFO)

    if re.search(r'^\[assembly: AssemblyInformationalVersion\("[^"]*"\)\]',
                 text, flags=re.MULTILINE):
        text = re.sub(r'^\[assembly: AssemblyInformationalVersion\("[^"]*"\)\]',
                      '[assembly: AssemblyInformationalVersion("%s")]' % version,
                      text, flags=re.MULTILINE)
    else:
        text = text.rstrip("\r\n") + \
            '\n[assembly: AssemblyInformationalVersion("%s")]\n' % version

    write_text(ASSEMBLY_INFO, text)
    return four


def update_changelog(version):
    """Insert a heading for the version if the changelog has none yet."""
    if not os.path.exists(CHANGELOG):
        write_text(CHANGELOG,
                   "# Changelog\n\nAll notable changes to this fork. This project "
                   "follows [Semantic Versioning](https://semver.org/).\n\n"
                   "## [%s] - %s\n\n### Added\n\n- \n\n### Changed\n\n- \n\n### Fixed\n\n- \n"
                   % (version, datetime.date.today().isoformat()))
        return "created"

    text = read_text(CHANGELOG)
    heading = "## [%s]" % version
    if heading in text:
        return "present"

    insertion = ("## [%s] - %s\n\n### Added\n\n- \n\n### Changed\n\n- \n\n### Fixed\n\n- \n\n"
                 % (version, datetime.date.today().isoformat()))
    match = re.search(r"^## \[", text, re.MULTILINE)
    if match:
        text = text[:match.start()] + insertion + text[match.start():]
    else:
        text = text.rstrip("\r\n") + "\n\n" + insertion
    write_text(CHANGELOG, text)
    return "inserted"


def git(*arguments):
    try:
        return subprocess.check_output(["git"] + list(arguments), cwd=ROOT,
                                       stderr=subprocess.DEVNULL,
                                       universal_newlines=True).strip()
    except (OSError, subprocess.CalledProcessError):
        return ""


def describe():
    return git("describe", "--tags", "--always", "--dirty")


def check():
    version = current_version()
    if version is None:
        print("bump-version: no AssemblyVersion found")
        return 1
    text = "%d.%d.%d" % version
    tag = git("describe", "--tags", "--exact-match")
    print("AssemblyInfo : %s" % text)
    print("HEAD         : %s" % (tag or "(no tag)"))
    if not tag:
        print("result       : HEAD is not tagged - fine for development builds")
        return 0
    if tag.lstrip("v") != text:
        print("result       : MISMATCH - tag is %s, assembly is %s" % (tag, text))
        return 1
    print("result       : match")
    return 0


def main(argv):
    if not os.path.exists(ASSEMBLY_INFO):
        print("bump-version: %s not found" % ASSEMBLY_INFO)
        return 2

    if len(argv) == 1 and argv[0] == "--check":
        return check()

    if len(argv) == 1 and argv[0] == "--show":
        version = current_version()
        if version is None:
            print("bump-version: no AssemblyVersion found")
            return 1
        print("%d.%d.%d  (%s)" % (version[0], version[1], version[2], describe() or "no git"))
        return 0

    if len(argv) != 1 or not SEMVER.match(argv[0]):
        print(__doc__.strip().splitlines()[5])
        print("       see --help for the three modes")
        return 1

    version = argv[0]
    four = set_version(version)
    state = update_changelog(version)
    print("AssemblyInfo : %s (AssemblyVersion %s)" % (version, four))
    print("CHANGELOG    : %s" % state)
    print()
    print("next steps:")
    print("  1. add the changelog entries")
    print("  2. git add Properties/AssemblyInfo.cs CHANGELOG.md")
    print('  3. git commit -m "Release %s"' % version)
    print("  4. git tag -a v%s -m \"VAGEDCSuite Reloaded %s\"" % (version, version))
    print("  5. git push origin master --follow-tags")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
