namespace DOIMAGE
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            btnGenerateImage = new System.Windows.Forms.Button();
            txtDirectoryPath = new System.Windows.Forms.TextBox();
            lblDirectory = new System.Windows.Forms.Label();
            txtLog = new System.Windows.Forms.RichTextBox();
            btnMoveImage = new System.Windows.Forms.Button();
            grpLog = new System.Windows.Forms.GroupBox();
            label_fileinfo = new System.Windows.Forms.Label();
            progressBar = new System.Windows.Forms.ProgressBar();
            lblProgress = new System.Windows.Forms.Label();
            treeViewFiles = new System.Windows.Forms.TreeView();
            lblFileSystem = new System.Windows.Forms.Label();
            pictureBoxPreview = new System.Windows.Forms.PictureBox();
            btn_delallimage = new System.Windows.Forms.Button();
            radioButton1 = new System.Windows.Forms.RadioButton();
            radioButton2 = new System.Windows.Forms.RadioButton();
            trackBarQuality = new System.Windows.Forms.TrackBar();
            lblQuality = new System.Windows.Forms.Label();
            btnCheckDuplicates = new System.Windows.Forms.Button();
            lab_imagesize = new System.Windows.Forms.Label();
            grpLog.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxPreview).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trackBarQuality).BeginInit();
            SuspendLayout();
            // 
            // btnGenerateImage
            // 
            resources.ApplyResources(btnGenerateImage, "btnGenerateImage");
            btnGenerateImage.Name = "btnGenerateImage";
            btnGenerateImage.UseVisualStyleBackColor = true;
            btnGenerateImage.Click += btnGenerateImage_Click;
            // 
            // txtDirectoryPath
            // 
            txtDirectoryPath.AllowDrop = true;
            resources.ApplyResources(txtDirectoryPath, "txtDirectoryPath");
            txtDirectoryPath.Name = "txtDirectoryPath";
            txtDirectoryPath.TextChanged += txtDirectoryPath_TextChanged;
            txtDirectoryPath.DragDrop += txtDirectoryPath_DragDrop;
            txtDirectoryPath.DragEnter += txtDirectoryPath_DragEnter;
            // 
            // lblDirectory
            // 
            resources.ApplyResources(lblDirectory, "lblDirectory");
            lblDirectory.Name = "lblDirectory";
            lblDirectory.Click += lblDirectory_Click;
            // 
            // txtLog
            // 
            resources.ApplyResources(txtLog, "txtLog");
            txtLog.Name = "txtLog";
            // 
            // btnMoveImage
            // 
            resources.ApplyResources(btnMoveImage, "btnMoveImage");
            btnMoveImage.Name = "btnMoveImage";
            btnMoveImage.UseVisualStyleBackColor = true;
            btnMoveImage.Click += btnMoveImage_Click;
            // 
            // grpLog
            // 
            resources.ApplyResources(grpLog, "grpLog");
            grpLog.Controls.Add(txtLog);
            grpLog.Name = "grpLog";
            grpLog.TabStop = false;
            // 
            // label_fileinfo
            // 
            resources.ApplyResources(label_fileinfo, "label_fileinfo");
            label_fileinfo.Name = "label_fileinfo";
            // 
            // progressBar
            // 
            resources.ApplyResources(progressBar, "progressBar");
            progressBar.Name = "progressBar";
            // 
            // lblProgress
            // 
            resources.ApplyResources(lblProgress, "lblProgress");
            lblProgress.BackColor = System.Drawing.SystemColors.Control;
            lblProgress.Name = "lblProgress";
            // 
            // treeViewFiles
            // 
            resources.ApplyResources(treeViewFiles, "treeViewFiles");
            treeViewFiles.Name = "treeViewFiles";
            treeViewFiles.AfterSelect += treeViewFiles_AfterSelect;
            treeViewFiles.TabIndexChanged += treeViewFiles_TabIndexChanged;
            treeViewFiles.KeyDown += treeViewFiles_KeyDown;
            treeViewFiles.MouseDoubleClick += treeViewFiles_MouseDoubleClick;
            // 
            // lblFileSystem
            // 
            resources.ApplyResources(lblFileSystem, "lblFileSystem");
            lblFileSystem.Name = "lblFileSystem";
            // 
            // pictureBoxPreview
            // 
            resources.ApplyResources(pictureBoxPreview, "pictureBoxPreview");
            pictureBoxPreview.Name = "pictureBoxPreview";
            pictureBoxPreview.TabStop = false;
            // 
            // btn_delallimage
            // 
            resources.ApplyResources(btn_delallimage, "btn_delallimage");
            btn_delallimage.Name = "btn_delallimage";
            btn_delallimage.UseVisualStyleBackColor = true;
            btn_delallimage.Click += btn_delallimage_Click;
            // 
            // radioButton1
            // 
            resources.ApplyResources(radioButton1, "radioButton1");
            radioButton1.Name = "radioButton1";
            radioButton1.TabStop = true;
            radioButton1.UseVisualStyleBackColor = true;
            radioButton1.CheckedChanged += radioButton1_CheckedChanged;
            // 
            // radioButton2
            // 
            resources.ApplyResources(radioButton2, "radioButton2");
            radioButton2.Name = "radioButton2";
            radioButton2.TabStop = true;
            radioButton2.UseVisualStyleBackColor = true;
            radioButton2.CheckedChanged += radioButton2_CheckedChanged;
            // 
            // trackBarQuality
            // 
            resources.ApplyResources(trackBarQuality, "trackBarQuality");
            trackBarQuality.Maximum = 100;
            trackBarQuality.Minimum = 1;
            trackBarQuality.Name = "trackBarQuality";
            trackBarQuality.Value = 36;
            trackBarQuality.ValueChanged += trackBarQuality_ValueChanged;
            // 
            // lblQuality
            // 
            resources.ApplyResources(lblQuality, "lblQuality");
            lblQuality.Name = "lblQuality";
            // 
            // btnCheckDuplicates
            // 
            resources.ApplyResources(btnCheckDuplicates, "btnCheckDuplicates");
            btnCheckDuplicates.Name = "btnCheckDuplicates";
            btnCheckDuplicates.UseVisualStyleBackColor = true;
            btnCheckDuplicates.Click += btnCheckDuplicates_Click;
            // 
            // lab_imagesize
            // 
            resources.ApplyResources(lab_imagesize, "lab_imagesize");
            lab_imagesize.Name = "lab_imagesize";
            // 
            // Form1
            // 
            AllowDrop = true;
            resources.ApplyResources(this, "$this");
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(lab_imagesize);
            Controls.Add(lblQuality);
            Controls.Add(label_fileinfo);
            Controls.Add(radioButton2);
            Controls.Add(radioButton1);
            Controls.Add(trackBarQuality);
            Controls.Add(btnCheckDuplicates);
            Controls.Add(btnMoveImage);
            Controls.Add(lblProgress);
            Controls.Add(pictureBoxPreview);
            Controls.Add(lblFileSystem);
            Controls.Add(treeViewFiles);
            Controls.Add(progressBar);
            Controls.Add(grpLog);
            Controls.Add(txtDirectoryPath);
            Controls.Add(lblDirectory);
            Controls.Add(btn_delallimage);
            Controls.Add(btnGenerateImage);
            Name = "Form1";
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            grpLog.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBoxPreview).EndInit();
            ((System.ComponentModel.ISupportInitialize)trackBarQuality).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Button btnGenerateImage;
        private System.Windows.Forms.TextBox txtDirectoryPath;
        private System.Windows.Forms.Label lblDirectory;
        private System.Windows.Forms.RichTextBox txtLog;
        private System.Windows.Forms.Button btnMoveImage;
        private System.Windows.Forms.GroupBox grpLog;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblProgress;
        private System.Windows.Forms.TreeView treeViewFiles;
        private System.Windows.Forms.Label lblFileSystem;
        private System.Windows.Forms.PictureBox pictureBoxPreview;
        private System.Windows.Forms.Button btn_delallimage;
        private System.Windows.Forms.RadioButton radioButton1;
        private System.Windows.Forms.RadioButton radioButton2;
        private System.Windows.Forms.TrackBar trackBarQuality;
        private System.Windows.Forms.Label lblQuality;
        private System.Windows.Forms.Button btnCheckDuplicates;
        private System.Windows.Forms.Label label_fileinfo;
        private System.Windows.Forms.Label lab_imagesize;
    }
}
