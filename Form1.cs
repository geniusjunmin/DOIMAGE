using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FFmpeg.NET;
using Microsoft.VisualBasic.FileIO;
using System.Text.Json;

namespace DOIMAGE
{
    public partial class Form1 : Form
    {
        private VideoProcessor _videoProcessor;
        private ThumbnailGenerator _thumbnailGenerator;
        private VideoCacheManager _videoCacheManager;
        private TreeViewManager _treeViewManager;
        private UIManager _uiManager;
        private UpdateManager _updateManager;
        private SettingsManager _settingsManager;

        public Form1()
        {
            InitializeComponent();
            _uiManager = new UIManager(this, txtLog, progressBar, lblProgress, pictureBoxPreview, radioButton1);
            _updateManager = new UpdateManager(Application.StartupPath, _uiManager.LogErrorMessage);
            _settingsManager = new SettingsManager(_uiManager.LogErrorMessage);

            string ffmpegPath = Path.Combine(Application.StartupPath, "ffmpeg.exe");
            _videoProcessor = new VideoProcessor(ffmpegPath, _uiManager.LogErrorMessage);
            _thumbnailGenerator = new ThumbnailGenerator(ffmpegPath, Application.StartupPath, _uiManager.LogMessage, _uiManager.LogErrorMessage);
            _videoCacheManager = new VideoCacheManager(_uiManager.LogErrorMessage);
            _treeViewManager = new TreeViewManager(treeViewFiles, txtDirectoryPath, _uiManager.LogMessage, _uiManager.LogErrorMessage);
            
            LoadAndApplySettings();

            _treeViewManager.LoadFileSystem();
            UpdateJpgTotalSize();
        }

