namespace VAGSuite
{
    partial class frmBinmerger
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.groupFiles = new DevExpress.XtraEditors.GroupControl();
            this.labelLow = new System.Windows.Forms.Label();
            this.buttonEditLow = new DevExpress.XtraEditors.ButtonEdit();
            this.btnBrowseLow = new DevExpress.XtraEditors.SimpleButton();
            this.labelHigh = new System.Windows.Forms.Label();
            this.buttonEditHigh = new DevExpress.XtraEditors.ButtonEdit();
            this.btnBrowseHigh = new DevExpress.XtraEditors.SimpleButton();
            this.btnSwap = new DevExpress.XtraEditors.SimpleButton();
            this.labelStatus = new System.Windows.Forms.Label();
            this.labelPreview = new System.Windows.Forms.Label();
            this.memoPreview = new System.Windows.Forms.TextBox();
            this.groupOutput = new DevExpress.XtraEditors.GroupControl();
            this.labelOut = new System.Windows.Forms.Label();
            this.buttonEditOut = new DevExpress.XtraEditors.ButtonEdit();
            this.btnBrowseOut = new DevExpress.XtraEditors.SimpleButton();
            this.groupValidation = new DevExpress.XtraEditors.GroupControl();
            this.memoResult = new System.Windows.Forms.TextBox();
            this.btnMerge = new DevExpress.XtraEditors.SimpleButton();
            this.btnCancel = new DevExpress.XtraEditors.SimpleButton();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            ((System.ComponentModel.ISupportInitialize)(this.groupFiles)).BeginInit();
            this.groupFiles.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditLow.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditHigh.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.groupOutput)).BeginInit();
            this.groupOutput.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditOut.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.groupValidation)).BeginInit();
            this.groupValidation.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupFiles
            // 
            this.groupFiles.Controls.Add(this.labelLow);
            this.groupFiles.Controls.Add(this.buttonEditLow);
            this.groupFiles.Controls.Add(this.btnBrowseLow);
            this.groupFiles.Controls.Add(this.labelHigh);
            this.groupFiles.Controls.Add(this.buttonEditHigh);
            this.groupFiles.Controls.Add(this.btnBrowseHigh);
            this.groupFiles.Controls.Add(this.btnSwap);
            this.groupFiles.Controls.Add(this.labelStatus);
            this.groupFiles.Controls.Add(this.labelPreview);
            this.groupFiles.Controls.Add(this.memoPreview);
            this.groupFiles.Location = new System.Drawing.Point(8, 8);
            this.groupFiles.Name = "groupFiles";
            this.groupFiles.Size = new System.Drawing.Size(724, 278);
            this.groupFiles.TabIndex = 0;
            this.groupFiles.Text = "Eingangsdateien (je 128 KB) und Byte-Reihenfolge";
            // 
            // labelLow
            // 
            this.labelLow.AutoSize = true;
            this.labelLow.Location = new System.Drawing.Point(16, 28);
            this.labelLow.Name = "labelLow";
            this.labelLow.Size = new System.Drawing.Size(323, 13);
            this.labelLow.TabIndex = 0;
            this.labelLow.Text = "LOW  = gerade Byte-Offsets (0x00000, 0x00002, ...) -> erstes Byte";
            // 
            // buttonEditLow
            // 
            this.buttonEditLow.Location = new System.Drawing.Point(16, 46);
            this.buttonEditLow.Name = "buttonEditLow";
            this.buttonEditLow.Size = new System.Drawing.Size(620, 20);
            this.buttonEditLow.TabIndex = 1;
            this.buttonEditLow.TextChanged += new System.EventHandler(this.buttonEditLow_TextChanged);
            // 
            // btnBrowseLow
            // 
            this.btnBrowseLow.Location = new System.Drawing.Point(644, 44);
            this.btnBrowseLow.Name = "btnBrowseLow";
            this.btnBrowseLow.Size = new System.Drawing.Size(64, 24);
            this.btnBrowseLow.TabIndex = 2;
            this.btnBrowseLow.Text = "...";
            this.btnBrowseLow.Click += new System.EventHandler(this.btnBrowseLow_Click);
            // 
            // labelHigh
            // 
            this.labelHigh.AutoSize = true;
            this.labelHigh.Location = new System.Drawing.Point(16, 80);
            this.labelHigh.Name = "labelHigh";
            this.labelHigh.Size = new System.Drawing.Size(330, 13);
            this.labelHigh.TabIndex = 3;
            this.labelHigh.Text = "HIGH = ungerade Byte-Offsets (0x00001, 0x00003, ...) -> zweites Byte";
            // 
            // buttonEditHigh
            // 
            this.buttonEditHigh.Location = new System.Drawing.Point(16, 98);
            this.buttonEditHigh.Name = "buttonEditHigh";
            this.buttonEditHigh.Size = new System.Drawing.Size(620, 20);
            this.buttonEditHigh.TabIndex = 4;
            this.buttonEditHigh.TextChanged += new System.EventHandler(this.buttonEditHigh_TextChanged);
            // 
            // btnBrowseHigh
            // 
            this.btnBrowseHigh.Location = new System.Drawing.Point(644, 96);
            this.btnBrowseHigh.Name = "btnBrowseHigh";
            this.btnBrowseHigh.Size = new System.Drawing.Size(64, 24);
            this.btnBrowseHigh.TabIndex = 5;
            this.btnBrowseHigh.Text = "...";
            this.btnBrowseHigh.Click += new System.EventHandler(this.btnBrowseHigh_Click);
            // 
            // btnSwap
            // 
            this.btnSwap.Location = new System.Drawing.Point(16, 130);
            this.btnSwap.Name = "btnSwap";
            this.btnSwap.Size = new System.Drawing.Size(150, 24);
            this.btnSwap.TabIndex = 6;
            this.btnSwap.Text = "LOW/HIGH tauschen";
            this.btnSwap.Click += new System.EventHandler(this.btnSwap_Click);
            // 
            // labelStatus
            // 
            this.labelStatus.AutoSize = true;
            this.labelStatus.Location = new System.Drawing.Point(180, 136);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Size = new System.Drawing.Size(0, 13);
            this.labelStatus.TabIndex = 7;
            // 
            // labelPreview
            // 
            this.labelPreview.AutoSize = true;
            this.labelPreview.Location = new System.Drawing.Point(16, 166);
            this.labelPreview.Name = "labelPreview";
            this.labelPreview.Size = new System.Drawing.Size(154, 13);
            this.labelPreview.TabIndex = 8;
            this.labelPreview.Text = "Vorschau (erste 16 Byte)";
            // 
            // memoPreview
            // 
            this.memoPreview.BackColor = System.Drawing.SystemColors.Window;
            this.memoPreview.Font = new System.Drawing.Font("Consolas", 9F);
            this.memoPreview.Location = new System.Drawing.Point(16, 184);
            this.memoPreview.Multiline = true;
            this.memoPreview.Name = "memoPreview";
            this.memoPreview.ReadOnly = true;
            this.memoPreview.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.memoPreview.Size = new System.Drawing.Size(692, 76);
            this.memoPreview.TabIndex = 9;
            this.memoPreview.WordWrap = false;
            // 
            // groupOutput
            // 
            this.groupOutput.Controls.Add(this.labelOut);
            this.groupOutput.Controls.Add(this.buttonEditOut);
            this.groupOutput.Controls.Add(this.btnBrowseOut);
            this.groupOutput.Location = new System.Drawing.Point(8, 292);
            this.groupOutput.Name = "groupOutput";
            this.groupOutput.Size = new System.Drawing.Size(724, 76);
            this.groupOutput.TabIndex = 1;
            this.groupOutput.Text = "Ziel";
            // 
            // labelOut
            // 
            this.labelOut.AutoSize = true;
            this.labelOut.Location = new System.Drawing.Point(16, 26);
            this.labelOut.Name = "labelOut";
            this.labelOut.Size = new System.Drawing.Size(224, 13);
            this.labelOut.TabIndex = 0;
            this.labelOut.Text = "Zieldatei (256 KB, frei waehlbar)";
            // 
            // buttonEditOut
            // 
            this.buttonEditOut.Location = new System.Drawing.Point(16, 44);
            this.buttonEditOut.Name = "buttonEditOut";
            this.buttonEditOut.Size = new System.Drawing.Size(620, 20);
            this.buttonEditOut.TabIndex = 1;
            // 
            // btnBrowseOut
            // 
            this.btnBrowseOut.Location = new System.Drawing.Point(644, 42);
            this.btnBrowseOut.Name = "btnBrowseOut";
            this.btnBrowseOut.Size = new System.Drawing.Size(64, 24);
            this.btnBrowseOut.TabIndex = 2;
            this.btnBrowseOut.Text = "...";
            this.btnBrowseOut.Click += new System.EventHandler(this.btnBrowseOut_Click);
            // 
            // groupValidation
            // 
            this.groupValidation.Controls.Add(this.memoResult);
            this.groupValidation.Location = new System.Drawing.Point(8, 374);
            this.groupValidation.Name = "groupValidation";
            this.groupValidation.Size = new System.Drawing.Size(724, 196);
            this.groupValidation.TabIndex = 2;
            this.groupValidation.Text = "Validierung nach dem Merge (Groesse, Teilenummer, Pruefsumme)";
            // 
            // memoResult
            // 
            this.memoResult.BackColor = System.Drawing.SystemColors.Window;
            this.memoResult.Font = new System.Drawing.Font("Consolas", 9F);
            this.memoResult.Location = new System.Drawing.Point(16, 28);
            this.memoResult.Multiline = true;
            this.memoResult.Name = "memoResult";
            this.memoResult.ReadOnly = true;
            this.memoResult.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.memoResult.Size = new System.Drawing.Size(692, 154);
            this.memoResult.TabIndex = 0;
            this.memoResult.WordWrap = false;
            // 
            // btnMerge
            // 
            this.btnMerge.Location = new System.Drawing.Point(540, 578);
            this.btnMerge.Name = "btnMerge";
            this.btnMerge.Size = new System.Drawing.Size(90, 28);
            this.btnMerge.TabIndex = 3;
            this.btnMerge.Text = "Merge";
            this.btnMerge.Click += new System.EventHandler(this.btnMerge_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(640, 578);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 28);
            this.btnCancel.TabIndex = 4;
            this.btnCancel.Text = "Abbrechen";
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // openFileDialog1
            // 
            this.openFileDialog1.Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*";
            this.openFileDialog1.Title = "Binaerdatei waehlen";
            // 
            // saveFileDialog1
            // 
            this.saveFileDialog1.DefaultExt = "bin";
            this.saveFileDialog1.Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*";
            this.saveFileDialog1.Title = "Zieldatei waehlen";
            // 
            // frmBinmerger
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(740, 614);
            this.Controls.Add(this.groupFiles);
            this.Controls.Add(this.groupOutput);
            this.Controls.Add(this.groupValidation);
            this.Controls.Add(this.btnMerge);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "frmBinmerger";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "LOW/HIGH-Chips zu einem 256-KB-Image zusammenfuehren";
            this.groupFiles.ResumeLayout(false);
            this.groupFiles.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditLow.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditHigh.Properties)).EndInit();
            this.groupOutput.ResumeLayout(false);
            this.groupOutput.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditOut.Properties)).EndInit();
            this.groupValidation.ResumeLayout(false);
            this.groupValidation.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.GroupControl groupFiles;
        private System.Windows.Forms.Label labelLow;
        private DevExpress.XtraEditors.ButtonEdit buttonEditLow;
        private DevExpress.XtraEditors.SimpleButton btnBrowseLow;
        private System.Windows.Forms.Label labelHigh;
        private DevExpress.XtraEditors.ButtonEdit buttonEditHigh;
        private DevExpress.XtraEditors.SimpleButton btnBrowseHigh;
        private DevExpress.XtraEditors.SimpleButton btnSwap;
        private System.Windows.Forms.Label labelStatus;
        private System.Windows.Forms.Label labelPreview;
        private System.Windows.Forms.TextBox memoPreview;
        private DevExpress.XtraEditors.GroupControl groupOutput;
        private System.Windows.Forms.Label labelOut;
        private DevExpress.XtraEditors.ButtonEdit buttonEditOut;
        private DevExpress.XtraEditors.SimpleButton btnBrowseOut;
        private DevExpress.XtraEditors.GroupControl groupValidation;
        private System.Windows.Forms.TextBox memoResult;
        private DevExpress.XtraEditors.SimpleButton btnMerge;
        private DevExpress.XtraEditors.SimpleButton btnCancel;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.SaveFileDialog saveFileDialog1;
    }
}
