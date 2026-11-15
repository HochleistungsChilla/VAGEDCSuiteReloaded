//---------------------------------------------------------------------------
//
// MergeSplitForm.cs
//
// Small Reloaded UI on top of the UI-free ChipMergeSplit class:
//
//   Merge  two 128 KB chip dumps (LOW = even offsets, HIGH = odd offsets)
//          into one 256 KB image;
//   Split  one 256 KB image back into two 128 KB halves, with the
//          low-is-even interleave switch.
//
// The app targets .NET Framework 4.0 and also runs under wine-mono, so this
// form sticks to plain WinForms labels/panels plus the Krypton controls the
// rest of the app already uses (KryptonTextBox, KryptonButton,
// KryptonRadioButton).  Colours are derived from the active palette, so the
// dialog follows the light/dark theme set in the main window.
//
// Nothing is written partially: both merge and split build the complete byte
// array in memory, write it to a temporary file next to the target and only
// then replace the target (see WriteAtomic).
//
//---------------------------------------------------------------------------

using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Krypton.Toolkit;
using VAGSuite;

namespace Reloaded
{
    /// <summary>
    /// Merge/split dialog for the two 128 KB chips of a 256 KB EDC15V-5 image.
    /// </summary>
    public class MergeSplitForm : KryptonForm
    {
        private const int PreviewBytes = 16;

        // --- merge section ---------------------------------------------------
        private readonly KryptonTextBox _lowBox;
        private readonly KryptonTextBox _highBox;
        private readonly KryptonTextBox _outBox;
        private readonly KryptonTextBox _preview;
        private readonly Label _mergeStatus;

        // --- split section ---------------------------------------------------
        private readonly KryptonTextBox _imageBox;
        private readonly KryptonTextBox _dirBox;
        private readonly KryptonRadioButton _evenLow;
        private readonly KryptonRadioButton _evenHigh;
        private readonly Label _splitTargets;
        private readonly Label _splitStatus;

        // --- palette-derived colours ----------------------------------------
        private readonly Color _formBack;
        private readonly Color _line;
        private readonly Color _text;
        private readonly Color _muted;
        private readonly Color _warn;

        // The output path is only re-suggested while the user has not picked
        // one explicitly (same rule as the old DevExpress dialog).
        private string _lastSuggestion = string.Empty;
        private bool _targetChosenInDialog;

        public MergeSplitForm() : this(null)
        {
        }

        /// <summary>
        /// Creates the dialog.  <paramref name="palette"/> is the palette of
        /// the opening window; null falls back to the persisted theme colour.
        /// </summary>
        public MergeSplitForm(KryptonPalette palette)
        {
            // Palette-derived colours come from the active theme data, so the
            // dialog follows all four themes without per-theme branching.
            UiPalette c = ThemeStore.Active;
            _formBack = c.Window;
            _line = c.Line;
            _text = c.Text;
            _muted = c.Muted;
            _warn = c.Dark ? Color.FromArgb(255, 170, 80) : Color.FromArgb(217, 119, 6);

            // --- form chrome -------------------------------------------------
            Text = L("merge_split_title", "Merge / Split chips");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 606);
            MinimumSize = new Size(700, 560);

            if (palette != null)
            {
                Palette = palette;
                try
                {
                    StateCommon.Back.Draw = InheritBool.True;
                    StateCommon.Back.Color1 = _formBack;
                    StateCommon.Back.Color2 = _formBack;
                    StateCommon.Back.ColorStyle = PaletteColorStyle.Solid;
                }
                catch
                {
                    // A missing palette member must not break the dialog.
                }
            }
            BackColor = _formBack;

            // ================================================================
            // Merge
            // ================================================================
            Panel merge = CreateSection(12, 10, 736, 300);
            Controls.Add(merge);

            AddHeading(merge, L("merge_split_merge_heading", "Merge two 128 KB chips"), 12, 8, 500);
            AddHint(merge,
                L("merge_split_merge_hint",
                  "LOW supplies the even byte offsets, HIGH the odd ones."),
                12, 28, 712);

            AddCaption(merge, L("merge_split_low", "LOW (even offsets)"), 12, 53, 128);
            _lowBox = CreatePathBox(merge, 144, 50, 520);
            _lowBox.TextChanged += (s, e) => RefreshMergeState();
            AddBrowse(merge, 670, 50, delegate { BrowseInto(_lowBox, false); });

