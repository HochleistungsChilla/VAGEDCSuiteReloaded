#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Compare the .DAM importer against the existing DAMOS CSV export.

    python3 tools/damos_verify.py FILE.DAM FILE.csv [--bin IMAGE.bin]
    python3 tools/damos_verify.py FILE.DAM FILE.csv --bin IMAGE.bin \
            --max-examples 20 --json /tmp/report.json

Object by object (matched on ``IdName``/``/SPZ`` name) it compares the
``Fieldvalues.*`` and ``AxisX.*``/``AxisY.*`` columns of the **Block 2** rows
with the values the importer derives from the .DAM:

    start address, type, columns, rows, data organisation width,
    factor, offset, unit  (plus the axis metadata and bSigned)

Without ``--bin`` the columns/rows come from the DAMOS declaration
(``/FKX``/``/FKY``); with ``--bin`` the stored axis header in the image is
used, which is what the export actually contains.

Exit code 0 when nothing mismatches, 1 otherwise.  ``--strict`` also fails on
coverage gaps (objects present on one side only).
"""

import argparse
import csv
import json
import math
import os
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import damos_import  # noqa: E402

BLOCK_OFFSETS = (('Block 3', 0x8000), ('Block 1', 0x38000))


def de_float(text):
    if text is None:
        return 0.0
    text = str(text).strip()
    if not text:
        return 0.0
    return float(text.replace(',', '.'))


def parse_addr(text):
    text = str(text or '').strip()
    if not text:
        return None
    if text.startswith('$'):
        return int(text[1:], 16)
    if text.lower().startswith('0x'):
        return int(text, 16)
    return int(text, 10)


def close(a, b):
    return math.isclose(a, b, rel_tol=1e-12, abs_tol=1e-12)


class Report(object):
    def __init__(self, max_examples):
        self.max_examples = max_examples
        self.rows = []          # (field, checked, mismatches)
        self.examples = {}      # field -> list of text
        self.notes = []

    def check(self, field, expected, actual, text):
        if field not in self.examples:
            self.examples[field] = []
            self.rows.append([field, 0, 0])
        entry = self.examples[field]
        row = next(r for r in self.rows if r[0] == field)
        row[1] += 1
        if not self._equal(expected, actual):
            row[2] += 1
            if len(entry) < self.max_examples:
                entry.append(text)

    @staticmethod
    def _equal(expected, actual):
        if isinstance(expected, float) or isinstance(actual, float):
            try:
                return close(float(expected), float(actual))
            except (TypeError, ValueError):
                return False
        return expected == actual

    def mismatches(self):
        return sum(row[2] for row in self.rows)


def read_csv(path):
    with open(path, 'r', encoding='iso-8859-1', newline='') as fh:
        return list(csv.DictReader(fh, delimiter=';'))


def block_rows(rows, block):
    suffix = '\\' + block
    return [row for row in rows if str(row.get('FolderName', '')).strip().endswith(suffix)]


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('dam')
    ap.add_argument('csv')
    ap.add_argument('--bin', metavar='IMAGE',
                    help='read the stored geometry/axis addresses from the image')
    ap.add_argument('--max-examples', type=int, default=20)
    ap.add_argument('--json', metavar='PATH', help='write the report as JSON')
    ap.add_argument('--strict', action='store_true',
                    help='also fail on objects present on only one side')
    args = ap.parse_args(argv)

    dam = damos_import.parse_dam(args.dam)
    img = None
    if args.bin:
        img = damos_import.load_image(args.bin)
        for obj in dam['objects']:
            damos_import.apply_image(img, obj, dam['warnings'])

    rows = read_csv(args.csv)
    rows2 = block_rows(rows, 'Block 2')
    rows3 = block_rows(rows, 'Block 3')
    rows1 = block_rows(rows, 'Block 1')

    report = Report(args.max_examples)
    csv_by_id = {row['IdName'].strip(): row for row in rows2}
    dam_names = {obj['name'] for obj in dam['objects']}

    missing_row = [obj for obj in dam['objects'] if obj['name'] not in csv_by_id]
    extra_row = [row for row in rows2 if row['IdName'].strip() not in dam_names]

    # ---- block relation: Block 3 = Block 2 + 0x8000, Block 1 = + 0x38000
    block_bad = 0
    block_examples = []
    by_id3 = {row['IdName'].strip(): row for row in rows3}
    by_id1 = {row['IdName'].strip(): row for row in rows1}
    for row in rows2:
        name = row['IdName'].strip()
        base = parse_addr(row['Fieldvalues.StartAddr'])
        for other, delta in ((by_id3.get(name), 0x8000), (by_id1.get(name), 0x38000)):
            if other is None:
                block_bad += 1
                if len(block_examples) < args.max_examples:
                    block_examples.append('%s: row missing' % name)
                continue
            got = parse_addr(other['Fieldvalues.StartAddr'])
            if got != base + delta:
                block_bad += 1
                if len(block_examples) < args.max_examples:
                    block_examples.append('%s: %s vs %s + 0x%X'
                                          % (name, other['Fieldvalues.StartAddr'],
                                             row['Fieldvalues.StartAddr'], delta))

    # ---- field comparison
    address_exact = 0
    address_axis_block = 0
    address_bad = 0
    for obj in dam['objects']:
        row = csv_by_id.get(obj['name'])
        if row is None:
            continue
        label = '#%d %s' % (obj['index'], obj['name'])
        csv_start = parse_addr(row['Fieldvalues.StartAddr'])
        expected = obj['value_address']
        report.check('start address', expected, csv_start,
                     '%s: DAM 0x%05X, CSV 0x%05X' % (label, expected or 0, csv_start or 0))
        if obj['start_address'] == csv_start:
            address_exact += 1
        elif obj['start_address'] is not None and csv_start is not None:
            delta = csv_start - obj['start_address']
            want = 4 + obj['columns'] * 2 if obj['type_code'] == 2 else \
                8 + (obj['rows'] + obj['columns']) * 2
            if obj['type_code'] in (2, 3) and delta == want:
                address_axis_block += 1
            else:
                address_bad += 1
        else:
            address_bad += 1

        report.check('type', obj['export_type'], row['Type'].strip(), label)
        report.check('columns', obj['columns'], int(row['Columns'] or 0), label)
        report.check('rows', obj['rows'], int(row['Rows'] or 0), label)
        csv_width = 1 if row['DataOrg'].strip() == 'eByte' else 2
        report.check('data org width', obj['width'], csv_width,
                     '%s: DAM %s (%d B), CSV %s' % (label, obj['data_org'],
                                                    obj['width'], row['DataOrg']))
        if obj['factor'] is not None:
            report.check('factor', obj['factor'], de_float(row['Fieldvalues.Factor']),
                         '%s: DAM %r, CSV %s' % (label, obj['factor'],
                                                 row['Fieldvalues.Factor']))
        if obj['offset'] is not None:
            report.check('offset', obj['offset'], de_float(row['Fieldvalues.Offset']),
                         '%s: DAM %r, CSV %s' % (label, obj['offset'],
                                                 row['Fieldvalues.Offset']))
        report.check('unit', (obj['unit'] or ''), (row['Fieldvalues.Unit'] or '').strip(),
                     '%s: DAM %r, CSV %r' % (label, obj['unit'], row['Fieldvalues.Unit']))
        report.check('signed', obj['signed'], row['bSigned'].strip() == '1',
                     '%s: DAM %s, CSV bSigned=%s'
                     % (label, obj['signed'], row['bSigned']))

        if obj['type_code'] == 3:
            # DamOS and the Suite are transposed: CSV AxisX is the DAMOS Y
            # axis and CSV AxisY the DAMOS X axis.
            _compare_axis(report, obj, 'axis_y', 'AxisX', row, img, label)
            _compare_axis(report, obj, 'axis_x', 'AxisY', row, img, label)
        else:
            _compare_axis(report, obj, 'axis_x', 'AxisX', row, img, label)

    # ---- output
    lines = []
    lines.append('DAMOS importer verification')
    lines.append('  DAM   : %s' % os.path.basename(args.dam))
    lines.append('  CSV   : %s' % os.path.basename(args.csv))
    lines.append('  image : %s' % (os.path.basename(args.bin) if args.bin else '(none)'))
    lines.append('  mode  : %s' % ('stored geometry from image' if img is not None
                                   else 'DAMOS declaration (/FKX,/FKY)'))
    lines.append('')
    lines.append('coverage')
    lines.append('  DAM objects ................... %5d' % len(dam['objects']))
    lines.append('  CSV rows (all blocks) ......... %5d  (Block 2 %d, Block 3 %d, Block 1 %d)'
                 % (len(rows), len(rows2), len(rows3), len(rows1)))
    lines.append('  matched object pairs .......... %5d' % (len(rows2) - len(extra_row)))
    lines.append('  DAM objects without CSV row ... %5d' % len(missing_row))
    lines.append('  CSV rows without DAM object ... %5d' % len(extra_row))
    lines.append('  block offset 0x8000/0x38000 ... %s'
                 % ('ok' if block_bad == 0 else '%d deviations' % block_bad))
    lines.append('')
    lines.append('field                            checked  mismatch')
    for field, checked, bad in report.rows:
        lines.append('  %-30s %7d %9d' % (field, checked, bad))
    lines.append('')
    lines.append('address relation (second DAM address -> CSV StartAddr)')
    lines.append('  equal (value starts there) .... %5d' % address_exact)
    lines.append('  + inline axis block ........... %5d' % address_axis_block)
    lines.append('  unexplained ................... %5d' % address_bad)

    bad_total = report.mismatches()
    if missing_row:
        lines.append('')
        lines.append('objects without a CSV row')
        for obj in missing_row[:args.max_examples]:
            lines.append('  #%d %s (type %d)' % (obj['index'], obj['name'],
                                                 obj['type_code']))
        if len(missing_row) > args.max_examples:
            lines.append('  ... %d more' % (len(missing_row) - args.max_examples))
    if extra_row:
        lines.append('')
        lines.append('CSV rows without a DAM object')
        for row in extra_row[:args.max_examples]:
            lines.append('  %s' % row['IdName'])
        if len(extra_row) > args.max_examples:
            lines.append('  ... %d more' % (len(extra_row) - args.max_examples))

    for field, checked, bad in report.rows:
        if not bad:
            continue
        lines.append('')
        lines.append('%s: %d of %d differ (showing up to %d)'
                     % (field, bad, checked, args.max_examples))
        for text in report.examples[field]:
            lines.append('  ' + text)

    if block_bad:
        lines.append('')
        lines.append('block offset deviations (showing up to %d)' % args.max_examples)
        for text in block_examples:
            lines.append('  ' + text)

    lines.append('')
    verdict = 'PASS' if bad_total == 0 else 'FAIL'
    lines.append('%s: %d mismatches in %d checked fields (of %d objects)'
                 % (verdict, bad_total, sum(r[1] for r in report.rows), len(dam['objects'])))
    print('\n'.join(lines))

    failed = bad_total > 0 or block_bad > 0
    if args.strict and (missing_row or extra_row):
        failed = True

    if args.json:
        payload = {
            'dam': os.path.abspath(args.dam),
            'csv': os.path.abspath(args.csv),
            'image': os.path.abspath(args.bin) if args.bin else None,
            'objects': len(dam['objects']),
            'csv_rows': len(rows),
            'matched': len(rows2) - len(extra_row),
            'missing_in_csv': [obj['name'] for obj in missing_row],
            'missing_in_dam': [row['IdName'] for row in extra_row],
            'fields': [{'field': f, 'checked': c, 'mismatches': b}
                       for f, c, b in report.rows],
            'mismatch_total': bad_total,
            'address': {'exact': address_exact, 'inline_axis': address_axis_block,
                        'unexplained': address_bad},
            'block_offsets_ok': block_bad == 0,
            'examples': report.examples,
            'pass': not failed,
        }
        with open(args.json, 'w', encoding='utf-8', newline='\n') as fh:
            json.dump(payload, fh, indent=1, ensure_ascii=False)
            fh.write('\n')
    return 1 if failed else 0


def _compare_axis(report, obj, attr, prefix, row, img, label):
    axis = obj.get(attr)
    csv_addr = parse_addr(row.get(prefix + '.DataAddr'))
    if axis is None and csv_addr in (None, 0):
        return
    if axis is None:
        report.check(prefix + ' presence', 'none', 'present',
                     '%s: DAM has no axis, CSV has %s' % (label, row.get(prefix + '.DataAddr')))
        return
    if csv_addr in (None, 0):
        report.check(prefix + ' presence', 'present', 'none',
                     '%s: DAM has an axis, CSV DataAddr=%s' % (label, row.get(prefix + '.DataAddr')))
        return
    report.check(prefix + ' unit', axis['unit'] or '', (row.get(prefix + '.Unit') or '').strip(),
                 '%s: DAM %r, CSV %r' % (label, axis['unit'], row.get(prefix + '.Unit')))
    if axis['factor'] is not None:
        report.check(prefix + ' factor', axis['factor'],
                     de_float(row.get(prefix + '.Factor')),
                     '%s: DAM %r, CSV %s' % (label, axis['factor'], row.get(prefix + '.Factor')))
    if axis['offset'] is not None:
        report.check(prefix + ' offset', axis['offset'],
                     de_float(row.get(prefix + '.Offset')),
                     '%s: DAM %r, CSV %s' % (label, axis['offset'], row.get(prefix + '.Offset')))
    if axis.get('address'):
        report.check(prefix + ' address', axis['address'], csv_addr,
                     '%s: DAM 0x%05X, CSV 0x%05X' % (label, axis['address'], csv_addr))
    csv_width = 1 if (row.get(prefix + '.DataOrg') or '').strip() == 'eByte' else 2
    report.check(prefix + ' width', 1 if abs(axis['digits']) != 2 else 2, csv_width,
                 '%s: DAM %s, CSV %s' % (label, axis['digits'], row.get(prefix + '.DataOrg')))


if __name__ == '__main__':
    sys.exit(main())
