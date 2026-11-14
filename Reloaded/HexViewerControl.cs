//---------------------------------------------------------------------------
//
// HexViewerControl.cs
//
// A dependency-free WinForms hex viewer for the DevExpress-free Reloaded UI.
// Plain System.Windows.Forms + GDI+ only: no DevExpress, no Krypton, no
// OxyPlot.  Targets .NET Framework 4.0 (Windows XP SP3 through Windows 11).
//
// Layout (16 bytes per row, monospace font):
//
//   00000000  00 11 22 33 44 55 66 77  88 99 AA BB CC DD EE FF  ...............
//   ^offset^  ^--------------------hex-------------------------^  ^---ascii---^
//
// Rendering draws only the visible rows (for a 256 KB buffer that is ~16 000
// rows total, ~30 drawn per frame) into a double-buffered surface, so large
// buffers scroll smoothly without flicker.
//
//---------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Reloaded
{
    /// <summary>
    /// Self-contained hex viewer: offset column (hex), 16 bytes per row as
    /// hex, and an ASCII column (printable characters, '.' for the rest).
    /// Supports a base address, vertical scrolling (scrollbar + mouse wheel)
    /// and single-byte selection.
    /// </summary>
    public class HexViewerControl : UserControl
    {
        /// <summary>Number of bytes shown per row.</summary>
        public const int BytesPerRow = 16;

        // "00000000" — 8 hex digits, enough for the full 32-bit address space.
        private const int OffsetDigits = 8;

        // The hex column is 48 characters wide: 16 bytes * 2 hex digits = 32,
        // plus 14 single spaces between bytes and 2 spaces at the 8-byte group
        // gap = 16 separators.  32 + 16 = 48.
        private const int HexLineChars = 48;

        // Left inset (px) before the offset column.
        private const int LeftMargin = 8;

        // ------------------------------------------------------------------
        // Theme hooks.  The control's own BackColor / ForeColor / Font are
        // used for the background, the hex/ASCII text and the cell metrics;
        // the three fields below colour the offset column and the selection.
        // Leave them at the defaults for the standard light look, or set them
        // from a Krypton palette later — nothing here is hard-coded dark-only.
        // ------------------------------------------------------------------
        public Color OffsetColor = SystemColors.GrayText;
        public Color SelectionColor = SystemColors.Highlight;
        public Color SelectionTextColor = SystemColors.HighlightText;

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------
        private byte[] _data = new byte[0];
        private int _baseAddress;        // added to every displayed offset
        private int _selectedOffset = -1; // byte index into _data, or -1

        private readonly VScrollBar _scrollBar;
        private readonly StringFormat _fmt;
        private readonly System.Text.StringBuilder _hexBuilder =
            new System.Text.StringBuilder(HexLineChars);
        private readonly System.Text.StringBuilder _asciiBuilder =
            new System.Text.StringBuilder(BytesPerRow);

        private float _charWidth;
        private float _lineHeight;
        private bool _metricsValid;

        private static readonly char[] HexDigits = "0123456789ABCDEF".ToCharArray();

        public HexViewerControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;

            TabStop = true;

            _fmt = new StringFormat(StringFormat.GenericTypographic);
            _fmt.FormatFlags |= StringFormatFlags.NoWrap;

            _scrollBar = new VScrollBar
            {
                Dock = DockStyle.Right,
                SmallChange = 1,
                TabStop = false
            };
            _scrollBar.Scroll += delegate { Invalidate(); };
            Controls.Add(_scrollBar);

            // Hex alignment requires a monospace font.  Consolas ships with
            // Windows Vista+, Courier New with every Windows back to 95; both
            // are system fonts (no font file is shipped).  The Font property
            // stays settable so a theme can override it.
            //
            // Set LAST: assigning Font raises OnFontChanged, which re-reads
            // the scrollbar (UpdateScrollbar), so _scrollBar must already
            // exist here.
            Font = ChooseMonospaceFont();
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>The bytes currently displayed.</summary>
        public byte[] Data
        {
            get { return _data; }
            set { SetBytes(value); }
        }

        /// <summary>Base address added to every displayed offset.</summary>
        public int CurrentAddress
        {
            get { return _baseAddress; }
            set { SetCurrentAddress(value); }
        }

        /// <summary>Byte index of the current selection, or -1 if none.</summary>
        public int SelectedOffset { get { return _selectedOffset; } }

        /// <summary>Raised when the selected byte changes.</summary>
        public event EventHandler SelectionChanged;

        /// <summary>Replaces the displayed data and clears the selection.</summary>
        public void SetBytes(byte[] data)
        {
            _data = data ?? new byte[0];
            _selectedOffset = -1;
            UpdateScrollbar();
            Invalidate();
        }

        /// <summary>Sets the base offset shown in the left column.</summary>
        public void SetCurrentAddress(int address)
        {
            _baseAddress = address;
            Invalidate();
        }

        /// <summary>
        /// Selects the byte at <paramref name="offset"/> (clamped to the data
        /// range), scrolls it into view and raises <see cref="SelectionChanged"/>.
        /// This is the programmatic counterpart of clicking a byte.
        /// </summary>
        public void SelectOffset(int offset)
        {
            SetSelection(offset);
        }

        // ------------------------------------------------------------------
        // Layout / scrolling
        // ------------------------------------------------------------------

        private int RowCount
        {
            get { return (_data.Length + BytesPerRow - 1) / BytesPerRow; }
        }

        private int VisibleRowCount
        {
            get
            {
                if (_lineHeight <= 0) return 1;
                return Math.Max(1, (int)(ClientSize.Height / _lineHeight));
            }
        }

        private int ScrollBarWidth
        {
            get
            {
                int w = _scrollBar.Width;
                return w > 0 ? w : SystemInformation.VerticalScrollBarWidth;
            }
        }

        private void UpdateScrollbar()
        {
            int rows = RowCount;
            int visible = VisibleRowCount;

            _scrollBar.Minimum = 0;
            _scrollBar.Maximum = Math.Max(0, rows - 1);
            _scrollBar.LargeChange = Math.Max(1, visible);
            _scrollBar.Enabled = rows > visible;

            int maxValue = Math.Max(0, rows - visible);
            if (_scrollBar.Value > maxValue) _scrollBar.Value = maxValue;
        }

        private void ScrollBy(int rows)
        {
            if (rows == 0) return;
            int min = _scrollBar.Minimum;
            int max = Math.Max(min, _scrollBar.Maximum - _scrollBar.LargeChange + 1);
            int value = _scrollBar.Value + rows;
            if (value < min) value = min;
            if (value > max) value = max;
            if (value != _scrollBar.Value)
            {
                _scrollBar.Value = value;
                Invalidate();
            }
        }

        private void EnsureMetrics(Graphics g)
        {
            if (_metricsValid) return;

            StringFormat fmt = StringFormat.GenericTypographic;
            _charWidth = g.MeasureString("0", Font, PointF.Empty, fmt).Width;
            _lineHeight = Font.GetHeight(g);
            if (_charWidth <= 0) _charWidth = g.MeasureString("M", Font).Width;
            if (_lineHeight <= 0) _lineHeight = Font.Height;

            _metricsValid = true;
            UpdateScrollbar();
        }

        private void InvalidateMetrics()
        {
            _metricsValid = false;
            UpdateScrollbar();
            Invalidate();
        }

        // Column start/width for the hex and ASCII columns, derived from the
        // measured monospace character advance.
        private void ComputeColumns(out float xHex, out float hexWidth, out float xAscii)
        {
            float gap = 2f * _charWidth;
            float xOffset = LeftMargin;
            float offsetWidth = OffsetDigits * _charWidth;
            xHex = xOffset + offsetWidth + gap;
            hexWidth = HexLineChars * _charWidth;
            xAscii = xHex + hexWidth + gap;
        }

        // Maps a character index in the 48-character hex line back to the byte
        // it belongs to (spaces map to -1).  Byte 7 is followed by two spaces,
        // which shifts every later byte's start by one character.
        private static int HexCharIndexToByte(int charIndex)
        {
            for (int i = 0; i < BytesPerRow; i++)
            {
                int start = i < 8 ? 3 * i : 3 * i + 1;
                if (charIndex >= start && charIndex < start + 2) return i;
            }
            return -1;
        }

        private int HitTestOffset(int x, int y)
        {
            if (_data.Length == 0) return -1;

            if (!_metricsValid)
            {
                using (Graphics g = CreateGraphics()) EnsureMetrics(g);
            }

            int contentWidth = ClientSize.Width - ScrollBarWidth;
            if (x < 0 || x >= contentWidth || y < 0) return -1;

            int row = _scrollBar.Value + (int)(y / _lineHeight);
            if (row < 0 || row >= RowCount) return -1;

            float xHex, hexWidth, xAscii;
            ComputeColumns(out xHex, out hexWidth, out xAscii);

            int col = -1;
            if (x >= xHex && x < xHex + hexWidth)
            {
                int charIndex = (int)((x - xHex) / _charWidth);
                col = HexCharIndexToByte(charIndex);
            }
            else if (x >= xAscii && x < xAscii + BytesPerRow * _charWidth)
            {
                col = (int)((x - xAscii) / _charWidth);
            }

            if (col < 0) return -1;
            int offset = row * BytesPerRow + col;
            return offset < _data.Length ? offset : -1;
        }

        private void SetSelection(int offset)
        {
            if (_data.Length == 0) return;
            if (offset < 0) offset = 0;
            if (offset >= _data.Length) offset = _data.Length - 1;
            if (offset == _selectedOffset) return;

            _selectedOffset = offset;
            ScrollToSelection();
            Invalidate();
            OnSelectionChanged();
        }

        private void ScrollToSelection()
        {
            int row = _selectedOffset / BytesPerRow;
            int visible = VisibleRowCount;
            if (row < _scrollBar.Value)
            {
                _scrollBar.Value = row;
            }
            else if (row >= _scrollBar.Value + visible)
            {
                _scrollBar.Value = row - visible + 1;
            }
        }

        protected virtual void OnSelectionChanged()
        {
            EventHandler handler = SelectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------

        private const int WM_MOUSEWHEEL = 0x020A;

        protected override void WndProc(ref Message m)
        {
            // Wheel messages are delivered to the focused window; this control
            // grabs focus on hover (OnMouseEnter), so it receives them and can
            // scroll the whole view regardless of the hovered column.
            if (m.Msg == WM_MOUSEWHEEL)
            {
                int wheel = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                int lines = SystemInformation.MouseWheelScrollLines;
                if (lines <= 0) lines = 3;
                ScrollBy(-(wheel / 120) * lines);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!Focused) Focus();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;

            int offset = HitTestOffset(e.X, e.Y);
            if (offset >= 0) SetSelection(offset);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_data.Length != 0 && _selectedOffset >= 0)
            {
                int sel = _selectedOffset;
                int rowStart = (sel / BytesPerRow) * BytesPerRow;
                switch (e.KeyCode)
                {
                    case Keys.Left:  SetSelection(sel - 1); e.Handled = true; break;
                    case Keys.Right: SetSelection(sel + 1); e.Handled = true; break;
                    case Keys.Up:    SetSelection(sel - BytesPerRow); e.Handled = true; break;
                    case Keys.Down:  SetSelection(sel + BytesPerRow); e.Handled = true; break;
                    case Keys.PageUp:   SetSelection(sel - VisibleRowCount * BytesPerRow); e.Handled = true; break;
                    case Keys.PageDown: SetSelection(sel + VisibleRowCount * BytesPerRow); e.Handled = true; break;
                    case Keys.Home:  SetSelection(rowStart); e.Handled = true; break;
                    case Keys.End:   SetSelection(Math.Min(_data.Length - 1, rowStart + BytesPerRow - 1)); e.Handled = true; break;
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            InvalidateMetrics();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollbar();
            Invalidate();
        }

        // ------------------------------------------------------------------
        // Painting
        // ------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            int contentWidth = ClientSize.Width - ScrollBarWidth;
            if (contentWidth <= 0) return;

            g.Clear(BackColor);
            if (_data.Length == 0) return;

            EnsureMetrics(g);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int firstRow = _scrollBar.Value;
            int rows = RowCount;
            int lastRow = Math.Min(rows - 1, firstRow + VisibleRowCount);

            float xHex, hexWidth, xAscii;
            ComputeColumns(out xHex, out hexWidth, out xAscii);

            for (int row = firstRow; row <= lastRow; row++)
            {
                int rowStart = row * BytesPerRow;
                int count = Math.Min(BytesPerRow, _data.Length - rowStart);
                float y = (row - firstRow) * _lineHeight;

                DrawOffset(g, rowStart, y);
                DrawHex(g, rowStart, count, xHex, y);
                DrawAscii(g, rowStart, count, xAscii, y);
                DrawSelection(g, rowStart, count, xHex, xAscii, y);
            }
        }

        private void DrawOffset(Graphics g, int rowStart, float y)
        {
            string text = unchecked(_baseAddress + rowStart).ToString("X8");
            using (Brush b = new SolidBrush(OffsetColor))
            {
                g.DrawString(text, Font, b, LeftMargin, y, _fmt);
            }
        }

        private void DrawHex(Graphics g, int rowStart, int count, float x, float y)
        {
            _hexBuilder.Length = 0;
            for (int i = 0; i < count; i++)
            {
                byte b = _data[rowStart + i];
                _hexBuilder.Append(HexDigits[b >> 4]);
                _hexBuilder.Append(HexDigits[b & 0xF]);
                if (i == 7) _hexBuilder.Append("  ");
                else if (i < BytesPerRow - 1) _hexBuilder.Append(' ');
            }
            using (Brush b = new SolidBrush(ForeColor))
            {
                g.DrawString(_hexBuilder.ToString(), Font, b, x, y, _fmt);
            }
        }

        private void DrawAscii(Graphics g, int rowStart, int count, float x, float y)
        {
            _asciiBuilder.Length = 0;
            for (int i = 0; i < count; i++)
            {
                byte b = _data[rowStart + i];
                _asciiBuilder.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
            }
            using (Brush b = new SolidBrush(ForeColor))
            {
                g.DrawString(_asciiBuilder.ToString(), Font, b, x, y, _fmt);
            }
        }

        private void DrawSelection(Graphics g, int rowStart, int count, float xHex, float xAscii, float y)
        {
            if (_selectedOffset < rowStart || _selectedOffset >= rowStart + count) return;

            int col = _selectedOffset - rowStart;
            int hexCharIndex = col < 8 ? 3 * col : 3 * col + 1;
            float hexX = xHex + hexCharIndex * _charWidth;
            float asciiX = xAscii + col * _charWidth;

            using (Brush fill = new SolidBrush(SelectionColor))
            {
                g.FillRectangle(fill, hexX, y, 2f * _charWidth, _lineHeight);
                g.FillRectangle(fill, asciiX, y, _charWidth, _lineHeight);
            }

            byte b = _data[_selectedOffset];
            using (Brush text = new SolidBrush(SelectionTextColor))
            {
                g.DrawString(
                    new string(new[] { HexDigits[b >> 4], HexDigits[b & 0xF] }),
                    Font, text, hexX, y, _fmt);
                g.DrawString(
                    (b >= 0x20 && b <= 0x7E ? (char)b : '.').ToString(),
                    Font, text, asciiX, y, _fmt);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _fmt != null) _fmt.Dispose();
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Font ChooseMonospaceFont()
        {
            foreach (FontFamily family in FontFamily.Families)
            {
                if (string.Equals(family.Name, "Consolas", StringComparison.OrdinalIgnoreCase))
                    return new Font(family, 9f);
            }
            foreach (FontFamily family in FontFamily.Families)
            {
                if (string.Equals(family.Name, "Courier New", StringComparison.OrdinalIgnoreCase))
                    return new Font(family, 9f);
            }
            return new Font(FontFamily.GenericMonospace, 9f);
        }
    }
}
