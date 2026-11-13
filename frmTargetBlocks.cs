//---------------------------------------------------------------------------
//
// frmTargetBlocks.cs
//
// Small dialog for selecting the target blocks for map changes on a
// 256 KB EDC15V-5 file.  Deliberately without a designer file and without DevExpress,
// so that the form stays independent of the ribbon UI.
//
//---------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Windows.Forms;

namespace VAGSuite
{
    /// <summary>Selection of the target blocks (block 1/2/3/all).</summary>
    public sealed class frmTargetBlocks : Form
    {
        private CheckBox cbBlock1;
        private CheckBox cbBlock2;
        private CheckBox cbBlock3;
        private CheckBox cbAll;
        private Label lblInfo;
        private Label lblNote;
        private Button btnOk;
        private Button btnCancel;
        private bool updating;

        /// <summary>Bit mask of the selection (bit 0 = block 1, ..., 0 = no selection).</summary>
        public int SelectedMask
        {
            get
            {
                int mask = 0;
                if (cbBlock1.Checked) mask |= 1;
                if (cbBlock2.Checked) mask |= 2;
                if (cbBlock3.Checked) mask |= 4;
                return mask;
            }
        }

        public frmTargetBlocks(int currentMask)
        {
            InitializeForm();
            SetMask(currentMask);
        }

        private void InitializeForm()
        {
            Text = "Zielblock fuer Map-Aenderungen";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(430, 300);
            Font = new Font("Segoe UI", 9F);

            lblInfo = new Label();
            lblInfo.Location = new Point(12, 10);
            lblInfo.Size = new Size(406, 58);
            lblInfo.Text = "In welche Datenbloecke soll eine Map-Aenderung geschrieben werden?\r\n" +
                           "Der relative Offset innerhalb des 32-KB-Fensters bleibt gleich; " +
                           "die Adresse ist Blockbasis + Offset.";
            Controls.Add(lblInfo);

            cbBlock1 = new CheckBox();
            cbBlock1.Location = new Point(18, 76);
            cbBlock1.Size = new Size(390, 22);
            cbBlock1.Text = "Block 1  (" + Edc15V256Layout.BlockRanges[0] + ")";
            cbBlock1.CheckedChanged += new EventHandler(BlockCheckedChanged);
            Controls.Add(cbBlock1);

            cbBlock2 = new CheckBox();
            cbBlock2.Location = new Point(18, 102);
            cbBlock2.Size = new Size(390, 22);
            cbBlock2.Text = "Block 2  (" + Edc15V256Layout.BlockRanges[1] + ")";
            cbBlock2.CheckedChanged += new EventHandler(BlockCheckedChanged);
            Controls.Add(cbBlock2);

            cbBlock3 = new CheckBox();
            cbBlock3.Location = new Point(18, 128);
            cbBlock3.Size = new Size(390, 22);
            cbBlock3.Text = "Block 3  (" + Edc15V256Layout.BlockRanges[2] + ")";
            cbBlock3.CheckedChanged += new EventHandler(BlockCheckedChanged);
            Controls.Add(cbBlock3);

            cbAll = new CheckBox();
            cbAll.Location = new Point(18, 158);
            cbAll.Size = new Size(390, 22);
            cbAll.Text = "Alle drei Bloecke";
            cbAll.CheckedChanged += new EventHandler(AllCheckedChanged);
            Controls.Add(cbAll);

            lblNote = new Label();
            lblNote.Location = new Point(12, 186);
            lblNote.Size = new Size(406, 62);
            lblNote.ForeColor = Color.FromArgb(110, 60, 0);
            lblNote.Text = "Keine Auswahl = nur der Block, aus dem die Map geladen wurde " +
                           "(bisheriges Verhalten).\r\n" +
                           "Die Code-Region 0x10000-0x37FFF ist nie Schreibziel.";
            Controls.Add(lblNote);

            btnOk = new Button();
            btnOk.Text = "OK";
            btnOk.Location = new Point(252, 262);
            btnOk.Size = new Size(80, 26);
            btnOk.DialogResult = DialogResult.None;
            btnOk.Click += new EventHandler(OkClick);
            Controls.Add(btnOk);

            btnCancel = new Button();
            btnCancel.Text = "Abbrechen";
            btnCancel.Location = new Point(338, 262);
            btnCancel.Size = new Size(80, 26);
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void SetMask(int mask)
        {
            updating = true;
            cbBlock1.Checked = (mask & 1) != 0;
            cbBlock2.Checked = (mask & 2) != 0;
            cbBlock3.Checked = (mask & 4) != 0;
            cbAll.Checked = (mask & 7) == 7;
            updating = false;
        }

        private void BlockCheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;
            updating = true;
            cbAll.Checked = cbBlock1.Checked && cbBlock2.Checked && cbBlock3.Checked;
            updating = false;
        }

        private void AllCheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;
            updating = true;
            cbBlock1.Checked = cbAll.Checked;
            cbBlock2.Checked = cbAll.Checked;
            cbBlock3.Checked = cbAll.Checked;
            updating = false;
        }

        private void OkClick(object sender, EventArgs e)
        {
            int mask = SelectedMask;
            if (mask == Edc15V256Layout.MaskAll)
            {
                DialogResult answer = MessageBox.Show(
                    "Alle drei Datenbloecke werden ueberschrieben.\r\n\r\n" +
                    "Derselbe relative Offset wird in Block 1 (0x38000), Block 2 (0x00000) " +
                    "und Block 3 (0x08000) geschrieben, und alle drei Pruefsummenfelder " +
                    "werden neu versiegelt.\r\n\r\nFortfahren?",
                    "Zielblock: alle drei Bloecke",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
