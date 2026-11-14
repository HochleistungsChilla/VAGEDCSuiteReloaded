# HexViewerControl — dependency-free hex viewer

A single-file `System.Windows.Forms.UserControl` (offset + hex + ASCII columns)
for the DevExpress-free Reloaded UI.  Plain WinForms + GDI+ only: no DevExpress,
no Krypton, no OxyPlot, no external HexBox assembly.  Targets .NET Framework
4.0 (Windows XP SP3 through Windows 11).

## API

- `SetBytes(byte[] data)` — replaces the displayed data (also settable via the `Data` property).
- `SetCurrentAddress(int address)` — base offset added to the left column (also the `CurrentAddress` property).
- `SelectOffset(int offset)` — programmatic selection (clamps, scrolls into view, raises `SelectionChanged`). Clicking a byte selects too.
- `SelectedOffset` (int) — current byte index, `-1` if none; `SelectionChanged` event.
- Theming: the control's `BackColor`, `ForeColor` and `Font` plus three public fields
  `OffsetColor`, `SelectionColor`, `SelectionTextColor` — no hard-coded dark-only palette.

## Build / verification (Wine, in-box .NET 4.0 csc)

Standalone compile — only the BCL references, no new dependencies:

```sh
wine 'C:\windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' \
  /nologo /target:library /out:HexViewerControl.dll \
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll \
  /r:System.Windows.Forms.dll \
  HexViewerControl.cs
# -> exit 0, HexViewerControl.dll (12288 B)
```

Throwaway host (kept under `/tmp`, never in the repo) that drops the control into
a Form and loads a 256 KB buffer.  Verified **headless** on a private Xvfb display
(never on Fran's real desktop `:0`), with a private `WINEPREFIX`:

```sh
# 1. private X server
pkill -f 'Xvfb :98'; sleep 1
Xvfb :98 -screen 0 1280x800x24 -nolisten tcp >/tmp/xvfb98.log 2>&1 &
sleep 3

# 2. build the host (host source /tmp/hexhost/hexhost.cs)
WINEPREFIX=/tmp/hexhost/prefix wine 'C:\windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' \
  /nologo /target:winexe /out:/tmp/hexhost/hexhost.exe \
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll \
  HexViewerControl.cs /tmp/hexhost/hexhost.cs

# 3. run in a Wine virtual desktop on :98 (no XAUTHORITY needed)
DISPLAY=:98 WINEPREFIX=/tmp/hexhost/prefix WINEDEBUG=-all \
  wine explorer /desktop=Hex,1000x700 /tmp/hexhost/hexhost.exe >/tmp/hexhost98.log 2>&1 &

# 4. screenshot the virtual desktop
DISPLAY=:98 import -window root /tmp/shot98.png
```

## Evidence (verified on Xvfb, screenshots inspected with view_image)

- Offset column renders hex addresses: base `0x10000` → `00010000`, `00010010`, … (increments `0x10` per row).
- Hex column renders 16 bytes per row with the 8+8 group gap; ASCII column shows printable characters and `.` for the rest.
- Status line confirmed `262144 bytes` (256 KB) loaded and displayed.
- **Click-select**: clicking the `41` byte (offset 0x41) set the selection — status read
  `sel offset 65 (0x00000041), value 0x41 'A'`, and the byte + its ASCII `A` were painted
  with a solid highlight in both columns.
- **Mouse-wheel scrolling**: 20 wheel-down notches moved the offset column from `00010000`
  to `000103C0` (exactly 60 rows × 0x10 = 0x3C0 bytes; 3 lines/notch), confirming the
  `WM_MOUSEWHEEL` → scrollbar → repaint chain.

## Caveats / limits

- **Font**: uses a monospace font (Consolas → Courier New → `GenericMonospace` fallback) rather
  than the literal UI font, because hex-column alignment needs fixed advance widths. It is a
  system font (nothing shipped) and stays overridable via the `Font` property.
- **Address width**: offsets are shown as 8 hex digits (32-bit). `baseAddress + rowStart` is
  computed as `int`; a base near `int.MaxValue` wraps rather than grows to 64-bit.
- **Large files**: rendering draws only the visible rows (~30 of ~16 384 rows for 256 KB), so
  scrolling is O(visible). `SetBytes` stores the caller's array by reference (no copy). The
  `VScrollBar` handles multi-thousand-row ranges; beyond 4 GB the 8-digit offset column wraps.
- **Selection is single-byte** (view-only, no range select, no editing). Click in the hex or
  ASCII column selects; clicking the offset column or a gap does nothing. Arrow/PageUp/PageDown/
  Home/End move the selection once one exists.
- **No horizontal scrollbar**: if the control is narrower than the three columns (~76 monospace
  chars + margins ≈ 550–600 px at 9 pt), the ASCII column clips on the right.
- **Mouse wheel** scrolls ~3 lines per notch (`SystemInformation.MouseWheelScrollLines`); the
  control grabs focus on hover so the wheel works without a prior click.
