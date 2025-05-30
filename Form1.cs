using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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

        public Form1()
        {
            InitializeComponent();
            LoadFileSystem();
            string ffmpegPath = Path.Combine(Application.StartupPath, "ffmpeg.exe");
            _videoProcessor = new VideoProcessor(ffmpegPath, LogerrorMessage);
            _thumbnailGenerator = new ThumbnailGenerator(ffmpegPath, Application.StartupPath, LogMessage, LogerrorMessage);
        }

        /// <summary>
        /// 加载并刷新文件系统树形视图
        /// </summary>
        private void LoadFileSystem()
        {
            try
            {
                // 保存当前选中的节点路径（用于恢复选中状态）
                string? selectedNodePath = treeViewFiles.SelectedNode?.FullPath;

                // 清空现有树节点
                treeViewFiles.BeginUpdate(); // 开始批量更新，避免闪烁
                treeViewFiles.Nodes.Clear();

                // 检查目录是否存在
                if (Directory.Exists(txtDirectoryPath.Text))
                {
                    // 创建根目录节点
                    var rootDirectoryInfo = new DirectoryInfo(txtDirectoryPath.Text);
                    TreeNode rootNode = CreateDirectoryNode(rootDirectoryInfo);

                    // 添加根节点到树视图
                    treeViewFiles.Nodes.Add(rootNode);

                    // 自动展开根节点
                    rootNode.Expand();
                }
                else
                {
                    txtLog.AppendText($"目录不存在: {txtDirectoryPath.Text}\n");
                }

                // 尝试恢复之前选中的节点
                if (!string.IsNullOrEmpty(selectedNodePath))
                {
                    TreeNode? nodeToSelect = FindNodeByPath(treeViewFiles.Nodes, selectedNodePath);
                    if (nodeToSelect != null)
                    {
                        treeViewFiles.SelectedNode = nodeToSelect;
                        nodeToSelect.Expand();          // 展开选中节点
                        nodeToSelect.EnsureVisible();   // 确保节点可见

                        // 触发选择事件以更新文件预览
                        treeViewFiles_AfterSelect(this,
                            new TreeViewEventArgs(nodeToSelect));
                    }
                }
                UpdateJpgTotalSize();

            }
            catch (UnauthorizedAccessException ex)
            {
                txtLog.AppendText($"无权限访问目录: {ex.Message}\n");
            }
            catch (Exception ex)
            {
                txtLog.AppendText($"加载文件系统失败: {ex.Message}\n");
            }
            finally
            {
                treeViewFiles.EndUpdate(); // 结束批量更新
            }
        }
        private List<string> GetAllVideoFiles(string directoryPath)
        {
            var videoFiles = new List<string>();
            var extensions = new[] { "", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };
            try
            {
                foreach (var file in Directory.EnumerateFiles(directoryPath, "*.*", System.IO.SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (extensions.Contains(ext))
                    {
                        videoFiles.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                LogerrorMessage($"遍历目录时出错: {ex.Message}");
            }
            return videoFiles;
        }

        private async void btnCheckDuplicates_Click(object sender, EventArgs e)
        {
            LoadVideoCache(txtDirectoryPath.Text);
            var videoFiles = GetAllVideoFiles(txtDirectoryPath.Text);
            if (videoFiles.Count == 0)
            {
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
                videoCache[path] = cacheEntry;
            };

            foreach (var file in videoFiles)
            {
                await semaphore.WaitAsync();
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var info = await _videoProcessor.GetVideoInfo(file, videoCache, updateCacheAction);
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
                        LogerrorMessage($"Error processing file {file} in GetVideoInfo task: {ex.Message}");
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

            SaveVideoCache();

            // 使用VideoDeduplicator进行重复检测
            var deduplicator = new VideoDeduplicator();
            deduplicator.SetThresholds(0.75, 0.85);

            // 直接比较所有视频，不再预过滤
            var duplicates = await deduplicator.FindDuplicatesInGroup(videoInfos.Values.ToList());
            
            // 更新进度显示
            progressBar.Value = progressBar.Maximum;
            lblProgress.Text = "比较完成";
            
            DisplayDuplicates(duplicates);
            lblProgress.Text = "检测完成。";
        }


        private void DisplayDuplicates(List<List<string>> duplicates)
        {
            if (duplicates == null || duplicates.Count == 0)
            {
                return;
            }

            treeViewFiles.BeginUpdate();

            // 首先清除所有节点的颜色
            ClearAllNodeColors(treeViewFiles.Nodes);

            // 创建一个颜色生成器
            var colorGenerator = new ColorGenerator();

            StringBuilder message = new StringBuilder();
            message.AppendLine($"找到 {duplicates.Count} 组重复视频：");

            // 收集所有重复文件路径
            var allDuplicatePaths = duplicates.SelectMany(g => g).ToList();

            // 遍历所有节点并重新排列
            foreach (TreeNode parentNode in treeViewFiles.Nodes)
            {
                ReorderDuplicateNodes(parentNode, duplicates);
            }

            // 标记重复文件
            for (int groupIndex = 0; groupIndex < duplicates.Count; groupIndex++)
            {
                var group = duplicates[groupIndex];
                Color groupColor = colorGenerator.GetNextColor();

                message.AppendLine($"\n组 {groupIndex + 1} ({group.Count} 个文件):");

                foreach (var file in group)
                {
                    message.AppendLine($"- {Path.GetFileName(file)}");
                    TreeNode node = FindTreeNodeByPath(file);
                    if (node != null)
                    {
                        node.BackColor = groupColor;
                        node.ForeColor = IsDarkColor(groupColor) ? Color.White : Color.Black;
                        node.EnsureVisible();
                        LogerrorMessage($"已标记重复文件: {file}");
                    }
                    else
                    {
                        LogerrorMessage($"未能找到节点: {file}");
                    }
                }
            }

            treeViewFiles.EndUpdate();
        }

        private void ReorderDuplicateNodes(TreeNode parentNode, List<List<string>> duplicateGroups)
        {
            if (parentNode.Nodes.Count == 0) return;

            // 收集所有重复路径
            var allDuplicatePaths = duplicateGroups.SelectMany(g => g).ToList();

            // 分离重复和非重复节点
            var normalNodes = new List<TreeNode>();
            var groupedDuplicateNodes = new List<List<TreeNode>>();

            // 初始化每个重复组的节点列表
            foreach (var group in duplicateGroups)
            {
                groupedDuplicateNodes.Add(new List<TreeNode>());
            }

            foreach (TreeNode node in parentNode.Nodes)
            {
                string fullPath = Path.Combine(txtDirectoryPath.Text, node.FullPath.Remove(0, node.FullPath.IndexOf('\\') + 1));
                
                if (allDuplicatePaths.Contains(fullPath))
                {
                    // 找到节点所属的重复组
                    for (int i = 0; i < duplicateGroups.Count; i++)
                    {
                        if (duplicateGroups[i].Contains(fullPath))
                        {
                            groupedDuplicateNodes[i].Add(node);
                            break;
                        }
                    }
                }
                else
                {
                    normalNodes.Add(node);
                    
                    // 递归处理子目录
                    if (node.Nodes.Count > 0)
                    {
                        ReorderDuplicateNodes(node, duplicateGroups);
                    }
                }
            }

            // 清空原节点集合
            parentNode.Nodes.Clear();

            // 先添加非重复节点(保持原顺序)
            foreach (var node in normalNodes)
            {
                parentNode.Nodes.Add(node);
            }

            // 然后按组添加重复节点，保持组内顺序
            foreach (var group in groupedDuplicateNodes)
            {
                foreach (var node in group)
                {
                    parentNode.Nodes.Add(node);
                }
            }
        }

        // 添加颜色生成器类
        private class ColorGenerator
        {
            private readonly List<Color> predefinedColors;
            private int currentIndex = 0;
            private Random random = new Random();

            public ColorGenerator()
            {
                // 预定义一组柔和的颜色
                predefinedColors = new List<Color>
                {
                    Color.FromArgb(255, 182, 193),  // 浅粉红
                    Color.FromArgb(152, 251, 152),  // 浅绿
                    Color.FromArgb(135, 206, 250),  // 浅天蓝
                    Color.FromArgb(255, 218, 185),  // 桃色
                    Color.FromArgb(221, 160, 221),  // 梅红色
                    Color.FromArgb(255, 255, 176),  // 浅黄
                    Color.FromArgb(176, 224, 230),  // 粉蓝
                    Color.FromArgb(255, 160, 122),  // 浅鲑鱼色
                    Color.FromArgb(216, 191, 216),  // 蓟色
                    Color.FromArgb(240, 230, 140),  // 卡其布
                    Color.FromArgb(255, 192, 203),  // 粉红
                    Color.FromArgb(173, 216, 230),  // 浅蓝
                    Color.FromArgb(144, 238, 144),  // 浅绿
                    Color.FromArgb(255, 222, 173),  // 纳瓦白
                    Color.FromArgb(250, 250, 210),  // 浅黄绿
                    Color.FromArgb(211, 211, 211),  // 浅灰
                    Color.FromArgb(230, 230, 250),  // 薰衣草白
                    Color.FromArgb(255, 228, 225),  // 薄雾玫瑰
                    Color.FromArgb(245, 245, 220),  // 米色
                    Color.FromArgb(240, 248, 255)   // 爱丽丝蓝
                };
            }

            public Color GetNextColor()
            {
                if (currentIndex < predefinedColors.Count)
                {
                    return predefinedColors[currentIndex++];
                }

                // 如果预定义颜色用完，生成新的柔和颜色
                return GeneratePastelColor();
            }

            private Color GeneratePastelColor()
            {
                // 生成柔和的颜色
                int red = random.Next(180, 255);
                int green = random.Next(180, 255);
                int blue = random.Next(180, 255);
                return Color.FromArgb(red, green, blue);
            }
        }

        // 添加判断颜色深浅的方法
        private bool IsDarkColor(Color color)
        {
            // 使用 YIQ 公式计算颜色的亮度
            double brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000.0;
            return brightness < 128;
        }

        // 添加清除颜色的辅助方法
        private void ClearAllNodeColors(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                node.BackColor = Color.Empty;
                node.ForeColor = Color.Black;
                ClearAllNodeColors(node.Nodes);
            }
        }

        private TreeNode? FindNodeByPath(TreeNodeCollection nodes, string path)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.FullPath == path)
                {
                    return node;
                }

                TreeNode? foundNode = FindNodeByPath(node.Nodes, path);
                if (foundNode != null)
                {
                    return foundNode;
                }
            }

            return null;
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
                LogMessage("目录不存在。");
                return;
            }

            try
            {
                btnGenerateImage.Enabled = false;
                progressBar.Value = 0;
                lblProgress.Text = "正在生成图片...";

                var videoFiles = GetAllVideoFiles(txtDirectoryPath.Text);
                if (videoFiles.Count == 0)
                {
                    LogMessage("没有找到可处理的视频文件。");
                    return;
                }

                progressBar.Maximum = videoFiles.Count;
                int processedCount = 0;
                var semaphore = new SemaphoreSlim(10); // 控制最大并发数为10
                var tasks = new List<Task>();

                // 在主线程获取质量值
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
                            this.Invoke((Action)(() =>
                            {
                                progressBar.Value = currentProgress;
                                lblProgress.Text = $"处理进度: {currentProgress}/{videoFiles.Count}";
                            }));
                        }
                        catch (Exception ex)
                        {
                            LogerrorMessage($"处理文件{file}时出错: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
                LogMessage("处理完成。");
                LoadFileSystem(); // 重新加载文件系统，实时更新
            }
            catch (Exception ex)
            {
                LogerrorMessage($"生成图片出错: {ex.Message}");
            }
            finally
            {
                btnGenerateImage.Enabled = true;
            }
        }

        private Task UpdateThumbnailProgress(int processed, int total)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try
                {
                    return Task.Run(() => this.Invoke((Action)(() =>
                    {
                        progressBar.Minimum = 0;
                        progressBar.Maximum = total;
                        progressBar.Value = Math.Min(processed, total);
                        if (!lblProgress.IsDisposed) lblProgress.Text = $"缩略图进度: {processed}/{total}";
                        lblProgress.Refresh();
                    })));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
            return Task.CompletedTask;
        }

        private void LogMessage(string message)
        {
            //txtLog.Invoke((MethodInvoker)delegate
            //{
            //    txtLog.AppendText($"{DateTime.Now}: {message}\n");
            //    txtLog.ScrollToCaret();
            //});
        }

        private void LogerrorMessage(string message)
        {
            txtLog.Invoke((MethodInvoker)delegate
            {
                txtLog.AppendText($"{DateTime.Now}: {message}\n");
                txtLog.ScrollToCaret();
            });
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
            LoadFileSystem();
        }

        private async void btnMoveImage_Click(object sender, EventArgs e)
        {
            if (treeViewFiles.SelectedNode != null)
            {
                string selectedFilePath = Path.Combine(txtDirectoryPath.Text, treeViewFiles.SelectedNode.FullPath.Remove(0, treeViewFiles.SelectedNode.FullPath.IndexOf('\\') + 1));

                string jpgFile = (selectedFilePath + ".jpg");

                if (File.Exists(jpgFile))
                {
                    try
                    {
                        pictureBoxPreview.Image.Dispose();
                    }
                    catch { }

                    try
                    {
                        File.Delete(jpgFile);
                        setnullimage();
                        treeViewFiles.Focus();
                    }
                    catch (Exception ex)
                    {
                        LogerrorMessage($"删除时出错: {ex.Message}");
                    }
                    await _thumbnailGenerator.GetImgCapAsync(selectedFilePath, trackBarQuality.Value);
                    try
                    {
                        LoadImageWithoutLock(jpgFile);
                        LogerrorMessage($"重新生成成功: {jpgFile}");
                    }
                    catch
                    {

                    }
                }
                else
                {
                    if (!Directory.Exists(selectedFilePath))
                    {
                        await _thumbnailGenerator.GetImgCapAsync(selectedFilePath, trackBarQuality.Value);
                        try
                        {
                            LoadImageWithoutLock(jpgFile);
                            LogerrorMessage($"重新生成成功: {jpgFile}");
                        }
                        catch
                        {
                        }
                    }
                }
            }
            else
            {
                MessageBox.Show("请选择一个文件。");
            }
        }

        public string getbase64str(string path)
        {
            // 确保文件存在
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The specified file does not exist.");
            }

            using (var fs = new FileStream(path, FileMode.Open))
            {
                byte[] buffer = new byte[fs.Length];
                fs.Read(buffer, 0, buffer.Length);

                // 将字节数组转换为Base64字符串
                return Convert.ToBase64String(buffer);
            }
        }

        private void LoadImageWithoutLock(string selectedFile)
        {
            if (File.Exists(selectedFile))
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(selectedFile)))
                {
                    Image image = Image.FromStream(stream);
                    pictureBoxPreview.SizeMode = PictureBoxSizeMode.Zoom;
                    pictureBoxPreview.Image = new Bitmap(image);
                }
            }
        }

        private void SupportLabel_Click(object sender, EventArgs e)
        {
            showimg();
        }

        #region 技术支持与自动更新

        private static readonly HttpClient httpClient = new HttpClient();

        private const string remoteUrl = "http://rd.junhoo.net:55667/DOIMAGE.txt";
        private const string localFilePath = "updcfg";
        private const string updateExePath = "updateapp.exe"; // 更新程序路径

        public async Task GetUpdateCfgAsync()
        {
            try
            {
                // 异步读取远程文本文件
                string remoteContent = await httpClient.GetStringAsync(remoteUrl);

                // 异步读取本地配置文件
                string localContent = File.Exists(localFilePath) ? await File.ReadAllTextAsync(localFilePath) : string.Empty;

                // 比较内容并更新
                if (remoteContent != localContent)
                {
                    // 异步写入本地配置文件
                    await File.WriteAllTextAsync(localFilePath, remoteContent);

                    // 运行更新程序
                    freeupdateexe();

                    System.Diagnostics.Process.Start(updateExePath);
                }
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"HTTP 请求错误: {httpEx.Message}");
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"文件操作错误: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"未知错误: {ex.Message}");
            }
        }

        public void freeupdateexe()
        {
            string exePath = Path.Combine(Application.StartupPath, "updateapp.exe");
            // 判断 updateapp.exe 是否存在
            if (!File.Exists(exePath))
            {
                try
                {
                    // 从嵌入资源中提取 updateapp.zip
                    string zipPath = Path.Combine(Application.StartupPath, "updateapp.zip");
                    using (Stream resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("DOIMAGE.updateapp.zip"))
                    {
                        if (resource == null)
                        {
                            MessageBox.Show("无法找到嵌入的 updateapp.zip 资源", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        using (FileStream fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                        {
                            resource.CopyTo(fileStream);
                        }
                    }
                    // 使用 FileStream 确保文件在解压时不被占用
                    using (FileStream zipToOpen = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
                    using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read))
                    {
                        archive.ExtractToDirectory(Application.StartupPath);
                    }

                    File.Delete(zipPath);

                }
                catch (Exception ex)
                {

                }
            }
        }

        public void showimg()
        {
            try 
            {
                string url = "http://www.junhoo.net";
                if (radioButton2.Checked) 
                {
                    url = "http://www.junhoo.net/en"; // 英文版网站
                }
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogerrorMessage($"打开技术支持网站失败: {ex.Message}");
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
            // 初始化质量滑块显示
            lblQuality.Text = $"质量: {trackBarQuality.Value}%";
            // 创建 Label
            supportLabel.Text = "技术支持: JunHoo";
            supportLabel.AutoSize = true;

            // 设置 Label 固定到窗口的右下角
            supportLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            supportLabel.Location = new System.Drawing.Point(this.ClientSize.Width - supportLabel.Width - 10, this.ClientSize.Height - supportLabel.Height);
            supportLabel.Click += SupportLabel_Click;

            // 将 Label 添加到窗口
            this.Controls.Add(supportLabel);

            // 确保 Label 在最顶层
            supportLabel.BringToFront();

            // 窗口大小改变时，更新 Label 的位置
            this.Resize += (sender, e) =>
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
            lblProgress.BackColor = Color.Transparent; // 设置 Label 的背景为透明
            lblProgress.BringToFront();

            // 恢复控件状态
            if (File.Exists("settings.ini"))
            {
                try
                {
                    var settings = File.ReadAllLines("settings.ini");
                    if (settings.Length == 0)
                    {
                        var culture = System.Globalization.CultureInfo.CurrentCulture.Name;

                        if (culture == "zh-CN" && !radioButton1.Checked)
                        {
                            radioButton1.Checked = true;
                        }
                        else if (culture != "zh-CN" && !radioButton2.Checked)
                        {
                            radioButton2.Checked = true;
                        }

                    }
                    foreach (var line in settings)
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
                    LogerrorMessage($"读取设置时出错: {ex.Message}");
                }
            }
      
            await GetUpdateCfgAsync();


        }

        private void TreeViewFiles_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            // 检查节点是否被选中
            bool isSelected = e.Node == treeViewFiles.SelectedNode;

            // 设置背景色
            Color backColor;
            if (e.Node.BackColor != Color.Empty)
            {
                // 使用节点的自定义背景色（用于显示重复文件）
                backColor = e.Node.BackColor;
            }
            else if (isSelected)
            {
                // 当 TreeView 具有焦点时，使用亮蓝色作为背景色
                backColor = treeViewFiles.Focused ? Color.CornflowerBlue : Color.LightBlue;
            }
            else
            {
                backColor = treeViewFiles.BackColor;
            }

            // 设置文本颜色
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

            // 使用加粗字体显示选中节点
            Font nodeFont = isSelected ? new Font(treeViewFiles.Font, FontStyle.Bold) : treeViewFiles.Font;

            // 填充背景
            using (Brush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // 绘制文本
            TextRenderer.DrawText(e.Graphics, e.Node.Text, nodeFont, e.Bounds, foreColor, backColor);

            // 如果节点有子节点，递归绘制下级节点
            e.DrawDefault = false;
        }

        private void treeViewFiles_TabIndexChanged(object sender, EventArgs e)
        {

        }

        public void setnullimage()
        {
            try
            {
                // 创建一个400x300的空白位图
                Bitmap image = new Bitmap(400, 300);
                using (Graphics g = Graphics.FromImage(image))
                {
                    // 设置背景颜色为淡灰色
                    g.Clear(Color.LightGray);

                    // 设置抗锯齿和高质量渲染
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                    // 绘制边框
                    using (Pen pen = new Pen(Color.DarkGray, 2))
                    {
                        g.DrawRectangle(pen, 10, 10, image.Width - 20, image.Height - 20);
                    }

                    // 设置字体和颜色
                    Font font = new Font("微软雅黑", 16, FontStyle.Bold);
                    SolidBrush textBrush = new SolidBrush(Color.DarkSlateGray);

                    // 根据选择的语言显示不同提示
                    if (radioButton1.Checked)
                    {
                        // 中文提示
                        string text = "当前图片不存在";

                        // 计算文本位置使其居中
                        SizeF textSize = g.MeasureString(text, font);
                        PointF location = new PointF(
                            (image.Width - textSize.Width) / 2,
                            (image.Height - textSize.Height) / 2);

                        // 绘制文本
                        g.DrawString(text, font, textBrush, location);

                        // 添加图标
                        using (Font iconFont = new Font("Segoe UI Emoji", 48))
                        {
                            string icon = "❌"; // 叉号图标
                            SizeF iconSize = g.MeasureString(icon, iconFont);
                            g.DrawString(icon, iconFont, textBrush,
                                (image.Width - iconSize.Width) / 2,
                                (image.Height - iconSize.Height) / 2 - 60);
                        }
                    }
                    else
                    {
                        // 英文提示
                        string text = "Image Not Available";

                        // 计算文本位置使其居中
                        SizeF textSize = g.MeasureString(text, font);
                        PointF location = new PointF(
                            (image.Width - textSize.Width) / 2,
                            (image.Height - textSize.Height) / 2);

                        // 绘制文本
                        g.DrawString(text, font, textBrush, location);

                        // 添加图标
                        using (Font iconFont = new Font("Segoe UI Emoji", 48))
                        {
                            string icon = "❌"; // 叉号图标
                            SizeF iconSize = g.MeasureString(icon, iconFont);
                            g.DrawString(icon, iconFont, textBrush,
                                (image.Width - iconSize.Width) / 2,
                                (image.Height - iconSize.Height) / 2 - 60);
                        }
                    }
                }

                // 显示生成的图片
                pictureBoxPreview.Image = image;
            }
            catch (Exception ex)
            {
                // 记录错误到日志
                txtLog.AppendText($"生成空图片时出错: {ex.Message}\n");

                // 显示简单的错误图片
                pictureBoxPreview.Image = new Bitmap(pictureBoxPreview.Width, pictureBoxPreview.Height);
                using (Graphics g = Graphics.FromImage(pictureBoxPreview.Image))
                {
                    g.Clear(Color.Red);
                    g.DrawString("ERROR", new Font("Arial", 12), Brushes.White, 10, 10);
                }
            }
        }

        private void treeViewFiles_AfterSelect(object sender, TreeViewEventArgs e)
        {
            string selectedFile = Path.Combine(txtDirectoryPath.Text, e.Node.FullPath.Remove(0, e.Node.FullPath.IndexOf('\\') + 1));
            // 显示图片预览

            try
            {
                // 显示基本文件信息
                if (File.Exists(selectedFile))
                {
                    if (Path.GetExtension(selectedFile).Equals(".jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadImageWithoutLock(selectedFile);
                    }
                    else
                    {
                        var fileInfo = new FileInfo(selectedFile);
                        var sb = new StringBuilder();

                        // 显示基本信息
                        sb.AppendLine($"文件名: {fileInfo.Name}");
                        sb.AppendLine($"大小: {FormatFileSize(fileInfo.Length)}");
                        sb.AppendLine($"修改时间: {fileInfo.LastWriteTime}");

                        label_fileinfo.Text = sb.ToString();
                        string jpgFile = (selectedFile + ".jpg");
                        if (File.Exists(jpgFile))
                        {
                            LoadImageWithoutLock(jpgFile);
                        }
                        else
                        {
                            LogMessage($"相关的jpg文件未找到: {jpgFile}");
                            setnullimage();
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                LogerrorMessage($"显示图片时出错: {selectedFile}: {ex.Message}");
            }
        }

        // 添加格式化文件大小的辅助方法
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
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
                await _thumbnailGenerator.ProcessDirectoryAsync(txtDirectoryPath.Text, UpdateThumbnailProgress, true);
                LogMessage("处理完成。");
                LoadFileSystem(); // 重新加载文件系统，实时更新
           
            }
        }

        private void UpdateJpgTotalSize()
        {
            try
            {
                if (!Directory.Exists(txtDirectoryPath.Text))
                {
                    txtLog.AppendText("目录不存在，无法计算jpg文件大小\n");
                    return;
                }

                long totalBytes = 0;
                foreach (var file in Directory.EnumerateFiles(txtDirectoryPath.Text, "*.jpg", System.IO. SearchOption.AllDirectories))
                {
                    totalBytes += new FileInfo(file).Length;
                }

                txtLog.AppendText($"当前目录jpg文件总大小: {FormatFileSize(totalBytes)}\n");
                lab_imagesize.Text = FormatFileSize(totalBytes);
            }
            catch (Exception ex)
            {
                LogerrorMessage($"计算jpg文件总大小时出错: {ex.Message}");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveVideoCache();
            File.WriteAllText("lastDirectoryPath.txt", txtDirectoryPath.Text);
            
            // 保存控件状态到配置文件
            var settings = new StringBuilder();
            settings.AppendLine($"Quality={trackBarQuality.Value}");
            settings.AppendLine($"Language={(radioButton1.Checked ? "zh-CN" : "en-US")}");
            File.WriteAllText("settings.ini", settings.ToString());
        }

        private void treeViewFiles_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            string selectedFile = Path.Combine(txtDirectoryPath.Text, treeViewFiles.SelectedNode.FullPath.Remove(0, treeViewFiles.SelectedNode.FullPath.IndexOf('\\') + 1));

            if (File.Exists(selectedFile))
            {
                try
                {
                    // Use ShellExecute to open the file with the associated application
                    var process = new System.Diagnostics.Process();
                    process.StartInfo = new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = selectedFile,
                        UseShellExecute = true // This ensures the file is opened with the default application
                    };
                    process.Start();
                }
                catch (Exception ex)
                {
                    LogerrorMessage("打开文件失败！" + selectedFile + " 失败原因：" + ex.Message);
                }
            }
            else
            {
                if (Directory.Exists(selectedFile))
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", selectedFile);
                    }
                    catch { }
                }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint GetLongPathName(string shortPath, System.Text.StringBuilder longPathBuffer, uint bufferLength);

        private static string GetLongPathName(string shortPath)
        {
            System.Text.StringBuilder longPathBuffer = new System.Text.StringBuilder(260); // MAX_PATH length
            uint result = GetLongPathName(shortPath, longPathBuffer, (uint)longPathBuffer.Capacity);

            if (result > 0 && result < longPathBuffer.Capacity)
            {
                return longPathBuffer.ToString();
            }
            else
            {
                // If the path can't be converted, return the original path
                return shortPath;
            }
        }

        private void treeViewFiles_KeyDown(object sender, KeyEventArgs e)
        {
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
                            try
                            {
                                if (File.Exists(selectedFile + ".jpg"))
                                {
                                    File.Delete(selectedFile + ".jpg");
                                }
                            }
                            catch { }

                            FileSystem.DeleteFile(selectedFile, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                            treeViewFiles.Nodes.Remove(treeViewFiles.SelectedNode);
                            LogerrorMessage("文件已移至回收站！" + selectedFile);
                            LoadFileSystem();
                        }
                        catch (Exception ex)
                        {
                            LogerrorMessage($"删除文件时出错: {ex.Message} 尝试移动到根目录删除！");
                            try
                            {
                                string rootDirectory = Path.GetPathRoot(selectedFile);
                                string tempFilePath = Path.Combine(rootDirectory, Path.GetFileName(selectedFile));
                                File.Move(selectedFile, tempFilePath);
                                FileSystem.DeleteFile(tempFilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                            }
                            catch
                            {
                                LogerrorMessage($"移动到根目录删除文件时出错: {ex.Message} 请手动删除！");
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

        // 添加缓存相关的字段
        private const string CACHE_FILE = "video_cache.json";
        private Dictionary<string, VideoCache> videoCache = new Dictionary<string, VideoCache>();

        // 添加加载和保存缓存的方法
        private void LoadVideoCache(string directoryPath)
        {
            try
            {
                if (File.Exists(CACHE_FILE))
                {
                    string json = File.ReadAllText(CACHE_FILE);
                    var allCache = JsonSerializer.Deserialize<Dictionary<string, VideoCache>>(json);
                    videoCache = allCache
                        .Where(kvp => kvp.Key.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    LogerrorMessage($"已加载目录缓存，包含 {videoCache.Count} 个视频信息");
                }
            }
            catch (Exception ex)
            {
                LogerrorMessage($"加载缓存失败: {ex.Message}");
                videoCache = new Dictionary<string, VideoCache>();
            }
        }

        private void SaveVideoCache()
        {
            try
            {
                Dictionary<string, VideoCache> allCache;
                if (File.Exists(CACHE_FILE))
                {
                    string json = File.ReadAllText(CACHE_FILE);
                    allCache = JsonSerializer.Deserialize<Dictionary<string, VideoCache>>(json);
                }
                else
                {
                    allCache = new Dictionary<string, VideoCache>();
                }
                foreach (var kvp in videoCache)
                {
                    allCache[kvp.Key] = kvp.Value;
                }
                string newJson = JsonSerializer.Serialize(allCache);
                File.WriteAllText(CACHE_FILE, newJson);
                LogerrorMessage($"已保存视频缓存，当前目录包含 {videoCache.Count} 个视频信息");
            }
            catch (Exception ex)
            {
                LogerrorMessage($"保存缓存失败: {ex.Message}");
            }
        }

        private TreeNode FindTreeNodeByPath(string fullPath)
        {
            try
            {
                // 确保路径使用正确的分隔符
                fullPath = fullPath.Replace('/', '\\');
                string relativePath = fullPath.Substring(txtDirectoryPath.Text.Length).TrimStart('\\');
                string[] parts = relativePath.Split('\\');

                // 从根节点开始查找
                if (treeViewFiles.Nodes.Count == 0) return null;
                TreeNode currentNode = treeViewFiles.Nodes[0];

                // 遍历路径的每一部分
                foreach (string part in parts)
                {
                    bool found = false;
                    foreach (TreeNode child in currentNode.Nodes)
                    {
                        // 使用不区分大小写的比较
                        if (string.Equals(child.Text, part, StringComparison.OrdinalIgnoreCase))
                        {
                            currentNode = child;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        LogerrorMessage($"无法找到节点: {part} in {fullPath}");
                        return null;
                    }
                }
                return currentNode;
            }
            catch (Exception ex)
            {
                LogerrorMessage($"查找节点时出错: {fullPath}, 错误: {ex.Message}");
                return null;
            }
        }

        private TreeNode CreateDirectoryNode(DirectoryInfo directoryInfo)
        {
            var directoryNode = new TreeNode(directoryInfo.Name);

            foreach (var directory in directoryInfo.GetDirectories())
            {
                directoryNode.Nodes.Add(CreateDirectoryNode(directory));
            }

            foreach (var file in directoryInfo.GetFiles())
            {
                string fileExtension = Path.GetExtension(file.Name).ToLower();

                // 常见域名后缀列表
                string[] commonDomainExtensions = new string[]
                {
                    ".com", ".net", ".org", ".edu", ".gov", ".mil", ".int",
                    ".info", ".biz", ".co", ".us", ".uk", ".cn"
                };

                if (commonDomainExtensions.Any(ext => fileExtension.StartsWith(ext)))
                {
                    fileExtension = "";
                }

                string[] videoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", "" }; // 支持的视频格式列表

                if (Array.Exists(videoExtensions, ext => ext == fileExtension))
                {
                    directoryNode.Nodes.Add(new TreeNode(file.Name));
                }
            }

            return directoryNode;
        }
    }
}
