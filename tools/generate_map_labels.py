#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Generate the EDCsuite label file for 038906018P (EDC15V-5.29, SG 1623)
from the DAMOS export of the 0498 / SG 1629 stand.

Input : <notes>/abgleich_block2.csv    (62 3-D maps, Block-2 addresses)
        <damos>/*.csv                  (authoritative type, width, dims,
                                        factor/offset and axis metadata)
        <notes>/damos_entries.json     (cross-check of the map dims/offsets)
        <bin>                          (raw axis endpoints)
Output: Labels/EDC15V-5_SG1623.labels

Format v2.1
-----------
    # type;addr;name;unit;factor;offset;len;rows;axis_min;axis_max;
    #   axis_unit;axis_factor;axis_offset;axis_name;source;bytes;axis_addr

    type=3d  len = suite X_axis_length (= DAMOS rows), rows = DAMOS cols
    type=1d  len = number of points, rows = 1
    type=0d  len = 1, rows = 1

`addr` is the absolute file offset of the Block-2 instance.  The other two
code blocks hold the same object at addr+0x8000 (block 3, 4x4) and
addr+0x38000 (block 1, automatic); the loader derives those candidates.

`bytes` is the storage width of one value from the DAMOS data organisation:
eByte -> 1, eLoHi/eHiLo -> 2, taken from `DataOrg` (the width of the object
itself).  798 of the 4052 objects are 8-bit: 65 curves and 733 single values;
the other 3123 single values are 16-bit and sit on a two-byte grid.
`axis_addr` is only emitted for 16-bit axes, because the Suite reads axis
values as words; 8-bit axes keep their name and unit but no address.

Every object is emitted: nothing is dropped.  The overlap check below proves
that the objects are disjoint once the width is honoured.

Orientation
-----------
3-D maps are transposed between DAMOS and the Suite (DAMOS 16x12 shows as
Suite 12x16), hence len = DAMOS rows and rows = DAMOS cols.  1-D curves are
not transposed: the Suite stores them as X_axis_length = points,
Y_axis_length = 1.  A DAMOS curve may share its breakpoints with other curves
(58 of the 134 do), so the axis address always comes from the DAMOS and is
never inferred from the data address.

Factor/offset
-------------
`factor`/`offset` are Suite display semantics: displayed = raw*factor+offset.
"""

import argparse
import csv
import json
import os
import re
import sys

# The five maps the current MSA15 heuristics name wrongly.  The values are
# the authoritative DAMOS names; the generator asserts that the CSV agrees.
NAME_OVERRIDES = {
    0x01608: "EXT friction torque map Md = f(N, T_water)",
    0x02370: "Factor Permitted Deviation correction characteristic map",
    0x04294: "Smoke Limiter by MAP, M_fuel = f(N, P_aircorr)",
    0x0730C: "Start Of Injection Limiter",
    0x0768C: "N108 Advance Valve",
}

NAME_FIXES = set(NAME_OVERRIDES.keys())

BLOCK_TAG = re.compile(r"\s*\[Block \d\]")


def parse_decimal(text):
    """DAMOS writes German decimals (0,1).  Return float."""
    if text is None:
        return 0.0
    text = str(text).strip()
    return float(text.replace(",", ".")) if text else 0.0


def parse_addr(text):
    text = str(text).strip()
    if not text or text in ("$0", "-", "0"):
        return 0
    return int(text[1:], 16) if text.startswith("$") else int(text, 0)


def clean_name(text):
    text = " ".join(str(text).replace(";", ",").split())
    return BLOCK_TAG.sub("", text).strip()


def clean_unit(text):
    return " ".join(str(text).split()).replace(";", ",")


def width_bytes(row):
    """Storage width of the DATA values: 1 for eByte, 2 for eLoHi/eHiLo.

    `DataOrg` is the width of the object itself.  `AxisX.DataOrg` is the axis
    width and DAMOS fills it with "eByte" even for objects that have no axis
    at all, so it must never be used for the value width.
    """
    org = (row.get("DataOrg") or "").strip()
    return 1 if org == "eByte" else 2


def axis_width_bytes(row):
    org = (row.get("AxisX.DataOrg") or "").strip()
    return 1 if org == "eByte" else 2


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.dirname(here)
    base = os.path.dirname(repo)

    ap = argparse.ArgumentParser()
    ap.add_argument("--csv", default=os.path.join(base, "ecu-a4b5", "notes", "abgleich_block2.csv"))
    ap.add_argument("--json", default=os.path.join(base, "ecu-a4b5", "notes", "damos_entries.json"))
    ap.add_argument("--damos", default=os.path.join(
        base, "ecu-a4b5", "damos", "038906018P_038906018P_0498_1,9l R4 EDC SG  1629.csv"))
    ap.add_argument("--bin", default=os.path.join(base, "ecu-a4b5", "bins",
                                                  "A4B5_038906018P_SG1623_frone2023_merged.bin"))
    ap.add_argument("--out", default=os.path.join(repo, "Labels", "EDC15V-5_SG1623.labels"))
    ap.add_argument("--software-id", default="038906018P")
    ap.add_argument("--software-stand", default="SG1623")
    ap.add_argument("--ecu-type", default="EDC15V-5.29")
    ap.add_argument("--image-size", default="0x40000")
    ap.add_argument("--block-base", default="0x00C00")
    ap.add_argument("--source", default="DAMOS-038906018P-0498-SG1629")
    ap.add_argument("--no-curves", action="store_true")
    ap.add_argument("--no-scalars", action="store_true")
    args = ap.parse_args()

    img = int(args.image_size, 0)
    buf = open(args.bin, "rb").read()
    if len(buf) != img:
        raise SystemExit("image is 0x%X, expected 0x%X" % (len(buf), img))

    with open(args.json, "r", encoding="utf-8") as f:
        damos_json = {int(e["addr"]): e for e in json.load(f)}

    out = {}

    # ------------------------------------------------------------- 3-D maps
    with open(args.csv, "r", encoding="utf-8-sig") as f:
        rows2d = list(csv.DictReader(f, delimiter=";"))
    for r in rows2d:
        addr = int(r["addr"], 16)
        if addr in out:
            raise SystemExit("duplicate address 0x%05X" % addr)
        if addr not in damos_json:
            raise SystemExit("address 0x%05X missing in damos_entries.json" % addr)
        damos_name = clean_name(r["damos_name"])
        name = clean_name(NAME_OVERRIDES.get(addr, damos_name))
        if addr in NAME_OVERRIDES and clean_name(NAME_OVERRIDES[addr]) != damos_name:
            raise SystemExit("name override disagrees with DAMOS at 0x%05X" % addr)
        cols = int(r["rows"])    # suite X = DAMOS rows
        rows_n = int(r["cols"])  # suite Y = DAMOS cols
        if (cols, rows_n) != (int(damos_json[addr]["rows"]), int(damos_json[addr]["cols"])):
            raise SystemExit("dimension mismatch at 0x%05X" % addr)
        out[addr] = dict(
            typ="3d", addr=addr, name=name, unit=clean_unit(r["unit"]),
            factor=parse_decimal(r["factor"]),
            offset=parse_decimal(damos_json[addr].get("offset", "0")),
            cols=cols, rows=rows_n, bytes=2,
            axis_min="", axis_max="", axis_addr=0, axis_name="", axis_unit="",
            axis_factor=1.0, axis_offset=0.0,
            source=args.source + ("-FIX" if addr in NAME_FIXES else ""))

    # ---------------------------------------- 1-D curves and single values
    with open(args.damos, "r", encoding="latin-1") as f:
        damos_rows = [r for r in csv.DictReader(f, delimiter=";")
                      if str(r["FolderName"]).strip().endswith("Block 2")]

    for r in damos_rows:
        typ = r["Type"]
        if typ not in ("eEindim", "eEinzel"):
            continue
        addr = parse_addr(r["Fieldvalues.StartAddr"])
        if addr <= 0 or addr in out:
            raise SystemExit("bad or duplicate DAMOS address 0x%05X" % addr)
        name = clean_name(r["Name"])
        unit = clean_unit(r["Fieldvalues.Unit"])
        factor = parse_decimal(r["Fieldvalues.Factor"])
        offset = parse_decimal(r["Fieldvalues.Offset"])
        b = width_bytes(r)

        if typ == "eEindim":
            if args.no_curves:
                continue
            n = int(r["Columns"] or 1)
            if int(r["Rows"] or 1) != 1 or n < 2:
                raise SystemExit("1-D object with %sx%s at 0x%05X"
                                 % (r["Columns"], r["Rows"], addr))
            ax = parse_addr(r["AxisX.DataAddr"])
            amin = amax = ""
            ax_addr = 0
            if ax > 0 and (r["AxisX.DataOrg"] or "").strip() != "eByte":
                if ax + 2 * n > len(buf):
                    raise SystemExit("axis outside image at 0x%05X" % addr)
                amin = buf[ax] | (buf[ax + 1] << 8)
                amax = buf[ax + 2 * (n - 1)] | (buf[ax + 2 * (n - 1) + 1] << 8)
                ax_addr = ax
            out[addr] = dict(
                typ="1d", addr=addr, name=name, unit=unit, factor=factor, offset=offset,
                cols=n, rows=1, bytes=b,
                axis_min=amin, axis_max=amax, axis_addr=ax_addr,
                axis_name=clean_unit(r["AxisX.Name"]),
                axis_unit=clean_unit(r["AxisX.Unit"]),
                axis_factor=parse_decimal(r["AxisX.Factor"]),
                axis_offset=parse_decimal(r["AxisX.Offset"]),
                source=args.source)
        else:
            if args.no_scalars:
                continue
            if int(r["Columns"] or 1) != 1 or int(r["Rows"] or 1) != 1:
                raise SystemExit("single value with %sx%s at 0x%05X"
                                 % (r["Columns"], r["Rows"], addr))
            out[addr] = dict(
                typ="0d", addr=addr, name=name, unit=unit, factor=factor, offset=offset,
                cols=1, rows=1, bytes=b,
                axis_min="", axis_max="", axis_addr=0, axis_name="", axis_unit="",
                axis_factor=1.0, axis_offset=0.0,
                source=args.source)

    # ---------------------------------------------------------- unique names
    used = {}
    for addr in sorted(out):
        item = out[addr]
        if used.get(item["name"]):
            item["name"] = "%s [0x%05X]" % (item["name"], addr)
        used[item["name"]] = used.get(item["name"], 0) + 1

    # --------------------------------------------------------- bounds/overlap
    end = 0
    for addr in sorted(out):
        item = out[addr]
        length = item["cols"] * item["rows"] * item["bytes"]
        if addr < end:
            raise SystemExit("overlap at 0x%05X (%s)" % (addr, item["name"]))
        if addr + length > img:
            raise SystemExit("0x%05X+%d exceeds image" % (addr, length))
        end = addr + length

    # ------------------------------------------------------------------ write
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        f.write("# EDCsuite map labels v2.1\n")
        f.write("# software-id=%s\n" % args.software_id)
        f.write("# software-stand=%s\n" % args.software_stand)
        f.write("# ecu-type=%s\n" % args.ecu_type)
        f.write("# image-size=%s\n" % args.image_size)
        f.write("# block-base=%s\n" % args.block_base)
        f.write("# type;addr;name;unit;factor;offset;len;rows;axis_min;axis_max;"
                "axis_unit;axis_factor;axis_offset;axis_name;source;bytes;axis_addr\n")
        for addr in sorted(out):
            i = out[addr]
            f.write("%s;0x%05X;%s;%s;%s;%s;%d;%d;%s;%s;%s;%s;%s;%s;%s;%d;%s\n" % (
                i["typ"], addr, i["name"], i["unit"], repr(i["factor"]), repr(i["offset"]),
                i["cols"], i["rows"], i["axis_min"], i["axis_max"],
                i["axis_unit"], repr(i["axis_factor"]), repr(i["axis_offset"]), i["axis_name"],
                i["source"], i["bytes"],
                ("0x%05X" % i["axis_addr"]) if i["axis_addr"] else ""))

    print("wrote %s" % args.out)
    for t, label in (("3d", "3d maps  "), ("1d", "1d curves"), ("0d", "0d values")):
        n = sum(1 for i in out.values() if i["typ"] == t)
        extra = ""
        if t == "1d":
            extra = "  (%d of them 8-bit)" % sum(1 for i in out.values()
                                                 if i["typ"] == "1d" and i["bytes"] == 1)
        if t == "0d":
            extra = "  (all 8-bit)" if all(i["bytes"] == 1 for i in out.values()
                                            if i["typ"] == "0d") else ""
        print("  %s: %d%s" % (label, n, extra))
    print("  total objects: %d  (x3 code blocks = %d)" % (len(out), 3 * len(out)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