        private void LoadAndApplySettings()
        {
            AppSettings settings = _settingsManager.LoadSettings();

            if (!string.IsNullOrEmpty(settings.LastDirectoryPath))
            {
                txtDirectoryPath.Text = settings.LastDirectoryPath;
            }

            trackBarQuality.Value = Math.Max(trackBarQuality.Minimum, Math.Min(trackBarQuality.Maximum, settings.Quality));
            lblQuality.Text = $"质量: {trackBarQuality.Value}%";

            if (settings.Language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
            {
                radioButton1.Checked = true;
            }
            else if (settings.Language.Equals("en-US", StringComparison.OrdinalIgnoreCase))
            {
                radioButton2.Checked = true;
            }
        }

        private async void btnCheckDuplicates_Click(object sender, EventArgs e)
        {
            _videoCacheManager.LoadVideoCache(txtDirectoryPath.Text);
            var videoFiles = FileSystemUtils.GetAllVideoFiles(txtDirectoryPath.Text, _uiManager.LogErrorMessage);
            if (videoFiles.Count == 0)
            {
                _uiManager.LogMessage("没有找到可处理的视频文件进行查重。");
                return;
            }

            var videoInfos = new Dictionary<string, VideoInfo>();
            progressBar.Maximum = videoFiles.Count;
            progressBar.Value = 0;
            lblProgress.Text = "正在分析视频...";

            int processedCount = 0;
            var semaphore = new SemaphoreSlim(10);
            var tasks = new List<Task>();

            Action<string, VideoCache> updateCacheAction = (path, cacheEntry) =>
            {
                _videoCacheManager.UpdateCache(path, cacheEntry);
            };

            foreach (var file in videoFiles)
            {
                await semaphore.WaitAsync();
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var info = await _videoProcessor.GetVideoInfo(file, _videoCacheManager.GetCache(), updateCacheAction);
                        if (info != null && !string.IsNullOrEmpty(info.Path))
                        {
                            lock (videoInfos)
                            {
                                videoInfos[info.Path] = info;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _uiManager.LogErrorMessage($"Error processing file {file} in GetVideoInfo task: {ex.Message}");
                    }
                    finally
                    {
                        int currentProgress = Interlocked.Increment(ref processedCount);
                        if (this.IsHandleCreated && !this.IsDisposed)
                        {
                            try
                            {
                                this.Invoke((Action)(() =>
                                {
                                    if (!progressBar.IsDisposed) progressBar.Value = currentProgress;
                                    if (!lblProgress.IsDisposed) lblProgress.Text = $"收集视频信息: {currentProgress}/{videoFiles.Count}";
                                }));
                            }
                            catch (ObjectDisposedException) { }
                            catch (InvalidOperationException) { }
                        }
                        semaphore.Release();
                    }
                });
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try
                {
                    this.Invoke((Action)(() =>
                    {
                        if (!progressBar.IsDisposed) progressBar.Value = videoFiles.Count;
                        if (!lblProgress.IsDisposed) lblProgress.Text = $"信息收集完成: {processedCount}/{videoFiles.Count}";
                    }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }

            _videoCacheManager.SaveVideoCache();

            var deduplicator = new VideoDeduplicator();
            deduplicator.SetThresholds(0.75, 0.85);
            var duplicates = await deduplicator.FindDuplicatesInGroup(videoInfos.Values.ToList());
            
            progressBar.Value = progressBar.Maximum;
            lblProgress.Text = "比较完成";
            
            DisplayDuplicates(duplicates);
            lblProgress.Text = "检测完成。";
        }

        private void DisplayDuplicates(List<List<string>> duplicates)
        {
            if (duplicates == null || duplicates.Count == 0)
            {
                _uiManager.LogMessage("未检测到重复视频。");
                return;
            }

            treeViewFiles.BeginUpdate();
            _treeViewManager.ClearAllNodeColors();

            StringBuilder message = new StringBuilder();
            message.AppendLine($"找到 {duplicates.Count} 组重复视频：");

            foreach (TreeNode parentNode in treeViewFiles.Nodes)
            {
                _treeViewManager.ReorderDuplicateNodes(parentNode, duplicates);
            }

            for (int groupIndex = 0; groupIndex < duplicates.Count; groupIndex++)
            {
                var group = duplicates[groupIndex];
                Color groupColor = _uiManager.GetNextGroupColor();

                message.AppendLine($"\n组 {groupIndex + 1} ({group.Count} 个文件):");

                foreach (var file in group)
                {
                    message.AppendLine($"- {Path.GetFileName(file)}");
                    TreeNode node = _treeViewManager.FindTreeNodeByPath(file);
                    if (node != null)
                    {
                        node.BackColor = groupColor;
                        node.ForeColor = _uiManager.IsDarkColor(groupColor) ? Color.White : Color.Black;
                        node.EnsureVisible();
                        _uiManager.LogErrorMessage($"已标记重复文件: {file}");
                    }
                    else
                    {
                        _uiManager.LogErrorMessage($"未能找到节点: {file}");
                    }
                }
            }
            treeViewFiles.EndUpdate();
        }

        private async void btnGenerateImage_Click(object sender, EventArgs e)
        {
            txtLog.Text = "";
            if (string.IsNullOrEmpty(txtDirectoryPath.Text))
            {
                MessageBox.Show("目录不能为空!");
                return;
            }

            if (!Directory.Exists(txtDirectoryPath.Text))
            {
                _uiManager.LogMessage("目录不存在。");
                return;
            }

            try
            {
                btnGenerateImage.Enabled = false;
                progressBar.Value = 0;
                lblProgress.Text = "正在生成图片...";

                var videoFiles = FileSystemUtils.GetAllVideoFiles(txtDirectoryPath.Text, _uiManager.LogErrorMessage);
                if (videoFiles.Count == 0)
                {
                    _uiManager.LogMessage("没有找到可处理的视频文件。");
                    btnGenerateImage.Enabled = true;
                    return;
                }

                progressBar.Maximum = videoFiles.Count;
                int processedCount = 0;
                var semaphore = new SemaphoreSlim(10);
                var tasks = new List<Task>();
                int quality = trackBarQuality.Value;
                
                foreach (var file in videoFiles)
                {
                    await semaphore.WaitAsync();
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await _thumbnailGenerator.GetImgCapAsync(file, quality);
                            int currentProgress = Interlocked.Increment(ref processedCount);
                            if (this.IsHandleCreated && !this.IsDisposed)
                            {
                                try
                                {
                                   this.Invoke((Action)(() =>
                                    {
                                        if(!progressBar.IsDisposed) progressBar.Value = currentProgress;
                                        if(!lblProgress.IsDisposed) lblProgress.Text = $"处理进度: {currentProgress}/{videoFiles.Count}";
                                    }));
                                }
                                catch (ObjectDisposedException) { }
                                catch (InvalidOperationException) { }
                            }
                        }
                        catch (Exception ex)
                        {
                            _uiManager.LogErrorMessage($"处理文件{file}时出错: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
                _uiManager.LogMessage("处理完成。");
                _treeViewManager.LoadFileSystem();
                UpdateJpgTotalSize();
            }
            catch (Exception ex)
            {
                _uiManager.LogErrorMessage($"生成图片出错: {ex.Message}");
            }
            finally
            {
                btnGenerateImage.Enabled = true;
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.Invoke((Action)(() =>
                    {
                        if (!progressBar.IsDisposed) progressBar.Value = progressBar.Maximum;
                        if (!lblProgress.IsDisposed) lblProgress.Text = "图片生成完成";
                    }));
                }
            }
        }

        private void txtDirectoryPath_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                txtDirectoryPath.Text = files[0];
            }
        }

        private void txtDirectoryPath_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void txtDirectoryPath_TextChanged(object sender, EventArgs e)
        {
            _treeViewManager.LoadFileSystem();
            UpdateJpgTotalSize();
        }

        private async void btnMoveImage_Click(object sender, EventArgs e)
        {
            if (treeViewFiles.SelectedNode != null)
            {
                string selectedNodeFullPath = treeViewFiles.SelectedNode.FullPath;
                if (string.IsNullOrEmpty(txtDirectoryPath.Text) || selectedNodeFullPath.IndexOf('\\') < 0)
                {
                    _uiManager.LogErrorMessage("无法确定选中文件的有效路径。");
                    return;
                }
                string selectedFilePath = Path.Combine(txtDirectoryPath.Text, selectedNodeFullPath.Remove(0, selectedNodeFullPath.IndexOf('\\') + 1));
                string jpgFile = selectedFilePath + ".jpg";

                if (File.Exists(jpgFile))
                {
                    try { pictureBoxPreview.Image?.Dispose(); } catch { }
                    try
                    {
                        File.Delete(jpgFile);
                        _uiManager.SetNullImage();
                        treeViewFiles.Focus();
                    }
                    catch (Exception ex)
                    { _uiManager.LogErrorMessage($"删除时出错: {ex.Message}"); }
                    
                    await _thumbnailGenerator.GetImgCapAsync(selectedFilePath, trackBarQuality.Value);
                    Image? newImage = FileSystemUtils.LoadImageWithoutLock(jpgFile, _uiManager.LogErrorMessage);
                    if (newImage != null)
                    {
                        pictureBoxPreview.Image = newImage;
                        _uiManager.LogMessage($"重新生成并加载成功: {jpgFile}");
                    }
                    else
                    {
                         _uiManager.LogErrorMessage($"重新生成成功，但加载图片失败: {jpgFile}");
                    }
                }
                else
                {
                    if (!Directory.Exists(selectedFilePath) && FileSystemUtils.IsVideoFile(selectedFilePath))
                    {
                        await _thumbnailGenerator.GetImgCapAsync(selectedFilePath, trackBarQuality.Value);
                        Image? newImage = FileSystemUtils.LoadImageWithoutLock(jpgFile, _uiManager.LogErrorMessage);
                        if (newImage != null)
                        {
                            pictureBoxPreview.Image = newImage;
                            _uiManager.LogMessage($"生成并加载成功: {jpgFile}");
                        }
                        else
                        {
                            _uiManager.LogErrorMessage($"生成成功，但加载图片失败: {jpgFile}");
                        }
                    }
                    else if (Directory.Exists(selectedFilePath))
                    { _uiManager.LogMessage("不能对文件夹执行此操作。"); }
                    else
                    { _uiManager.LogMessage($"原始文件不存在或不是支持的视频文件: {selectedFilePath}"); }
                }
            }
            else
            { MessageBox.Show("请选择一个文件。"); }
        }

        private void SupportLabel_Click(object sender, EventArgs e)
        {
            showimg();
        }

        #region 技术支持与自动更新

        public void showimg()
        {
            try 
            {
                string url = "http://www.junhoo.net";
                if (radioButton2.Checked) 
                {
                    url = "http://www.junhoo.net/en";
                }
                
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _uiManager.LogErrorMessage($"打开技术支持网站失败: {ex.Message}");
                MessageBox.Show("无法打开技术支持网站，请检查网络连接", 
                    "错误", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Error);
            }
        }
        #endregion

        Label supportLabel = new Label();

        private void trackBarQuality_ValueChanged(object sender, EventArgs e)
        {
            lblQuality.Text = $"质量: {trackBarQuality.Value}%";
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            lblQuality.Text = $"质量: {trackBarQuality.Value}%";
            supportLabel.Text = "技术支持: JunHoo";
            supportLabel.AutoSize = true;
            supportLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            supportLabel.Location = new System.Drawing.Point(this.ClientSize.Width - supportLabel.Width - 10, this.ClientSize.Height - supportLabel.Height - 10);
            supportLabel.Click += SupportLabel_Click;
            this.Controls.Add(supportLabel);
            supportLabel.BringToFront();
            this.Resize += (s, evt) =>
            {
                supportLabel.Location = new System.Drawing.Point(this.ClientSize.Width - supportLabel.Width - 10, this.ClientSize.Height - supportLabel.Height - 10);
            };

            if (File.Exists("lastDirectoryPath.txt"))
            {
                txtDirectoryPath.Text = File.ReadAllText("lastDirectoryPath.txt");
            }
            pictureBoxPreview.SizeMode = PictureBoxSizeMode.StretchImage;
            treeViewFiles.DrawMode = TreeViewDrawMode.OwnerDrawText;
            treeViewFiles.DrawNode += TreeViewFiles_DrawNode;
            lblProgress.BackColor = Color.Transparent;
            lblProgress.BringToFront();

            if (File.Exists("settings.ini"))
            {
                try
                {
                    var settingsLines = File.ReadAllLines("settings.ini");
                    if (settingsLines.Length == 0)
                    {
                        var culture = System.Globalization.CultureInfo.CurrentCulture.Name;
                        if (culture == "zh-CN" && !radioButton1.Checked) radioButton1.Checked = true;
                        else if (culture != "zh-CN" && !radioButton2.Checked) radioButton2.Checked = true;
                    }
                    foreach (var line in settingsLines)
                    {
                        if (line.StartsWith("Quality="))
                        {
                            int quality = int.Parse(line.Substring("Quality=".Length));
                            trackBarQuality.Value = quality;
                            lblQuality.Text = $"质量: {quality}%";
                        }
                        else if (line.StartsWith("Language="))
                        {
                            string lang = line.Substring("Language=".Length);
                            radioButton1.Checked = (lang == "zh-CN");
                            radioButton2.Checked = (lang == "en-US");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _uiManager.LogErrorMessage($"读取设置时出错: {ex.Message}");
                }
            }
            else
            {
                 var culture = System.Globalization.CultureInfo.CurrentCulture.Name;
                 if (culture == "zh-CN") radioButton1.Checked = true;
                 else radioButton2.Checked = true;
            }
      
            if(treeViewFiles.SelectedNode == null) _uiManager.SetNullImage();

            await _updateManager.CheckForUpdatesAsync();
        }

        private void TreeViewFiles_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            bool isSelected = e.Node == treeViewFiles.SelectedNode;

            Color backColor;
            if (e.Node.BackColor != Color.Empty)
            {
                backColor = e.Node.BackColor;
            }
            else if (isSelected)
            {
                backColor = treeViewFiles.Focused ? Color.CornflowerBlue : Color.LightBlue;
            }
            else
            {
                backColor = treeViewFiles.BackColor;
            }

            Color foreColor;
            if (isSelected)
            {
                foreColor = Color.White;
            }
            else if (e.Node.ForeColor != Color.Empty)
            {
                foreColor = e.Node.ForeColor;
            }
            else
            {
                foreColor = treeViewFiles.ForeColor;
            }

            Font nodeFont = isSelected ? new Font(treeViewFiles.Font, FontStyle.Bold) : treeViewFiles.Font;

            using (Brush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            TextRenderer.DrawText(e.Graphics, e.Node.Text, nodeFont, e.Bounds, foreColor, backColor);

            e.DrawDefault = false;
        }

        private void treeViewFiles_TabIndexChanged(object sender, EventArgs e)
        {

        }

        private void treeViewFiles_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node == null || string.IsNullOrEmpty(txtDirectoryPath.Text) || e.Node.FullPath.IndexOf('\\') < 0) 
            {
                _uiManager.SetNullImage();
                label_fileinfo.Text = "请选择一个文件或文件夹";
                return;
            }
            string selectedFileOrDirPath = Path.Combine(txtDirectoryPath.Text, e.Node.FullPath.Remove(0, e.Node.FullPath.IndexOf('\\') + 1));

            try
            {
                if (File.Exists(selectedFileOrDirPath))
                {
                    var fileInfo = new FileInfo(selectedFileOrDirPath);
                    label_fileinfo.Text = $"文件名: {fileInfo.Name}\n大小: {_uiManager.FormatFileSize(fileInfo.Length)}\n修改时间: {fileInfo.LastWriteTime}";

                    if (Path.GetExtension(selectedFileOrDirPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        Image? img = FileSystemUtils.LoadImageWithoutLock(selectedFileOrDirPath, _uiManager.LogErrorMessage);
                        if (img != null) pictureBoxPreview.Image = img;
                        else _uiManager.SetNullImage();
                    }
                    else
                    {
                        string jpgFile = selectedFileOrDirPath + ".jpg";
                        if (File.Exists(jpgFile))
                        {
                            Image? img = FileSystemUtils.LoadImageWithoutLock(jpgFile, _uiManager.LogErrorMessage);
                            if (img != null) pictureBoxPreview.Image = img;
                            else _uiManager.SetNullImage();
                        }
                        else
                        {
                            _uiManager.LogMessage($"相关的jpg文件未找到: {jpgFile}");
                            _uiManager.SetNullImage();
                        }
                    }
                }
                else if (Directory.Exists(selectedFileOrDirPath))
                {
                    var dirInfo = new DirectoryInfo(selectedFileOrDirPath);
                    label_fileinfo.Text = $"文件夹名: {dirInfo.Name}\n修改时间: {dirInfo.LastWriteTime}";
                    _uiManager.SetNullImage();
                }
                else
                {
                     _uiManager.SetNullImage();
                     label_fileinfo.Text = "选择的项不存在。";
                }
            }
            catch (Exception ex)
            {
                _uiManager.LogErrorMessage($"显示预览/信息时出错: {selectedFileOrDirPath}: {ex.Message}");
                _uiManager.SetNullImage();
            }
        }

        private async void btn_delallimage_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtDirectoryPath.Text))
            {
                MessageBox.Show("目录不能为空!");
                return;
            }
            var result = MessageBox.Show("确定要删除所有已经生成的九宫格图吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                await _thumbnailGenerator.ProcessDirectoryAsync(txtDirectoryPath.Text, _uiManager.UpdateThumbnailProgress, true);
                _uiManager.LogMessage("处理完成。");
                _treeViewManager.LoadFileSystem();
                UpdateJpgTotalSize();
            }
        }