            AddCaption(merge, L("merge_split_high", "HIGH (odd offsets)"), 12, 83, 128);
            _highBox = CreatePathBox(merge, 144, 80, 520);
            _highBox.TextChanged += (s, e) => RefreshMergeState();
            AddBrowse(merge, 670, 80, delegate { BrowseInto(_highBox, false); });

            AddCaption(merge, L("merge_split_output", "Output image"), 12, 113, 128);
            _outBox = CreatePathBox(merge, 144, 110, 520);
            _outBox.TextChanged += (s, e) => RefreshMergeState();
            AddBrowse(merge, 670, 110, delegate { BrowseInto(_outBox, true); });

            AddCaption(merge, L("merge_split_preview", "Preview"), 12, 141, 200);
            _preview = new KryptonTextBox();
            _preview.Multiline = true;              // before the size, see Main.cs
            _preview.ReadOnly = true;
            _preview.ScrollBars = ScrollBars.Vertical;
            _preview.Font = new Font("Consolas", 8.25F);
            _preview.Location = new Point(12, 159);
            _preview.Size = new Size(714, 68);
            _preview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            merge.Controls.Add(_preview);

            _mergeStatus = AddStatus(merge, 12, 233, 560, 34);

            KryptonButton swap = CreateActionButton(
                L("merge_split_swap", "Swap"), 12, 270, 88, 26);
            swap.Click += delegate { SwapHalves(); };
            merge.Controls.Add(swap);

            KryptonButton mergeButton = CreateActionButton(
                L("merge_split_merge_action", "Merge"), 620, 268, 106, 28);
            mergeButton.Click += delegate { DoMerge(); };
            merge.Controls.Add(mergeButton);

            // ================================================================
            // Split
            // ================================================================
            Panel split = CreateSection(12, 318, 736, 240);
            Controls.Add(split);

            AddHeading(split, L("merge_split_split_heading", "Split a 256 KB image"), 12, 8, 500);
            AddHint(split,
                L("merge_split_split_hint",
                  "Writes two 128 KB halves next to the image."),
                12, 28, 712);

            AddCaption(split, L("merge_split_input", "Image (256 KB)"), 12, 53, 128);
            _imageBox = CreatePathBox(split, 144, 50, 520);
            _imageBox.TextChanged += (s, e) => RefreshSplitState();
            AddBrowse(split, 670, 50, delegate { BrowseInto(_imageBox, false); });

            AddCaption(split, L("merge_split_outdir", "Output folder"), 12, 83, 128);
            _dirBox = CreatePathBox(split, 144, 80, 520);
            _dirBox.TextChanged += (s, e) => RefreshSplitState();
            AddBrowse(split, 670, 80, delegate { BrowseForFolder(); });

            _evenLow = new KryptonRadioButton();
            _evenLow.Text = L("merge_split_even_low", "Even offsets > LOW (standard)");
            _evenLow.Location = new Point(30, 112);
            _evenLow.AutoSize = true;
            _evenLow.Checked = true;
            _evenLow.CheckedChanged += (s, e) => RefreshSplitState();
            split.Controls.Add(_evenLow);

            _evenHigh = new KryptonRadioButton();
            _evenHigh.Text = L("merge_split_even_high", "Odd offsets > LOW (swapped)");
            _evenHigh.Location = new Point(30, 136);
            _evenHigh.AutoSize = true;
            _evenHigh.CheckedChanged += (s, e) => RefreshSplitState();
            split.Controls.Add(_evenHigh);

            _splitTargets = AddStatus(split, 12, 162, 580, 34);
            _splitStatus = AddStatus(split, 12, 198, 580, 20);

            KryptonButton splitButton = CreateActionButton(
                L("merge_split_split_action", "Split"), 604, 194, 122, 28);
            splitButton.Click += delegate { DoSplit(); };
            split.Controls.Add(splitButton);

            // ================================================================
            // Close
            // ================================================================
            KryptonButton close = CreateActionButton(
                L("merge_split_close", "Close"), 652, 566, 96, 28);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            CancelButton = close;
            RefreshMergeState();
            RefreshSplitState();
        }

        // ------------------------------------------------------------------
        // construction helpers
        // ------------------------------------------------------------------

