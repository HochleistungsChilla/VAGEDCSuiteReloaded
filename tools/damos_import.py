#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""DAMOS .DAM importer (Bosch MSA/EDC15 export, plain text, ISO-8859-1).

Reads a .DAM file produced by the Bosch DAMOS editor and turns it into a
Python/JSON object list, or into a v2.1 EDCsuite label file that
``MapLabelFile.cs`` can load.

    python3 tools/damos_import.py FILE.DAM                    # JSON to stdout
    python3 tools/damos_import.py FILE.DAM --json out.json
    python3 tools/damos_import.py FILE.DAM --labels out.labels \
            --bin image.bin --software-id 038906018P --software-stand SG1629
    python3 tools/damos_import.py FILE.DAM --stats            # human summary

The .DAM carries no unit/factor/offset of its own: a value record only names a
*conversion code* (the first field of ``/SPW``).  ``CONVERSION_CODES`` below is
that code table, recovered from a reference export of the same stand (all 118
codes used by this file, 4052 objects, one factor/offset/unit per code with no
exceptions).  A code outside the table is reported as unknown, never guessed.

See Docs/DAMOS_IMPORT.md for the record layout and the validation numbers.
"""

import argparse
import json
import os
import re
import struct
import sys

# --------------------------------------------------------------------------
# Conversion codes: code -> (factor, offset, unit, precision)
#
# DAMOS stores only the code in /SPW (and /SPX, /SPY for axes).  The code
# resolves the display conversion:  displayed = raw * factor + offset.
# Recovered from the reference CSV export of this stand; see the doc.
# --------------------------------------------------------------------------
CONVERSION_CODES = {
    0: ('1', '0', '', 0),
    1: ('7.7198616600790513833992094862', '0', '(U/Min)/s', 2),
    2: ('0.000009765625', '0', '(mg/Hub)/(1/Min)', 2),
    3: ('0.00000244140625', '0', '(mg/Hub)/(1/Min)', 2),
    4: ('0.0037252905552010062009789598', '0', '(mg/Hub)/((mg/Hub)*s', 2),
    5: ('0.00006103515625', '0', '(mg/Hub)/(mg/Hub)', 2),
    6: ('0.0074505821512363868550869222', '0', '1/s', 2),
    7: ('0.015625', '0', '(mg/Hub air)/(mg/Hu', 2),
    8: ('0.000152587890625', '0', '%/((mg/Hub air)*s)', 2),
    9: ('0.00009765625', '0', '%/(mg/Hub air)', 2),
    10: ('0.0847710503472222222222222222', '0', 'm/s^2', 2),
    11: ('0.0002', '0', '(mg/Hub)/Nm', 2),
    13: ('1', '0', '-', 0),
    14: ('1', '0', '-', 0),
    15: ('1', '0', '-', 0),
    16: ('0.00390625', '0', '-', 4),
    17: ('1', '0', '-', 0),
    18: ('0.1', '0', 'degree C', 1),
    19: ('0.1', '0', 'degree C', 1),
    20: ('0.0001220703125', '0', '%/(mA*s)', 2),
    21: ('0.0000390625', '0', '%/(mA)', 2),
    22: ('0.00390625', '0', '-', 4),
    23: ('0.0001', '0', '-', 2),
    24: ('0.00390625', '0', '(mg/Hub)/(km / h)', 4),
    25: ('0.0000610352', '0', '-', 2),
    26: ('0.0036900369003690036900369004', '-0.9741697416974169741697416974', '', 2),
    27: ('1', '0', 'Imp/m', 0),
    28: ('1', '0', 'Imp/m', 0),
    29: ('0.000762939453125', '0', '(mg/Hub)/((km / h)*s)', 2),
    30: ('0.00048828125', '0', '(mg/Hub)/(km / h)', 2),
    31: ('-0.0666666666666666666666666667', '13.866666666666666666666666667', '-', 2),
    32: ('0.000030517578125', '0', '-', 2),
    33: ('0.00006103515625', '0', '-', 2),
    34: ('0.0005', '0', 'Hz', 2),
    35: ('1', '0', 'Hz', 0),
    36: ('0.0101874501005959760183349652', '0', 'ul/Imp', 2),
    37: ('1', '0', 'mA', 0),
    38: ('1', '0', 'kBaud', 0),
    39: ('0.015625', '0', 'hPa/hPa', 2),
    40: ('0.000625', '0', '%/hPa', 2),
    41: ('0.000244140625', '0', '%/(hPa*s)', 2),
    42: ('0.000078125', '0', '%/hPa', 2),
    43: ('-0.0666666666666666666666666667', '14.933333333333333333333333333', '', 2),
    44: ('1', '0', 'us', 0),
    45: ('0.0000390625', '0', '(mg/Hub)/(1/Min)', 2),
    46: ('0.0000745058111040201240195792', '0', '(mg/Hub)/((1/Min)*s)', 2),
    47: ('0.000078125', '0', '(mg/Hub)/(1/Min)', 2),
    48: ('1', '0', '-', 0),
    49: ('0.01', '0', '%', 2),
    50: ('0.0000625', '0', '-', 2),
    51: ('0.0000186264506076006395857477', '0', '(mg/Hub)/((1/Min)*s)', 2),
    52: ('0.000009765625', '0', '(mg/Hub)/(1/Min)', 2),
    53: ('0.0009765625', '0', '-', 2),
    54: ('0.1', '0', 'Nm', 1),
    55: ('0.01', '0', 'mg/Hub', 2),
    56: ('0.1', '0', 'mg/Hub air', 1),
    57: ('0.1', '0', 'kg / h air', 1),
    59: ('1', '0', '1/Min', 0),
    60: ('1', '0', '-', 0),
    61: ('1', '0', '-', 0),
    62: ('1', '0', 'hPa', 0),
    63: ('0.00390625', '0', '-', 4),
    64: ('0.01', '0', '%', 2),
    65: ('0.5', '0', '%/s', 2),
    66: ('0.1', '0', '%/s', 1),
    67: ('0.01', '0', '%', 2),
    68: ('0.01', '0', 'degree KW', 2),
    69: ('0.006103515625', '0', '%/(degree KW)*s)', 2),
    70: ('0.00390625', '0', '%/(degree KW)', 4),
    71: ('750', '0', 'us', 2),
    72: ('0.1', '-273.14', 'degree C', 1),
    73: ('1', '0', '-', 0),
    74: ('4.887585532746823069403714565', '0', 'mV', 2),
    76: ('0.1527370478983382209188660802', '0', 'mV', 2),
    77: ('0.001', '0', '-', 2),
    78: ('0.25', '0', 'Revolutions', 2),
    79: ('0.000976562', '0', '-', 2),
    80: ('0.0001', '0', '-', 2),
    81: ('20.37243401759530791788856305', '0', 'mV', 2),
    82: ('203.7243401759530791788856305', '0', 'mV/s', 2),
    83: ('0.01909213098729227761485826', '0', 'mV', 2),
    84: ('1.221001221001221001221001221', '0', 'mV', 2),
    85: ('1', '0', 'mV', 0),
    86: ('0.01', '0', 'km / h', 2),
    87: ('0.0150220048899755501222493888', '0', 'l / h', 2),
    88: ('0.0000390625', '0', '(km / h)/(1/Min)', 2),
    89: ('1', '0', 's', 0),
    90: ('5', '0', 's', 2),
    91: ('10000', '90000', 'us', 2),
    92: ('20000', '0', 'us', 2),
    93: ('1000', '0', 'us', 2),
    94: ('10000', '0', 'us', 2),
    95: ('20000', '0', 'us', 2),
    96: ('0.01', '0', 's', 2),
    97: ('0.02', '0', 's', 2),
    98: ('1', '0', 'us', 0),
    99: ('1', '0', 'us', 0),
    100: ('100000', '0', 'us', 2),
    101: ('2', '0', 'us', 2),
    102: ('2000', '0', 'us', 2),
    103: ('4', '0', 'us', 2),
    104: ('50', '0', 'mA/s', 2),
    105: ('0.00244140625', '0', '(mg/Hub)/100K', 2),
    106: ('0.01953125', '0', '(mg/Hub)/100K', 2),
    107: ('0.5', '0', '(mg/Hub)/s', 2),
    108: ('5', '0', '(mg/Hub air)/s', 2),
    109: ('0.5', '0', '(mg/Hub)/s', 2),
    110: ('1', '0', '1/Min', 0),
    111: ('50', '0', '(U/Min)/s', 2),
    112: ('100', '0', '(U/Min)/s', 2),
    113: ('50', '0', 'hPa/s', 2),
    114: ('1', '0', 'degree C/s', 0),
    115: ('61.05006105006105006105006105', '0', 'mV/s', 2),
    116: ('244.37927663734115347018572825', '0', 'mV/s', 2),
    117: ('48.87585532746823069403714565', '0', 'mV/s', 2),
    118: ('0.01', '0', '(km / h)/s', 2),
    119: ('0.5', '0', '(km / h)/s', 2),
    120: ('65536', '0', '-', 2),
}

# /SPZ type codes.  Names follow the export ("eEinzel/eEindim/eZweidim") plus
# the DAMOS wording for the two special curve families.
TYPE_CODES = {
    1: 'eEinzel',      # single value, 1 x 1
    2: 'eKennlinie',   # curve, FKX points
    3: 'eKennfeld',    # map, FKX x FKY
    6: 'eKennlinie',   # curve over the full STA table (see doc, unverified)
    7: 'eKennlinie',   # curve, eByte, one point per value of the axis range
    12: 'eKennlinie',  # ASCII string, one byte per character
}

# Suite/CSV type name written into --labels output and reported in JSON.
TYPE_EXPORT_NAME = {
    1: 'eEinzel',
    2: 'eEindim',
    3: 'eZweidim',
    6: 'eEindim',
    7: 'eEindim',
    12: 'eEindim',
}

OBJECT_RE = re.compile(r'^(\d+),/SPZ,([^,]*),\{(.*)\},(\d+)\s*$')
SRC_RE = re.compile(r'^(\d+),/SRC,([^,]*),\{(.*)\},(\$[0-9A-Fa-f]+),(.*);\s*$')
REG_RE = re.compile(r'^(\d+),/REG,([^,]*),\{(.*)\},(.*)$')
ADDR_RE = re.compile(r'^\$([0-9A-Fa-f]+),\$([0-9A-Fa-f]+)\s*$')

HEADER_SCALARS = ('EPR', 'EAD', 'EPK', 'PNR', 'KNR', 'PRO', 'YNR', 'BEA', 'TEL',
                  'VAD', 'SPC', 'UMR', 'UTB', 'SRC', 'UMV', 'UP', 'SGB', 'ABL',
                  'RFG', 'DAB', 'PBA')


# --------------------------------------------------------------------------
# small helpers
# --------------------------------------------------------------------------
def de_float(text):
    """DAMOS writes German decimals (``-0.97`` or ``-0,97``)."""
    if text is None:
        return 0.0
    return float(str(text).strip().replace(',', '.'))


def parse_int(text):
    return int(str(text).strip())


def parse_addr(text):
    text = str(text).strip()
    if not text:
        return None
    if text.startswith('$'):
        return int(text[1:], 16)
    if text.lower().startswith('0x'):
        return int(text, 16)
    return int(text, 10)


def hexaddr(value):
    return None if value is None else '0x%05X' % value


def split_fields(body):
    return [part.strip() for part in body.split(',')]


def conversion(code):
    """Return dict(factor, offset, unit, precision, known) for a /SPW code."""
    entry = CONVERSION_CODES.get(code)
    if entry is None:
        return {'conversion_code': code, 'factor': None, 'offset': None,
                'unit': None, 'precision': None, 'known': False}
    factor, offset, unit, precision = entry
    return {'conversion_code': code, 'factor': float(factor),
            'offset': float(offset), 'unit': unit, 'precision': precision,
            'known': True}


# --------------------------------------------------------------------------
# record parsing
# --------------------------------------------------------------------------
def _parse_axis(tag, body, warnings, owner, which):
    """Build the axis dict from a /SPX or /SPY body."""
    fields = split_fields(body)
    if len(fields) != 5:
        warnings.append('%s %s: expected 5 fields, got %d' % (owner, tag, len(fields)))
        fields += ['0'] * (5 - len(fields))
    try:
        code = parse_int(fields[0])
        digits = parse_int(fields[1])
        axis = conversion(code)
        axis.update({
            'digits': digits,
            'signed': digits < 0,
            'raw_min': de_float(fields[2]),
            'raw_max': de_float(fields[3]),
            'sta_id': parse_int(fields[4]),
            'address': None,
        })
        if not axis['known']:
            warnings.append('%s %s: unknown conversion code %d' % (owner, tag, code))
        return axis
    except ValueError:
        warnings.append('%s %s: unparsable fields %r' % (owner, tag, fields))
        return None


def _empty_axis(axis):
    """A /SPX,/SPY block of all zeros declares no axis."""
    return (axis is None or (axis['conversion_code'] == 0 and axis['sta_id'] == 0
                             and axis['raw_min'] == 0 and axis['raw_max'] == 0))


def parse_dam(path):
    """Parse a .DAM file.  Returns a dict (see Docs/DAMOS_IMPORT.md)."""
    with open(path, 'rb') as fh:
        data = fh.read()
    text = data.decode('iso-8859-1')
    lines = [line.rstrip('\r') for line in text.split('\n')]

    header = []
    header_free = []
    objects = []
    sources = []
    controllers = []
    warnings = []

    i = 0
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()

        if not stripped:
            i += 1
            continue

        match = OBJECT_RE.match(line)
        if match:
            obj, i = _parse_object(match, lines, i, warnings)
            objects.append(obj)
            continue

        match = SRC_RE.match(line)
        if match:
            sources.append({
                'index': parse_int(match.group(1)),
                'name': match.group(2),
                'description': match.group(3),
                'address': parse_addr(match.group(4)),
                'fields': split_fields(match.group(5)),
            })
            i += 1
            continue

        match = REG_RE.match(line)
        if match:
            fields = split_fields(match.group(4))
            record = {
                'index': parse_int(match.group(1)),
                'name': match.group(2),
                'description': match.group(3),
                'fields': fields,
                'unit': None,
                'replacement': None,
            }
            for part in fields:
                if part.startswith('{') and part.endswith('}'):
                    record['unit'] = part[1:-1]
            if i + 1 < len(lines) and lines[i + 1].strip().startswith('/REP,'):
                record['replacement'] = split_fields(lines[i + 1].strip()[5:])
                i += 1
            controllers.append(record)
            i += 1
            continue

        if stripped.startswith('/'):
            tag, _, value = stripped.partition(',')
            header.append([tag, value])
            i += 1
            continue

        header_free.append(stripped)
        i += 1

    scalars = {}
    for tag, value in header:
        key = tag.lstrip('/')
        if key in HEADER_SCALARS and key not in scalars:
            scalars[key] = value.strip()

    return {
        'format': 'damos-dam',
        'version': 1,
        'path': os.path.abspath(path),
        'header': {
            'tags': header,
            'scalars': scalars,
            'free_text': header_free,
            'object_count': parse_int(scalars['SPC']) if 'SPC' in scalars else None,
        },
        'objects': objects,
        'sources': sources,
        'controllers': controllers,
        'warnings': warnings,
    }


def _parse_object(match, lines, i, warnings):
    index = parse_int(match.group(1))
    name = match.group(2).strip()
    description = match.group(3).strip()
    type_code = parse_int(match.group(4))
    owner = '#%d %s' % (index, name)

    tags = {}
    slot = None
    address = None
    abl = None
    j = i + 1
    while j < len(lines):
        raw = lines[j]
        stripped = raw.strip()
        if stripped.startswith('/ABL'):
            abl = stripped
            j += 1
            break
        if not stripped:
            j += 1
            continue
        addr = ADDR_RE.match(stripped)
        if addr:
            slot = int(addr.group(1), 16)
            address = int(addr.group(2), 16)
        elif stripped.startswith('/'):
            tag, _, value = stripped.partition(',')
            tags[tag] = value.strip()
        else:
            warnings.append('%s: unexpected line %r' % (owner, stripped))
        j += 1

    if type_code not in TYPE_CODES:
        warnings.append('%s: unknown /SPZ type code %d' % (owner, type_code))
    if address is None:
        warnings.append('%s: no address line' % owner)
    if abl is None:
        warnings.append('%s: no /ABL terminator' % owner)

    spw = split_fields(tags.get('/SPW', '0,0,0.,0.'))
    try:
        code = parse_int(spw[0])
        digits = parse_int(spw[1])
    except (IndexError, ValueError):
        code, digits = 0, 0
        warnings.append('%s: unparsable /SPW %r' % (owner, tags.get('/SPW')))
    if code not in CONVERSION_CODES:
        warnings.append('%s: unknown conversion code %d' % (owner, code))

    conv = conversion(code)
    width = 1 if abs(digits) != 2 else 2
    if abs(digits) > 2 and type_code != 12:
        warnings.append('%s: unexpected /SPW width %d' % (owner, digits))

    fkx = split_fields(tags.get('/FKX', '0,0,0'))
    fky = split_fields(tags.get('/FKY', '0,0,0'))
    declared_x = parse_int(fkx[0]) if fkx else 0
    declared_y = parse_int(fky[0]) if fky else 0

    axis_x = _parse_axis('/SPX', tags.get('/SPX', '0,0,0.,0.,0'), warnings, owner, 'x')
    axis_y = _parse_axis('/SPY', tags.get('/SPY', '0,0,0.,0.,0'), warnings, owner, 'y')
    if _empty_axis(axis_x):
        axis_x = None
    if _empty_axis(axis_y):
        axis_y = None

    abl_value = None
    if abl:
        parts = abl.split(',')
        if len(parts) > 1:
            try:
                abl_value = parse_int(parts[1].rstrip(';'))
            except ValueError:
                abl_value = None

    obj = {
        'index': index,
        'name': name,
        'description': description,
        'type': TYPE_CODES.get(type_code, 'eUnknown'),
        'type_code': type_code,
        'export_type': TYPE_EXPORT_NAME.get(type_code),
        'data_org': 'eLoHi' if width == 2 else 'eByte',
        'width': width,
        'signed': digits < 0,
        'raw_digits': digits,
        'conversion_code': code,
        'unit': conv['unit'],
        'factor': conv['factor'],
        'offset': conv['offset'],
        'precision': conv['precision'],
        'conversion_known': conv['known'],
        'slot_address': slot,
        'start_address': address,
        'value_address': address,
        'columns': 1,
        'rows': 1,
        'declared_x': declared_x,
        'declared_y': declared_y,
        'axis_x': axis_x,
        'axis_y': axis_y,
        'dims_source': 'declared',
        'abl': abl_value,
        'tags': {tag: value for tag, value in tags.items()
                 if tag not in ('/SPW', '/SPX', '/SPY', '/FKX', '/FKY')},
    }
    _apply_dimensions(obj, warnings)
    return obj, j


def _apply_dimensions(obj, warnings):
    """Fill columns/rows from the DAMOS declaration (type specific)."""
    code = obj['type_code']
    owner = '#%d %s' % (obj['index'], obj['name'])
    if code == 1:
        obj['columns'] = obj['rows'] = 1
    elif code == 2:
        obj['columns'] = obj['declared_x']
        obj['rows'] = 1
    elif code == 3:
        # DAMOS X count is the CSV "Rows", DAMOS Y count the CSV "Columns".
        obj['rows'] = obj['declared_x']
        obj['columns'] = obj['declared_y']
    elif code == 6:
        # One point per entry of the Stuetzstellenverteilung (STA) table.
        axis = obj['axis_x']
        obj['columns'] = axis['sta_id'] if axis else 0
        obj['rows'] = 1
        warnings.append('%s: type 6 point count taken from the STA id (%d), '
                        'no export row to verify against' % (owner, obj['columns']))
    elif code == 7:
        # eByte curve with one point per value of the range 0..max.
        axis = obj['axis_x']
        if axis:
            obj['columns'] = int(round(axis['raw_max'] - axis['raw_min'])) + 1
        obj['rows'] = 1
    elif code == 12:
        obj['columns'] = abs(obj['raw_digits'])
        obj['rows'] = 1
    if obj['columns'] <= 0:
        warnings.append('%s: no usable column count' % owner)
    # The first breakpoint array always sits directly behind the block
    # header; the second one behind the first.  With --bin the stored counts
    # replace the declaration and apply_image() re-derives both addresses.
    if code in (2, 3) and obj['start_address'] is not None:
        if obj['axis_x'] is not None:
            obj['axis_x']['address'] = obj['start_address'] + 4
            obj['axis_x']['count'] = obj['rows'] if code == 3 else obj['columns']
        if code == 3 and obj['axis_y'] is not None:
            obj['axis_y']['address'] = obj['start_address'] + 4 + obj['rows'] * 2
            obj['axis_y']['count'] = obj['columns']
    obj['value_address'] = _value_address(obj)


def _value_address(obj):
    """Address of the value array (CSV ``Fieldvalues.StartAddr``).

    The value array follows the inline breakpoint blocks::

        curve: header(4)  X breakpoints (n * 2)          values
        map:   header(4)  X breakpoints (rows * 2)
               header(4)  Y breakpoints (cols * 2)       values

    ``start_address`` is the address of the first header; single values,
    strings and type 7 tables have no inline axis and start at the value.
    """
    if obj['start_address'] is None:
        return None
    code = obj['type_code']
    if code == 2:
        return obj['start_address'] + 4 + obj['columns'] * 2
    if code == 3:
        return obj['start_address'] + 8 + (obj['rows'] + obj['columns']) * 2
    return obj['start_address']


# --------------------------------------------------------------------------
# optional image probe: the stored (real) geometry
# --------------------------------------------------------------------------
def apply_image(img, obj, warnings):
    """Read the inline axis headers from the ECU image and set real dims.

    Every curve/map block starts with ``(id, count)`` as two little-endian
    words; the count is the number of stored points.  It agrees with the
    export everywhere, while ``/FKX``/``/FKY`` are DAMOS *maxima* and are
    sometimes larger (see Docs/DAMOS_IMPORT.md).
    """
    addr = obj['start_address']
    code = obj['type_code']
    if code not in (2, 3) or addr is None or addr + 4 > len(img):
        return
    owner = '#%d %s' % (obj['index'], obj['name'])
    if obj['axis_x'] is None or (code == 3 and obj['axis_y'] is None):
        warnings.append('%s: geometry header found but the record has no axis' % owner)
        return
    _x_id, x_count = struct.unpack('<2H', img[addr:addr + 4])
    if code == 2:
        if x_count == 0:
            warnings.append('%s: stored axis header has count 0' % owner)
            return
        obj['columns'] = x_count
        obj['rows'] = 1
        obj['axis_x']['address'] = addr + 4
        obj['axis_x']['count'] = x_count
        obj['dims_source'] = 'image'
    else:
        header2 = addr + 4 + x_count * 2
        if header2 + 4 > len(img):
            warnings.append('%s: second axis header outside image' % owner)
            return
        _y_id, y_count = struct.unpack('<2H', img[header2:header2 + 4])
        obj['rows'] = x_count
        obj['columns'] = y_count
        obj['axis_x']['address'] = addr + 4
        obj['axis_x']['count'] = x_count
        obj['axis_y']['address'] = header2 + 4
        obj['axis_y']['count'] = y_count
        obj['dims_source'] = 'image'
    obj['value_address'] = _value_address(obj)


def load_image(path, size=None):
    with open(path, 'rb') as fh:
        data = fh.read()
    if size is not None and len(data) != size:
        raise SystemExit('%s is %d bytes, expected %d' % (path, len(data), size))
    return data


# --------------------------------------------------------------------------
# JSON
# --------------------------------------------------------------------------
def _json_object(obj):
    out = {
        'index': obj['index'],
        'name': obj['name'],
        'description': obj['description'],
        'type': obj['type'],
        'type_code': obj['type_code'],
        'data_org': obj['data_org'],
        'width': obj['width'],
        'signed': obj['signed'],
        'columns': obj['columns'],
        'rows': obj['rows'],
        'unit': obj['unit'],
        'factor': obj['factor'],
        'offset': obj['offset'],
        'precision': obj['precision'],
        'conversion_code': obj['conversion_code'],
        'slot_address': obj['slot_address'],
        'start_address': obj['start_address'],
        'value_address': obj['value_address'],
        'dims_source': obj['dims_source'],
        'declared': {'x': obj['declared_x'], 'y': obj['declared_y']},
        'axis_x': obj['axis_x'],
        'axis_y': obj['axis_y'],
        'abl': obj['abl'],
    }
    for key in ('slot_address', 'start_address', 'value_address'):
        out[key + '_hex'] = hexaddr(out[key])
    for axis in ('axis_x', 'axis_y'):
        if out[axis]:
            out[axis] = dict(out[axis])
            out[axis]['address_hex'] = hexaddr(out[axis].get('address'))
    return out


def dam_to_json(dam):
    return {
        'format': dam['format'],
        'version': dam['version'],
        'path': dam['path'],
        'header': dam['header'],
        'objects': [_json_object(obj) for obj in dam['objects']],
        'sources': dam['sources'],
        'controllers': dam['controllers'],
        'warnings': dam['warnings'],
    }


# --------------------------------------------------------------------------
# labels (format v2.1, see Docs/MAP_LABELS.md)
# --------------------------------------------------------------------------
LABEL_HEADER = ('# type;addr;name;unit;factor;offset;len;rows;axis_min;axis_max;'
                'axis_unit;axis_factor;axis_offset;axis_name;source;bytes;axis_addr')


def _fmt(value):
    if value is None:
        return ''
    if isinstance(value, int):
        return str(value)
    return repr(float(value))


def _clean(text, separator=';'):
    return ' '.join(str(text).replace(separator, ',').split())


def _axis_raw_endpoints(img, axis, count):
    if img is None or not axis or not axis.get('address'):
        return '', ''
    addr = axis['address']
    if count < 2 or addr + count * 2 > len(img):
        return '', ''
    first = struct.unpack('<H', img[addr:addr + 2])[0]
    last = struct.unpack('<H', img[addr + (count - 1) * 2:addr + count * 2])[0]
    return str(first), str(last)


def dam_to_labels(dam, img=None, software_id='', software_stand='', ecu_type='',
                  image_size=None, block_base=None, source='DAMOS-IMPORT',
                  name_field='name'):
    lines = ['# EDCsuite map labels v2.1']
    if software_id:
        lines.append('# software-id=%s' % software_id)
    if software_stand:
        lines.append('# software-stand=%s' % software_stand)
    if ecu_type:
        lines.append('# ecu-type=%s' % ecu_type)
    if image_size:
        lines.append('# image-size=0x%X' % image_size)
    if block_base is not None:
        lines.append('# block-base=0x%05X' % block_base)
    lines.append(LABEL_HEADER)

    for obj in dam['objects']:
        code = obj['type_code']
        name = obj['name'] if name_field == 'name' else obj['description']
        name = _clean(name or obj['name'])
        unit = _clean(obj['unit'] or '-')
        factor = obj['factor'] if obj['factor'] is not None else 1.0
        offset = obj['offset'] if obj['offset'] is not None else 0.0
        addr = obj['value_address']
        if addr is None or not name:
            continue
        if code == 3:
            lines.append('3d;0x%05X;%s;%s;%s;%s;%d;%d;;;;1.0;0.0;;%s;%d;' % (
                addr, name, unit, _fmt(factor), _fmt(offset),
                obj['columns'], obj['rows'], source, obj['width']))
        elif code in (2, 6, 7, 12):
            axis = obj['axis_x'] or {}
            amin, amax = _axis_raw_endpoints(img, axis, obj['columns'])
            axis_addr = axis.get('address')
            lines.append('1d;0x%05X;%s;%s;%s;%s;%d;1;%s;%s;%s;%s;%s;;%s;%d;%s' % (
                addr, name, unit, _fmt(factor), _fmt(offset), obj['columns'],
                amin, amax, _clean(axis.get('unit') or '-'),
                _fmt(axis.get('factor') if axis.get('factor') is not None else 1.0),
                _fmt(axis.get('offset') if axis.get('offset') is not None else 0.0),
                source, obj['width'],
                ('0x%05X' % axis_addr) if axis_addr else ''))
        else:
            lines.append('0d;0x%05X;%s;%s;%s;%s;1;1;;;;1.0;0.0;;%s;%d;' % (
                addr, name, unit, _fmt(factor), _fmt(offset), source, obj['width']))
    return '\n'.join(lines) + '\n'


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------
def main(argv=None):
    ap = argparse.ArgumentParser(
        description='Import a DAMOS .DAM file (JSON and/or EDCsuite labels).')
    ap.add_argument('dam', help='input .DAM file')
    ap.add_argument('--json', metavar='PATH',
                    help='write JSON here (default: JSON to stdout)')
    ap.add_argument('--labels', metavar='PATH',
                    help='write a v2.1 label file here')
    ap.add_argument('--bin', metavar='PATH',
                    help='ECU image: read the stored point counts and axis '
                         'addresses instead of the /FKX,/FKY declarations')
    ap.add_argument('--image-size', default=None,
                    help='expected image size (e.g. 0x40000)')
    ap.add_argument('--software-id', default='')
    ap.add_argument('--software-stand', default='')
    ap.add_argument('--ecu-type', default='')
    ap.add_argument('--block-base', default=None, help='e.g. 0x00C00')
    ap.add_argument('--source', default='DAMOS-IMPORT',
                    help='value of the label "source" field')
    ap.add_argument('--name-field', choices=('name', 'description'),
                    default='name', help='label display name source')
    ap.add_argument('--stats', action='store_true',
                    help='print a summary instead of JSON')
    ap.add_argument('--quiet', action='store_true',
                    help='suppress the warning summary on stderr')
    args = ap.parse_args(argv)

    dam = parse_dam(args.dam)
    img = None
    if args.bin:
        size = int(args.image_size, 0) if args.image_size else None
        img = load_image(args.bin, size)
        for obj in dam['objects']:
            apply_image(img, obj, dam['warnings'])

    if args.stats:
        print(stats_text(dam))
        return 0

    wrote = False
    if args.labels:
        if img is None and not args.quiet:
            print('note: no --bin, curve/map addresses use the DAMOS declaration; '
                  'pass --bin for the stored geometry', file=sys.stderr)
        block_base = int(args.block_base, 0) if args.block_base else None
        text = dam_to_labels(
            dam, img=img,
            software_id=args.software_id, software_stand=args.software_stand,
            ecu_type=args.ecu_type,
            image_size=(len(img) if img is not None else
                        (int(args.image_size, 0) if args.image_size else None)),
            block_base=block_base, source=args.source,
            name_field=args.name_field)
        with open(args.labels, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(text)
        wrote = True
        if not args.quiet:
            print('wrote %s (%d lines)' % (args.labels, text.count('\n')), file=sys.stderr)

    if args.json:
        payload = json.dumps(dam_to_json(dam), indent=1, ensure_ascii=False)
        with open(args.json, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(payload + '\n')
        wrote = True
        if not args.quiet:
            print('wrote %s' % args.json, file=sys.stderr)
    elif not wrote:
        # No output flag: JSON on stdout.
        if not args.quiet:
            print('JSON on stdout', file=sys.stderr)
        json.dump(dam_to_json(dam), sys.stdout, indent=1, ensure_ascii=False)
        sys.stdout.write('\n')
        wrote = True

    if not args.quiet and dam['warnings']:
        print('%d warning(s):' % len(dam['warnings']), file=sys.stderr)
        for message in dam['warnings'][:20]:
            print('  ' + message, file=sys.stderr)
        if len(dam['warnings']) > 20:
            print('  ... %d more' % (len(dam['warnings']) - 20), file=sys.stderr)
    return 0


def stats_text(dam):
    objects = dam['objects']
    by_type = {}
    by_width = {}
    unknown_codes = set()
    for obj in objects:
        by_type[obj['type']] = by_type.get(obj['type'], 0) + 1
        by_width[obj['data_org']] = by_width.get(obj['data_org'], 0) + 1
        if not obj['conversion_known']:
            unknown_codes.add(obj['conversion_code'])
    out = ['file              : %s' % dam['path'],
           'header /SPC       : %s' % dam['header']['object_count'],
           'objects parsed    : %d' % len(objects),
           'source records    : %d' % len(dam['sources']),
           'controller records: %d' % len(dam['controllers']),
           'warnings          : %d' % len(dam['warnings']),
           'types             : ' + ', '.join(
               '%s=%d' % (k, v) for k, v in sorted(by_type.items())),
           'storage           : ' + ', '.join(
               '%s=%d' % (k, v) for k, v in sorted(by_width.items())),
           'unknown codes     : ' + (', '.join(str(c) for c in sorted(unknown_codes))
                                     if unknown_codes else 'none')]
    return '\n'.join(out)


if __name__ == '__main__':
    sys.exit(main())
