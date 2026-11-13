namespace VAGSuite
{
    partial class frmBinSplit
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
            this.groupInput = new DevExpress.XtraEditors.GroupControl();
            this.labelIn = new System.Windows.Forms.Label();
            this.buttonEditIn = new DevExpress.XtraEditors.ButtonEdit();
            this.btnBrowseIn = new DevExpress.XtraEditors.SimpleButton();
            this.labelInStatus = new System.Windows.Forms.Label();
            this.groupOrder = new DevExpress.XtraEditors.GroupControl();
            this.rdoLowEven = new System.Windows.Forms.RadioButton();
            this.rdoHighEven = new System.Windows.Forms.RadioButton();
            this.groupOutput = new DevExpress.XtraEditors.GroupControl();
            this.labelDir = new System.Windows.Forms.Label();
            this.buttonEditOutDir = new DevExpress.XtraEditors.ButtonEdit();
            this.btnBrowseDir = new DevExpress.XtraEditors.SimpleButton();
            this.labelNames = new System.Windows.Forms.Label();
            this.groupResult = new DevExpress.XtraEditors.GroupControl();
            this.memoResult = new System.Windows.Forms.TextBox();
            this.btnSplit = new DevExpress.XtraEditors.SimpleButton();
            this.btnCancel = new DevExpress.XtraEditors.SimpleButton();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            ((System.ComponentModel.ISupportInitialize)(this.groupInput)).BeginInit();
            this.groupInput.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditIn.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.groupOrder)).BeginInit();
            this.groupOrder.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.groupOutput)).BeginInit();
            this.groupOutput.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditOutDir.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.groupResult)).BeginInit();
            this.groupResult.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupInput
            // 
            this.groupInput.Controls.Add(this.labelIn);
            this.groupInput.Controls.Add(this.buttonEditIn);
            this.groupInput.Controls.Add(this.btnBrowseIn);
            this.groupInput.Controls.Add(this.labelInStatus);
            this.groupInput.Location = new System.Drawing.Point(8, 8);
            this.groupInput.Name = "groupInput";
            this.groupInput.Size = new System.Drawing.Size(724, 96);
            this.groupInput.TabIndex = 0;
            this.groupInput.Text = "Eingabedatei (256 KB)";
            // 
            // labelIn
            // 
            this.labelIn.AutoSize = true;
            this.labelIn.Location = new System.Drawing.Point(16, 26);
            this.labelIn.Name = "labelIn";
            this.labelIn.Size = new System.Drawing.Size(199, 13);
            this.labelIn.TabIndex = 0;
            this.labelIn.Text = "Zusammengefuehrtes Image";
            // 
            // buttonEditIn
            // 
            this.buttonEditIn.Location = new System.Drawing.Point(16, 44);
            this.buttonEditIn.Name = "buttonEditIn";
            this.buttonEditIn.Size = new System.Drawing.Size(620, 20);
            this.buttonEditIn.TabIndex = 1;
            this.buttonEditIn.TextChanged += new System.EventHandler(this.buttonEditIn_TextChanged);
            // 
            // btnBrowseIn
            // 
            this.btnBrowseIn.Location = new System.Drawing.Point(644, 42);
            this.btnBrowseIn.Name = "btnBrowseIn";
            this.btnBrowseIn.Size = new System.Drawing.Size(64, 24);
            this.btnBrowseIn.TabIndex = 2;
            this.btnBrowseIn.Text = "...";
            this.btnBrowseIn.Click += new System.EventHandler(this.btnBrowseIn_Click);
            // 
            // labelInStatus
            // 
            this.labelInStatus.AutoSize = true;
            this.labelInStatus.Location = new System.Drawing.Point(16, 70);
            this.labelInStatus.Name = "labelInStatus";
            this.labelInStatus.Size = new System.Drawing.Size(0, 13);
            this.labelInStatus.TabIndex = 3;
            // 
            // groupOrder
            // 
            this.groupOrder.Controls.Add(this.rdoLowEven);
            this.groupOrder.Controls.Add(this.rdoHighEven);
            this.groupOrder.Location = new System.Drawing.Point(8, 112);
            this.groupOrder.Name = "groupOrder";
            this.groupOrder.Size = new System.Drawing.Size(724, 90);
            this.groupOrder.TabIndex = 1;
            this.groupOrder.Text = "Byte-Reihenfolge";
            // 
            // rdoLowEven
            // 
            this.rdoLowEven.AutoSize = true;
            this.rdoLowEven.Checked = true;
            this.rdoLowEven.Location = new System.Drawing.Point(16, 28);
            this.rdoLowEven.Name = "rdoLowEven";
            this.rdoLowEven.Size = new System.Drawing.Size(468, 17);
            this.rdoLowEven.TabIndex = 0;
            this.rdoLowEven.TabStop = true;
            this.rdoLowEven.Text = "Standard (little endian): gerade Bytes -> _low, ungerade Bytes -> _high";
            this.rdoLowEven.UseVisualStyleBackColor = true;
            // 
            // rdoHighEven
            // 
            this.rdoHighEven.AutoSize = true;
            this.rdoHighEven.Location = new System.Drawing.Point(16, 54);
            this.rdoHighEven.Name = "rdoHighEven";
            this.rdoHighEven.Size = new System.Drawing.Size(474, 17);
            this.rdoHighEven.TabIndex = 1;
            this.rdoHighEven.Text = "Umgekehrt: ungerade Bytes -> _low, gerade Bytes -> _high";
            this.rdoHighEven.UseVisualStyleBackColor = true;
            // 
            // groupOutput
            // 
            this.groupOutput.Controls.Add(this.labelDir);
            this.groupOutput.Controls.Add(this.buttonEditOutDir);
            this.groupOutput.Controls.Add(this.btnBrowseDir);
            this.groupOutput.Controls.Add(this.labelNames);
            this.groupOutput.Location = new System.Drawing.Point(8, 210);
            this.groupOutput.Name = "groupOutput";
            this.groupOutput.Size = new System.Drawing.Size(724, 116);
            this.groupOutput.TabIndex = 2;
            this.groupOutput.Text = "Ausgabe";
            // 
            // labelDir
            // 
            this.labelDir.AutoSize = true;
            this.labelDir.Location = new System.Drawing.Point(16, 26);
            this.labelDir.Name = "labelDir";
            this.labelDir.Size = new System.Drawing.Size(167, 13);
            this.labelDir.TabIndex = 0;
            this.labelDir.Text = "Ausgabe-Verzeichnis";
            // 
            // buttonEditOutDir
            // 
            this.buttonEditOutDir.Location = new System.Drawing.Point(16, 44);
            this.buttonEditOutDir.Name = "buttonEditOutDir";
            this.buttonEditOutDir.Size = new System.Drawing.Size(620, 20);
            this.buttonEditOutDir.TabIndex = 1;
            this.buttonEditOutDir.TextChanged += new System.EventHandler(this.buttonEditOutDir_TextChanged);
            // 
            // btnBrowseDir
            // 
            this.btnBrowseDir.Location = new System.Drawing.Point(644, 42);
            this.btnBrowseDir.Name = "btnBrowseDir";
            this.btnBrowseDir.Size = new System.Drawing.Size(64, 24);
            this.btnBrowseDir.TabIndex = 2;
            this.btnBrowseDir.Text = "...";
            this.btnBrowseDir.Click += new System.EventHandler(this.btnBrowseDir_Click);
            // 
            // labelNames
            // 
            this.labelNames.AutoSize = true;
            this.labelNames.Location = new System.Drawing.Point(16, 76);
            this.labelNames.Name = "labelNames";
            this.labelNames.Size = new System.Drawing.Size(0, 13);
            this.labelNames.TabIndex = 3;
            // 
            // groupResult
            // 
            this.groupResult.Controls.Add(this.memoResult);
            this.groupResult.Location = new System.Drawing.Point(8, 334);
            this.groupResult.Name = "groupResult";
            this.groupResult.Size = new System.Drawing.Size(724, 176);
            this.groupResult.TabIndex = 3;
            this.groupResult.Text = "Ergebnis";
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
            this.memoResult.Size = new System.Drawing.Size(692, 134);
            this.memoResult.TabIndex = 0;
            this.memoResult.WordWrap = false;
            // 
            // btnSplit
            // 
            this.btnSplit.Location = new System.Drawing.Point(540, 520);
            this.btnSplit.Name = "btnSplit";
            this.btnSplit.Size = new System.Drawing.Size(90, 28);
            this.btnSplit.TabIndex = 4;
            this.btnSplit.Text = "Split";
            this.btnSplit.Click += new System.EventHandler(this.btnSplit_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(640, 520);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 28);
            this.btnCancel.TabIndex = 5;
            this.btnCancel.Text = "Abbrechen";
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // openFileDialog1
            // 
            this.openFileDialog1.Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*";
            this.openFileDialog1.Title = "Binaerdatei waehlen";
            // 
            // folderBrowserDialog1
            // 
            this.folderBrowserDialog1.Description = "Ausgabe-Verzeichnis waehlen";
            // 
            // frmBinSplit
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(740, 556);
            this.Controls.Add(this.groupInput);
            this.Controls.Add(this.groupOrder);
            this.Controls.Add(this.groupOutput);
            this.Controls.Add(this.groupResult);
            this.Controls.Add(this.btnSplit);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "frmBinSplit";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "256-KB-Image in LOW- und HIGH-Chip trennen";
            this.groupInput.ResumeLayout(false);
            this.groupInput.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditIn.Properties)).EndInit();
            this.groupOrder.ResumeLayout(false);
            this.groupOrder.PerformLayout();
            this.groupOutput.ResumeLayout(false);
            this.groupOutput.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.buttonEditOutDir.Properties)).EndInit();
            this.groupResult.ResumeLayout(false);
            this.groupResult.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.GroupControl groupInput;
        private System.Windows.Forms.Label labelIn;
        private DevExpress.XtraEditors.ButtonEdit buttonEditIn;
        private DevExpress.XtraEditors.SimpleButton btnBrowseIn;
        private System.Windows.Forms.Label labelInStatus;
        private DevExpress.XtraEditors.GroupControl groupOrder;
        private System.Windows.Forms.RadioButton rdoLowEven;
        private System.Windows.Forms.RadioButton rdoHighEven;
        private DevExpress.XtraEditors.GroupControl groupOutput;
        private System.Windows.Forms.Label labelDir;
        private DevExpress.XtraEditors.ButtonEdit buttonEditOutDir;
        private DevExpress.XtraEditors.SimpleButton btnBrowseDir;
        private System.Windows.Forms.Label labelNames;
        private DevExpress.XtraEditors.GroupControl groupResult;
        private System.Windows.Forms.TextBox memoResult;
        private DevExpress.XtraEditors.SimpleButton btnSplit;
        private DevExpress.XtraEditors.SimpleButton btnCancel;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
    }
}