        /// <summary>Bordered section panel on the form background (rounded corners).</summary>
        private RoundedPanel CreateSection(int x, int y, int width, int height)
        {
            RoundedPanel panel = new RoundedPanel();
            panel.Location = new Point(x, y);
            panel.Size = new Size(width, height);
            panel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.BackColor = _formBack;
            panel.CornerRadius = 8;
            panel.BorderColor = _line;
            return panel;
        }

        private void AddHeading(Control parent, string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, 20);
            label.AutoSize = false;
            label.BackColor = _formBack;
            label.ForeColor = _text;
            label.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
            parent.Controls.Add(label);
        }

        private void AddHint(Control parent, string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, 16);
            label.AutoSize = false;
            label.BackColor = _formBack;
            label.ForeColor = _muted;
            label.Font = new Font("Segoe UI", 8.25F);
            parent.Controls.Add(label);
        }

        private void AddCaption(Control parent, string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, 18);
            label.AutoSize = false;
            label.BackColor = _formBack;
            label.ForeColor = _text;
            label.Font = new Font("Segoe UI", 9F);
            parent.Controls.Add(label);
        }

        private Label AddStatus(Control parent, int x, int y, int width, int height)
        {
            Label label = new Label();
            label.Text = string.Empty;
            label.Location = new Point(x, y);
            label.Size = new Size(width, height);
            label.AutoSize = false;
            label.BackColor = _formBack;
            label.ForeColor = _muted;
            label.Font = new Font("Segoe UI", 8.25F);
            parent.Controls.Add(label);
            return label;
        }

        private KryptonTextBox CreatePathBox(Control parent, int x, int y, int width)
        {
            KryptonTextBox box = new KryptonTextBox();
            box.Location = new Point(x, y);
            box.Size = new Size(width, 24);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            box.Font = new Font("Segoe UI", 9F);
            parent.Controls.Add(box);
            return box;
        }

        private void AddBrowse(Control parent, int x, int y, EventHandler handler)
        {
            KryptonButton button = CreateActionButton(
                L("merge_split_browse", "Browse"), x, y, 64, 24);
            button.Font = new Font("Segoe UI", 8.25F);
            button.Click += handler;
            parent.Controls.Add(button);
        }

        private static KryptonButton CreateActionButton(string text, int x, int y,
                                                        int width, int height)
        {
            KryptonButton button = new KryptonButton();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            button.AutoSize = false;
            return button;
        }

        // ------------------------------------------------------------------
        // state
        // ------------------------------------------------------------------

        private void SwapHalves()
        {
            string low = _lowBox.Text;
            _lowBox.Text = _highBox.Text;
            _highBox.Text = low;
            RefreshMergeState();
        }

        private void RefreshMergeState()
        {
            bool lowThere = File.Exists(_lowBox.Text.Trim());
            bool highThere = File.Exists(_highBox.Text.Trim());

            StringBuilder preview = new StringBuilder();
            preview.Append("LOW    ");
            preview.Append(PreviewOf(_lowBox.Text.Trim(), lowThere));
            preview.AppendLine();
            preview.Append("HIGH   ");
            preview.Append(PreviewOf(_highBox.Text.Trim(), highThere));
            preview.AppendLine();
            if (lowThere && highThere)
            {
                try
                {
                    byte[] low = ReadHead(_lowBox.Text.Trim(), PreviewBytes);
                    byte[] high = ReadHead(_highBox.Text.Trim(), PreviewBytes);
                    preview.Append("MERGE  " +
                        ChipMergeSplit.PreviewMerged(low, high, PreviewBytes));
                }
                catch (Exception ex)
                {
                    preview.Append("MERGE  " +
                        string.Format(L("merge_split_not_readable",
                                        "(not readable: {0})"), ex.Message));
                }
            }
            else
            {
                preview.Append("MERGE  " +
                    L("merge_split_pick_both", "(select both files)"));
            }
            _preview.Text = preview.ToString();

            if (lowThere && highThere)
            {
                FileInfo lowInfo = new FileInfo(_lowBox.Text.Trim());
                FileInfo highInfo = new FileInfo(_highBox.Text.Trim());
                if (lowInfo.Length != highInfo.Length)
                {
                    SetStatus(_mergeStatus, string.Format(
                        L("merge_split_status_diff",
                          "LOW {0} / HIGH {1} - sizes differ, merge not possible."),
                        ChipMergeSplit.SizeText(lowInfo.Length),
                        ChipMergeSplit.SizeText(highInfo.Length)), true);
                }
                else if (lowInfo.Length != ChipMergeSplit.ChipSize)
                {
                    SetStatus(_mergeStatus, string.Format(
                        L("merge_split_status_notchip",
                          "LOW/HIGH {0} each - expected 128 KB per chip."),
                        ChipMergeSplit.SizeText(lowInfo.Length)), true);
                }
                else
                {
                    SetStatus(_mergeStatus, string.Format(
                        L("merge_split_status_ready",
                          "LOW/HIGH {0} each - ready to merge."),
                        ChipMergeSplit.SizeText(lowInfo.Length)), false);
                }
            }
            else if (lowThere || highThere)
            {
                SetStatus(_mergeStatus,
                    L("merge_split_status_second", "Select the second file."), false);
            }
            else
            {
                SetStatus(_mergeStatus, string.Empty, false);
            }

            string suggestion = lowThere
                ? ChipMergeSplit.SuggestMergedPath(_lowBox.Text.Trim())
                : string.Empty;
            if (_outBox.Text.Length == 0 || _outBox.Text == _lastSuggestion)
            {
                _outBox.Text = suggestion;
            }
            _lastSuggestion = suggestion;
        }

        private void RefreshSplitState()
        {
            string image = _imageBox.Text.Trim();
            bool exists = File.Exists(image);

            if (exists)
            {
                try
                {
                    long length = new FileInfo(image).Length;
                    if ((length & 1) != 0)
                    {
                        SetStatus(_splitStatus, string.Format(
                            L("merge_split_status_odd",
                              "Length {0} bytes is odd - cannot split."),
                            length), true);
                    }
                    else if (length != ChipMergeSplit.ImageSize)
                    {
                        SetStatus(_splitStatus, string.Format(
                            L("merge_split_status_badimage",
                              "Size {0} - expected 256 KB."),
                            ChipMergeSplit.SizeText(length)), true);
                    }
                    else
                    {
                        SetStatus(_splitStatus, string.Format(
                            L("merge_split_status_image", "Size {0} - ready to split."),
                            ChipMergeSplit.SizeText(length)), false);
                    }
                }
                catch (Exception ex)
                {
                    SetStatus(_splitStatus, ex.Message, true);
                }

                if (_dirBox.Text.Trim().Length == 0)
                {
                    _dirBox.Text = Path.GetDirectoryName(image);
                }
            }
            else
            {
                SetStatus(_splitStatus, string.Empty, false);
            }

            string dir = _dirBox.Text.Trim();
            if (exists && dir.Length > 0 && Directory.Exists(dir))
            {
                _splitTargets.Text = string.Format(
                    L("merge_split_targets", "Targets: {0}   and   {1}"),
                    Path.Combine(dir, ChipMergeSplit.SuggestHalfName(image, "low")),
                    Path.Combine(dir, ChipMergeSplit.SuggestHalfName(image, "high")));
            }
            else
            {
                _splitTargets.Text = L("merge_split_targets_none",
                    "Targets: choose an output folder");
            }
        }

        private void SetStatus(Label label, string text, bool warn)
        {
            label.Text = text;
            label.ForeColor = warn ? _warn : _muted;
        }

        private static string PreviewOf(string path, bool exists)
        {
            if (!exists)
            {
                return L("merge_split_no_file", "(not selected)");
            }
            try
            {
                return ChipMergeSplit.Preview(ReadHead(path, PreviewBytes), PreviewBytes);
            }
            catch (Exception ex)
            {
                return string.Format(
                    L("merge_split_not_readable", "(not readable: {0})"), ex.Message);
            }
        }

        private static byte[] ReadHead(string path, int count)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open,
                                                      FileAccess.Read, FileShare.ReadWrite))
            {
                int length = (int)Math.Min(count, stream.Length);
                byte[] buffer = new byte[length];
                int offset = 0;
                while (offset < length)
                {
                    int read = stream.Read(buffer, offset, length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                if (offset != length)
                {
                    byte[] shorter = new byte[offset];
                    Array.Copy(buffer, shorter, offset);
                    return shorter;
                }
                return buffer;
            }
        }

        // ------------------------------------------------------------------
        // file selection
        // ------------------------------------------------------------------

        private void BrowseInto(KryptonTextBox box, bool save)
        {
            if (save)
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Filter = BinaryFilter();
                    if (box.Text.Trim().Length > 0)
                    {
                        try
                        {
                            dialog.InitialDirectory =
                                Path.GetDirectoryName(box.Text.Trim());
                            dialog.FileName = Path.GetFileName(box.Text.Trim());
                        }
                        catch
                        {
                            // Keep the dialog defaults.
                        }
                    }
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        box.Text = dialog.FileName;
                        _targetChosenInDialog = true;
                    }
                }
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = BinaryFilter();
                dialog.CheckFileExists = true;
                if (box.Text.Trim().Length > 0)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(box.Text.Trim());
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            dialog.InitialDirectory = dir;
                        }
                    }
                    catch
                    {
                        // Keep the dialog defaults.
                    }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    box.Text = dialog.FileName;
                }
            }
        }

        private void BrowseForFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                string current = _dirBox.Text.Trim();
                if (current.Length > 0 && Directory.Exists(current))
                {
                    dialog.SelectedPath = current;
                }
                else
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(_imageBox.Text.Trim());
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            dialog.SelectedPath = dir;
                        }
                    }
                    catch
                    {
                        // Keep the dialog defaults.
                    }
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _dirBox.Text = dialog.SelectedPath;
                }
            }
        }

        private static string BinaryFilter()
        {
            return L("merge_split_filter",
                     "Binary images (*.bin)|*.bin|All files (*.*)|*.*");
        }

        // ------------------------------------------------------------------
        // merge
        // ------------------------------------------------------------------

        private void DoMerge()
        {
            try
            {
                string lowPath = _lowBox.Text.Trim();
                string highPath = _highBox.Text.Trim();
                if (!File.Exists(lowPath))
                {
                    Warn(L("merge_split_err_no_low", "The LOW file does not exist."));
                    return;
                }
                if (!File.Exists(highPath))
                {
                    Warn(L("merge_split_err_no_high", "The HIGH file does not exist."));
                    return;
                }

                FileInfo lowInfo = new FileInfo(lowPath);
                FileInfo highInfo = new FileInfo(highPath);
                if (lowInfo.Length != highInfo.Length)
                {
                    Error(string.Format(
                        L("merge_split_err_size",
                          "LOW and HIGH are not the same size: {0} vs {1} bytes. " +
                          "Nothing was written."),
                        lowInfo.Length, highInfo.Length));
                    return;
                }
                if (lowInfo.Length != ChipMergeSplit.ChipSize &&
                    !Confirm(string.Format(
                        L("merge_split_warn_notchip",
                          "The halves are {0}, not 128 KB. The result will not be " +
                          "a 256 KB image.\r\n\r\nContinue?"),
                        ChipMergeSplit.SizeText(lowInfo.Length))))
                {
                    return;
                }

                string target = _outBox.Text.Trim();
                if (target.Length == 0)
                {
                    Warn(L("merge_split_err_target", "Please choose a target file."));
                    return;
                }
                if (SamePath(target, lowPath) || SamePath(target, highPath))
                {
                    Error(L("merge_split_err_same",
                        "The target must differ from both input files. " +
                        "Nothing was written."));
                    return;
                }
                if (File.Exists(target) && !_targetChosenInDialog &&
                    !Confirm(string.Format(
                        L("merge_split_confirm_overwrite",
                          "The target file already exists:\r\n\r\n{0}\r\n\r\nOverwrite?"),
                        target)))
                {
                    return;
                }

                byte[] low = ChipMergeSplit.ReadFile(lowPath);
                byte[] high = ChipMergeSplit.ReadFile(highPath);
                byte[] image = ChipMergeSplit.Merge(low, high);
                WriteAtomic(target, image);
                _targetChosenInDialog = false;

                int offset;
                bool partOk = ChipMergeSplit.FindPartnumber(
                    image, ChipMergeSplit.ExpectedPartnumber, out offset);
                string partText = partOk
                    ? string.Format(L("merge_split_part_found", "found at 0x{0:X5}"), offset)
                    : L("merge_split_part_missing", "not found (hint only)");
                Info(string.Format(
                    L("merge_split_done_merge",
                      "Merge complete.\r\n\r\nResult: {0}\r\nPart number {1}: {2}"),
                    ChipMergeSplit.SizeText(image.Length),
                    ChipMergeSplit.ExpectedPartnumber, partText));
                RefreshMergeState();
            }
            catch (Exception ex)
            {
                Error(string.Format(L("merge_split_failed", "{0} failed"), "Merge") +
                      "\r\n\r\n" + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // split
        // ------------------------------------------------------------------

        private void DoSplit()
        {
            try
            {
                string imagePath = _imageBox.Text.Trim();
                if (!File.Exists(imagePath))
                {
                    Warn(L("merge_split_err_no_image", "The image file does not exist."));
                    return;
                }

                string dir = _dirBox.Text.Trim();
                if (dir.Length == 0 || !Directory.Exists(dir))
                {
                    Warn(string.Format(
                        L("merge_split_err_outdir",
                          "The output folder does not exist:\r\n\r\n{0}"), dir));
                    return;
                }

                FileInfo info = new FileInfo(imagePath);
                if ((info.Length & 1) != 0)
                {
                    Error(string.Format(
                        L("merge_split_err_odd",
                          "The file has an odd length ({0} bytes) and cannot be " +
                          "split. Nothing was written."), info.Length));
                    return;
                }
                if (info.Length != ChipMergeSplit.ImageSize &&
                    !Confirm(string.Format(
                        L("merge_split_warn_notimage",
                          "The file is {0}, not 256 KB.\r\n\r\nSplit anyway?"),
                        ChipMergeSplit.SizeText(info.Length))))
                {
                    return;
                }

                string lowPath = Path.Combine(dir,
                    ChipMergeSplit.SuggestHalfName(imagePath, "low"));
                string highPath = Path.Combine(dir,
                    ChipMergeSplit.SuggestHalfName(imagePath, "high"));
                if (SamePath(lowPath, imagePath) || SamePath(highPath, imagePath))
                {
                    Error(L("merge_split_err_split_same",
                        "A half would overwrite the input image. " +
                        "Choose another output folder. Nothing was written."));
                    return;
                }
                if (File.Exists(lowPath) || File.Exists(highPath))
                {
                    if (!Confirm(string.Format(
                            L("merge_split_confirm_overwrite_two",
                              "At least one target file already exists:\r\n\r\n" +
                              "{0}\r\n{1}\r\n\r\nOverwrite?"), lowPath, highPath)))
                    {
                        return;
                    }
                }

                byte[] image = ChipMergeSplit.ReadFile(imagePath);
                byte[] low;
                byte[] high;
                ChipMergeSplit.Split(image, _evenLow.Checked, out low, out high);
                WriteAtomic(lowPath, low);
                WriteAtomic(highPath, high);

                Info(string.Format(
                    L("merge_split_done_split",
                      "Split complete.\r\n\r\nLOW:  {0}\r\nHIGH: {1}"),
                    ChipMergeSplit.SizeText(low.Length),
                    ChipMergeSplit.SizeText(high.Length)));
                RefreshSplitState();
            }
            catch (Exception ex)
            {
                Error(string.Format(L("merge_split_failed", "{0} failed"), "Split") +
                      "\r\n\r\n" + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // output helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes <paramref name="bytes"/> to <paramref name="path"/> through a
        /// temporary file in the same directory: the target is only touched
        /// once the complete content is on disk, so a failure can never leave
        /// a truncated output file behind.
        /// </summary>
        private static void WriteAtomic(string path, byte[] bytes)
        {
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch
                {
                    // The temporary file is a leftover, never a valid target.
                }
            }
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // dialog helpers
        // ------------------------------------------------------------------

        private void Warn(string text)
        {
            MessageBox.Show(this, text, Text, MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
        }

        private void Error(string text)
        {
            MessageBox.Show(this, text, Text, MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
        }

        private void Info(string text)
        {
            MessageBox.Show(this, text, Text, MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
        }

        private bool Confirm(string text)
        {
            return MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo,
                                   MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>
        /// Translation with an English fallback: a key that is not in the
        /// locale table (yet) shows the built-in text instead of the raw key.
        /// </summary>
        private static string L(string key, string fallback)
        {
            string value = I18n.T(key);
            return string.IsNullOrEmpty(value) || value == key ? fallback : value;
        }
    }
}