        private void UpdateJpgTotalSize()
        {
            try
            {
                if (!Directory.Exists(txtDirectoryPath.Text))
                {
                    _uiManager.LogErrorMessage("目录不存在，无法计算jpg文件大小"); 
                    lab_imagesize.Text = "N/A";
                    return;
                }

                long totalBytes = 0;
                foreach (var file in Directory.EnumerateFiles(txtDirectoryPath.Text, "*.jpg", System.IO.SearchOption.AllDirectories))
                {
                    totalBytes += new FileInfo(file).Length;
                }
                string formattedSize = _uiManager.FormatFileSize(totalBytes);
                _uiManager.LogMessage($"当前目录jpg文件总大小: {formattedSize}");
                lab_imagesize.Text = formattedSize;
            }
            catch (Exception ex)
            {
                _uiManager.LogErrorMessage($"计算jpg文件总大小时出错: {ex.Message}");
                lab_imagesize.Text = "Error";
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _videoCacheManager.SaveVideoCache();
            File.WriteAllText("lastDirectoryPath.txt", txtDirectoryPath.Text);
            
            var settings = new StringBuilder();
            settings.AppendLine($"Quality={trackBarQuality.Value}");
            settings.AppendLine($"Language={(radioButton1.Checked ? "zh-CN" : "en-US")}");
            File.WriteAllText("settings.ini", settings.ToString());
        }

        private void treeViewFiles_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (treeViewFiles.SelectedNode == null || string.IsNullOrEmpty(txtDirectoryPath.Text) || treeViewFiles.SelectedNode.FullPath.IndexOf('\\') < 0) return;
            string selectedFileOrDir = Path.Combine(txtDirectoryPath.Text, treeViewFiles.SelectedNode.FullPath.Remove(0, treeViewFiles.SelectedNode.FullPath.IndexOf('\\') + 1));

            if (File.Exists(selectedFileOrDir))
            {
                try
                {
                    var process = new System.Diagnostics.Process();
                    process.StartInfo = new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = selectedFileOrDir,
                        UseShellExecute = true
                    };
                    process.Start();
                }
                catch (Exception ex)
                {
                    _uiManager.LogErrorMessage("打开文件失败！" + selectedFileOrDir + " 失败原因：" + ex.Message);
                }
            }
            else if (Directory.Exists(selectedFileOrDir))
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", selectedFileOrDir);
                }
                catch (Exception ex)
                {
                     _uiManager.LogErrorMessage("打开文件夹失败！" + selectedFileOrDir + " 失败原因：" + ex.Message);
                }
            }
        }

        private void treeViewFiles_KeyDown(object sender, KeyEventArgs e)
        {
            if (treeViewFiles.SelectedNode == null || string.IsNullOrEmpty(txtDirectoryPath.Text) || treeViewFiles.SelectedNode.FullPath.IndexOf('\\') < 0) return;
            string selectedFile = Path.Combine(txtDirectoryPath.Text, treeViewFiles.SelectedNode.FullPath.Remove(0, treeViewFiles.SelectedNode.FullPath.IndexOf('\\') + 1));
            
            if (!Directory.Exists(selectedFile))
            {
                if (e.KeyCode == Keys.Enter)
                {
                    treeViewFiles_MouseDoubleClick(sender, null);
                }
                if (e.KeyCode == Keys.Delete)
                {
                    var result = MessageBox.Show("确定要将文件移至回收站吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        try
                        {
                            try { if (File.Exists(selectedFile + ".jpg")) File.Delete(selectedFile + ".jpg"); } catch { }
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(selectedFile, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                            _uiManager.LogMessage("文件已移至回收站！" + selectedFile);
                            _treeViewManager.LoadFileSystem();
                            UpdateJpgTotalSize();
                        }
                        catch (Exception ex)
                        {
                            _uiManager.LogErrorMessage($"删除文件时出错: {ex.Message} 尝试移动到根目录删除！");
                            try
                            {
                                string rootDirectory = Path.GetPathRoot(selectedFile) ?? throw new InvalidOperationException("Cannot get root directory for selected file.");
                                string fileName = Path.GetFileName(selectedFile);
                                string tempFilePath = Path.Combine(rootDirectory, fileName);
                                
                                if (File.Exists(tempFilePath) && !string.Equals(tempFilePath, selectedFile, StringComparison.OrdinalIgnoreCase))
                                {
                                   tempFilePath = Path.Combine(rootDirectory, Guid.NewGuid().ToString() + "_" + fileName);
                                }
                                else if (string.Equals(tempFilePath, selectedFile, StringComparison.OrdinalIgnoreCase))
                                {
                                     _uiManager.LogErrorMessage($"无法将文件移动到其当前位置以进行删除。手动删除: {selectedFile}");
                                     return;
                                }

                                File.Move(selectedFile, tempFilePath);
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(tempFilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                                _uiManager.LogMessage("文件已通过移动到根目录后移至回收站！" + selectedFile);
                                _treeViewManager.LoadFileSystem();
                                UpdateJpgTotalSize();
                            }
                            catch (Exception exMove)
                            {
                                _uiManager.LogErrorMessage($"移动到根目录删除文件时出错: {exMove.Message} 请手动删除！");
                            }
                        }
                    }
                }
            }
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            supportLabel.Text = "技术支持: JunHoo";
            LanguageManager.ChangeLanguage(this, LanguageManager.LangKeys.zh_CN);
            return;
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            supportLabel.Text = "Technical support";
            LanguageManager.ChangeLanguage(this, LanguageManager.LangKeys.en_US);
            return;
        }

        private void lblDirectory_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog path = new FolderBrowserDialog();
            path.ShowDialog();
            string txtPath = path.SelectedPath;
            txtDirectoryPath.Text = txtPath;
        }
    }
}

