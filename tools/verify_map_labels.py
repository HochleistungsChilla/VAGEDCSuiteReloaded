#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Offline verification of a generated label file.

Two detection sources are supported:

  --symbols dump.tsv   authoritative: the symbol table the real C# parser
                       produced BEFORE labels were applied (`Harness msa15-base`)
  (default)            Python model: msa15_detected.json for the 3-D maps plus
                       a structural 1-D model against the raw image

The script reproduces MapLabelStore.Apply: block candidates from the fixed
spacing of this ECU family (0, +0x8000, +0x38000), the 16 bit width rule,
dimension/length checks, the exact 1-D axis layout, the -FIX rule, name
collisions and the occupancy rule for created objects.

Usage:
  python3 tools/verify_map_labels.py --symbols /tmp/sym_msa15_base.tsv
  python3 tools/verify_map_labels.py
  python3 tools/verify_map_labels.py --markdown-out Docs/MAP_LABELS_VERIFY.md
"""

import argparse
import collections
import json
import os
import sys

BLOCK_DELTAS = (0x0000, 0x8000, 0x38000)
BLOCK_NAMES = {0x0000: "codeblock 2", 0x8000: "codeblock 3", 0x38000: "codeblock 1"}
FIELDS = ("type", "addr", "name", "unit", "factor", "offset", "len", "rows",
          "axis_min", "axis_max", "axis_unit", "axis_factor", "axis_offset",
          "axis_name", "source", "bytes", "axis_addr")

# isAxisID() strips of MSA15FileParser
AXIS_ID_STRIPS = (0xDB, 0xC0, 0xC1, 0xC2, 0xC4, 0xC5, 0xE0, 0xE4, 0xE5, 0xE9,
                  0xEA, 0xEB, 0xEC, 0xDA, 0xDC, 0xDD, 0xDE, 0xDF, 0xF9, 0xFE,
                  0xFC, 0xD7, 0xE6, 0xD5, 0xC3, 0xE7)

# Documented corrections (old heuristic name -> DAMOS name).
EXPECTED_FIXES = {
    0x01608: ("MAF correction by temperature", "EXT friction torque map Md = f(N, T_water)"),
    0x02370: ("IQ by MAP limiter", "Factor Permitted Deviation correction characteristic map"),
    0x04294: ("IQ by MAP limiter", "Smoke Limiter by MAP, M_fuel = f(N, P_aircorr)"),
    0x0730C: ("Boost limiter", "Start Of Injection Limiter"),
    0x0768C: ("IQ to airmass", "N108 Advance Valve"),
}


def parse_labels(path):
    columns = None
    labels = []
    with open(path, "r", encoding="utf-8-sig") as f:
        for line in f:
            line = line.rstrip("\n").strip()
            if not line:
                continue
            if line.startswith("#"):
                body = line.lstrip("#").strip()
                if body.startswith("type;"):
                    columns = body.split(";")
                continue
            p = line.split(";")
            if columns is None:
                raise SystemExit("label file has no '# type;...' header")
            if len(p) != len(columns):
                raise SystemExit("row has %d fields, header has %d: %s"
                                 % (len(p), len(columns), line[:70]))
            row = dict(zip(columns, p))
            lbl = {
                "type": row["type"].strip().lower(),
                "addr": int(row["addr"], 16),
                "name": row["name"],
                "unit": row["unit"],
                "factor": float(row["factor"].replace(",", ".")),
                "offset": float(row["offset"].replace(",", ".")),
                "cols": int(row["len"]),
                "rows": int(row["rows"]),
                "bytes": int(row["bytes"]) if row.get("bytes", "").strip() else 2,
                "axis_min": row.get("axis_min", "").strip(),
                "axis_max": row.get("axis_max", "").strip(),
                "axis_addr": int(row["axis_addr"], 16) if row.get("axis_addr", "").strip() else 0,
                "source": row["source"],
            }
            lbl["length"] = lbl["cols"] * lbl["rows"] * lbl["bytes"]
            if lbl["type"] == "1d" and lbl["bytes"] != 2:
                # 8 bit curves are read as 16 bit by CheckMap(); the loader
                # cannot retarget them and skips these rows.
                continue
            labels.append(lbl)
    return labels


def axis_words(buf, address, length):
    return [buf[address + 2 * i] | (buf[address + 2 * i + 1] << 8) for i in range(length)]


def axis_header_ok(buf, address, length):
    if address < 4 or address + 2 * length > len(buf):
        return False
    if (buf[address - 4] | (buf[address - 3] << 8)) == 0:
        return False
    return (buf[address - 2] | (buf[address - 1] << 8)) == length


def axis_matches(buf, address, length, lbl):
    if not axis_header_ok(buf, address, length):
        return False
    vals = axis_words(buf, address, length)
    if lbl["axis_min"] != "" and lbl["axis_max"] != "":
        if vals[0] == int(lbl["axis_min"]) and vals[-1] == int(lbl["axis_max"]):
            return True
    return all(vals[i] <= vals[i + 1] for i in range(length - 1)) or \
           all(vals[i] >= vals[i + 1] for i in range(length - 1))


def load_symbols(path):
    syms = {}
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            p = line.rstrip("\n").split("\t")
            if len(p) < 10:
                continue
            syms[int(p[0], 16)] = {
                "name": p[1], "xlen": int(p[2]), "ylen": int(p[3]),
                "length": int(p[4]), "xa": int(p[5], 16),
                "is1d": p[7] == "1", "is1scalar": p[8] == "1", "is3d": p[9] == "1",
            }
    return syms


def model_detection(buf, detected, named):
    """Python model: 3-D from msa15_detected.json, 1-D from the axis header."""
    syms = {}
    for addr, v in detected.items():
        syms[addr] = {"name": named.get(addr, ""), "xlen": v["xlen"], "ylen": v["ylen"],
                      "length": v["length"], "xa": addr - 4 - 2 * v["xlen"] - 2 * v["ylen"] + 4,
                      "is1d": False, "is1scalar": False, "is3d": True}
    for addr in list(syms):
        pass
    return syms


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.dirname(here)
    notes = os.path.join(os.path.dirname(repo), "ecu-a4b5", "notes")

    ap = argparse.ArgumentParser()
    ap.add_argument("--labels", default=os.path.join(repo, "Labels", "EDC15V-5_SG1623.labels"))
    ap.add_argument("--symbols", default="",
                    help="baseline symbol dump of the C# harness (authoritative)")
    ap.add_argument("--detected", default=os.path.join(notes, "msa15_detected.json"))
    ap.add_argument("--named", default=os.path.join(notes, "msa15_named.json"))
    ap.add_argument("--bin", default=os.path.join(os.path.dirname(repo), "ecu-a4b5", "bins",
                                                  "A4B5_038906018P_SG1623_frone2023_merged.bin"))
    ap.add_argument("--markdown-out", default="")
    ap.add_argument("--list-values", type=int, default=0)
    args = ap.parse_args()

    labels = parse_labels(args.labels)
    buf = open(args.bin, "rb").read()
    named = {int(k): v.strip() for k, v in json.load(open(args.named, encoding="utf-8")).items()}
    by_type = collections.Counter(l["type"] for l in labels)

    if args.symbols:
        syms = load_symbols(args.symbols)
        source = "C#-Symboltabelle %s (ohne Label-Datei)" % os.path.basename(args.symbols)
        # 1-D symbols the model cannot see are irrelevant with a real dump
    else:
        detected = {int(k): v for k, v in json.load(open(args.detected, encoding="utf-8")).items()}
        syms = model_detection(buf, detected, named)
        for lbl in labels:
            if lbl["type"] != "1d":
                continue
            for d in BLOCK_DELTAS:
                a = lbl["addr"] + d
                t = a - 4 - 2 * lbl["cols"]
                if t < 0 or (buf[t] | (buf[t + 1] << 8)) == 0:
                    continue
                if (buf[t + 2] | (buf[t + 3] << 8)) == lbl["cols"]:
                    syms.setdefault(a, {"name": "", "xlen": lbl["cols"], "ylen": 1,
                                        "length": 2 * lbl["cols"], "xa": t + 4,
                                        "is1d": False, "is1scalar": False, "is3d": False})
        source = "Python model (msa15_detected.json + axis structure)"

    # occupancy of the heuristic symbols before labels
    used = bytearray(len(buf))
    for addr, s in syms.items():
        lo = addr
        if s["xa"] > 0 and s["xa"] - 4 < lo:
            lo = s["xa"] - 4
        hi = max(addr + max(s["length"], 0),
                 (s["xa"] + 2 * s["xlen"]) if s["xa"] > 0 else 0)
        for i in range(max(0, lo), min(len(buf), hi)):
            used[i] = 1

    names = set(s["name"] for s in syms.values() if s["name"])

    applied, created, skipped, rejected = {}, {}, [], []

    for lbl in sorted(labels, key=lambda x: (x["type"], x["addr"])):
        for d in BLOCK_DELTAS:
            addr = lbl["addr"] + d
            if addr + lbl["length"] > len(buf):
                rejected.append((addr, "outside image", lbl))
                continue
            s = syms.get(addr)

            if lbl["type"] in ("3d", "1d"):
                if s is None:
                    rejected.append((addr, "not detected", lbl))
                    continue
                if (s["xlen"], s["ylen"], s["length"]) != (lbl["cols"], lbl["rows"], lbl["length"]):
                    rejected.append((addr, "dims %dx%d/%d != %dx%d/%d" % (
                        s["xlen"], s["ylen"], s["length"],
                        lbl["cols"], lbl["rows"], lbl["length"]), lbl))
                    continue
                if lbl["type"] == "1d":
                    expected = addr - 2 * lbl["cols"]
                    if s["xa"] != expected:
                        rejected.append((addr, "axis 0x%X != 0x%X" % (s["xa"], expected), lbl))
                        continue
                    if not axis_matches(buf, s["xa"], lbl["cols"], lbl):
                        rejected.append((addr, "axis header/values", lbl))
                        continue
                generic = not s["name"] or s["name"].startswith(("2D ", "3D ", "Generated"))
                if not generic and not lbl["source"].upper().endswith("-FIX"):
                    skipped.append((addr, "heuristic name kept", lbl))
                    continue
                applied[addr] = lbl
                continue

            # 0d: rename an existing symbol, otherwise create one
            if s is not None:
                if s["length"] != lbl["length"]:
                    skipped.append((addr, "address hosts a %d byte symbol" % s["length"], lbl))
                    continue
                generic = not s["name"] or s["name"].startswith(("2D ", "3D ", "Generated"))
                if not generic and not lbl["source"].upper().endswith("-FIX"):
                    skipped.append((addr, "heuristic name kept", lbl))
                    continue
                applied[addr] = lbl
                continue
            if any(used[addr:addr + lbl["length"]]):
                skipped.append((addr, "inside a detected object", lbl))
                continue
            name = lbl["name"] + " [codeblock %d]" % (2 if d == 0 else (3 if d == 0x8000 else 1))
            if name in names:
                name = name + " @0x%05X" % addr      # MapLabelStore disambiguates
                if name in names:
                    skipped.append((addr, "name collision", lbl))
                    continue
            names.add(name)
            for i in range(addr, addr + lbl["length"]):
                used[i] = 1
            created[addr] = lbl

    # ------------------------------------------------------------ statistics
    # "named after" = renamed, created, or a heuristic name that we keep
    after = set(applied) | set(created)
    after |= set(a for a, why, l in skipped if why == "heuristic name kept")
    per_block = {d: collections.Counter() for d in BLOCK_DELTAS}
    for lbl in labels:
        for d in BLOCK_DELTAS:
            addr = lbl["addr"] + d
            if addr in after or addr in named:
                per_block[d][lbl["type"]] += 1

    ok = True
    print("Label file : %s" % args.labels)
    print("Detection  : %s" % source)
    print("Objects    : %d rows  (%d 3d, %d 1d, %d 0d) = %d symbols x 3 blocks"
          % (len(labels), by_type["3d"], by_type["1d"], by_type["0d"], len(labels)))
    print("Applied    : %d renamed/created, %d kept heuristic name, %d skipped, %d rejected"
          % (len(applied) + len(created), len(skipped), len(skipped), len(rejected)))
    print()
    print("Block            maps       curves     values")
    for d in BLOCK_DELTAS:
        b = per_block[d]
        print("  %-12s +0x%05X  %2d/%-2d     %2d/%-3d    %4d/%-4d" % (
            BLOCK_NAMES[d], d, b["3d"], by_type["3d"], b["1d"], by_type["1d"],
            b["0d"], by_type["0d"]))
        if b["3d"] != by_type["3d"] or b["1d"] != by_type["1d"]:
            ok = False

    if rejected:
        print("\nRejected : %d" % len(rejected))
        for a, why, l in rejected[:30]:
            print("  0x%05X %-3s %-40s %s" % (a, l["type"], why, l["name"][:44]))
    else:
        print("\nRejected : 0")

    values_skipped = [x for x in skipped if x[2]["type"] == "0d"]
    if values_skipped:
        print("Values skipped (address inside a detected object): %d" % len(values_skipped))
        for a, why, l in values_skipped[:5]:
            print("  0x%05X %s" % (a, l["name"][:50]))

    print("\nCorrections (Block-2 address):")
    for addr, (old_exp, new_exp) in sorted(EXPECTED_FIXES.items()):
        hit = applied.get(addr)
        old = named.get(addr, "")
        new = hit["name"] if hit else (old or "(none)")
        good = (old.strip() == old_exp and new == new_exp)
        ok = ok and good
        print("  0x%05X  %-8s  %-46s -> %s" % (addr, "OK" if good else "FAIL", old or "(unnamed)", new))

    if args.list_values:
        print("\nFirst %d created values (Block 2):" % args.list_values)
        n = 0
        for addr in sorted(created):
            if created[addr]["type"] != "0d":
                continue
            print("  0x%05X  %-52s %s" % (addr, created[addr]["name"][:52], created[addr]["unit"]))
            n += 1
            if n >= args.list_values:
                break

    if args.markdown_out:
        with open(args.markdown_out, "w", encoding="utf-8") as f:
            f.write("# Label-Verifikation %s\n\n" % os.path.basename(args.labels))
            f.write("Erzeugt mit `tools/verify_map_labels.py` gegen `%s`.\n\n" % source)
            f.write("## Abdeckung je Codeblock\n\n")
            f.write("| Block | 3-D-Maps | 1-D-Kurven | Einzelwerte |\n|---|---|---|---|\n")
            for d in BLOCK_DELTAS:
                b = per_block[d]
                f.write("| %s (+0x%05X) | %d/%d | %d/%d | %d/%d |\n" % (
                    BLOCK_NAMES[d], d, b["3d"], by_type["3d"], b["1d"],
                    by_type["1d"], b["0d"], by_type["0d"]))
            f.write("\nUmbenannt/angelegt: %d, Heuristikname behalten: %d, "
                    "uebersprungen: %d, abgelehnt: %d.\n\n"
                    "Uebersprungen sind Adressen, die in einem von der Heuristik "
                    "erkannten Objekt liegen (Axialwerte oder Daten); dort ein "
                    "zweites Symbol anzulegen wuerde das vorhandene ueberlappen. "
                    "65 Kurven mit 8 Bit Speicherbreite sind nicht im "
                    "Nenner enthalten (Loader ueberspringt sie).\n\n"
                    % (len(applied) + len(created), len(skipped), len(skipped), len(rejected)))
            f.write("## Die %d 3-D-Maps (Block 2)\n\n" % by_type["3d"])
            f.write("| addr | dims | vorher | nachher |\n|---|---|---|---|\n")
            for lbl in sorted([l for l in labels if l["type"] == "3d"], key=lambda x: x["addr"]):
                addr = lbl["addr"]
                hit = applied.get(addr) or created.get(addr)
                before = named.get(addr, "")
                f.write("| 0x%05X | %dx%d | %s | %s |\n" % (
                    addr, lbl["cols"], lbl["rows"],
                    (before or "(unbenannt)").replace("|", "/"),
                    (hit["name"] if hit else (before or "(keiner)")).replace("|", "/")))
            f.write("\n## Die %d 1-D-Kurven (Block 2)\n\n" % by_type["1d"])
            f.write("| addr | Punkte | Achse | Name | Einheit |\n|---|---|---|---|---|\n")
            for lbl in sorted([l for l in labels if l["type"] == "1d"], key=lambda x: x["addr"]):
                hit = applied.get(lbl["addr"])
                mark = "" if hit or lbl["addr"] in named else " (offen)"
                f.write("| 0x%05X | %d | %s..%s | %s%s | %s |\n" % (
                    lbl["addr"], lbl["cols"], lbl["axis_min"], lbl["axis_max"],
                    lbl["name"].replace("|", "/"), mark, lbl["unit"] or "-"))
        print("\nmarkdown: %s" % args.markdown_out)

    return 0 if ok and not rejected else 1


if __name__ == "__main__":
    sys.exit(main())
