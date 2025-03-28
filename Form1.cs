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

        public Form1()
        {
            InitializeComponent();
            LoadFileSystem();
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

        private async Task<List<string>> ExtractKeyFrameHashes(string videoPath, int frameCount)
        {
            var hashes = new List<string>();
            try
            {
                var inputFile = new MediaFile(videoPath);
                var ffmpeg = new Engine(Path.Combine(Application.StartupPath, "ffmpeg.exe"));
                var metadata = await GetMetaDataWithTimeout(ffmpeg, inputFile, 3000);
                if (metadata == null) return hashes;

                double totalSeconds = metadata.Duration.TotalSeconds;
                var random = new Random();

                // 确保采样点更有代表性
                var samplePoints = new List<int>();

                // 跳过开头和结尾的部分，因为这些部分可能是片头片尾
                double startTime = Math.Min(totalSeconds * 0.1, 10); // 跳过开始的10%或10秒
                double endTime = Math.Max(totalSeconds * 0.9, totalSeconds - 10); // 跳过结尾的10%或10秒

                if (endTime - startTime > frameCount)
                {
                    // 在有效时间范围内均匀采样
                    double interval = (endTime - startTime) / (frameCount + 1);
                    for (int i = 0; i < frameCount; i++)
                    {
                        double baseTime = startTime + interval * (i + 1);
                        // 在每个区间内添加随机偏移，但确保不超出边界
                        int second = (int)(baseTime + random.NextDouble() * interval * 0.5);
                        second = Math.Max((int)startTime, Math.Min(second, (int)endTime));
                        samplePoints.Add(second);
                    }
                }
                else
                {
                    // 对于很短的视频，均匀采样
                    for (int i = 0; i < Math.Min(frameCount, (int)totalSeconds); i++)
                    {
                        samplePoints.Add(i);
                    }
                }

                foreach (var second in samplePoints)
                {
                    var options = new ConversionOptions { Seek = TimeSpan.FromSeconds(second) };
                    var tempFile = Path.GetTempFileName() + ".jpg";
                    var outputFile = new MediaFile(tempFile);
                    await ffmpeg.GetThumbnailAsync(inputFile, outputFile, options);

                    using (var image = Image.FromFile(tempFile))
                    {
                        // 计算两种不同的哈希值以提高准确性
                        string pHash = CalculatePerceptualHash(image);
                        string aHash = CalculateAverageHash(image);
                        hashes.Add(pHash + ":" + aHash); // 组合两种哈希
                    }
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogerrorMessage($"提取关键帧失败: {videoPath}: {ex.Message}");
            }
            return hashes;
        }

        private string CalculatePerceptualHash(Image image)
        {
            using (var resized = new Bitmap(image, new Size(32, 32)))
            using (var grayImage = ToGrayscale(resized))
            {
                // 将图像转换为二维数组
                double[,] pixels = new double[32, 32];
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        pixels[x, y] = grayImage.GetPixel(x, y).R;
                    }
                }

                // 计算DCT变换
                double[,] dct = ComputeDCT(pixels);

                // 计算均值
                double sum = 0;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        if (x == 0 && y == 0) continue; // 跳过DC系数
                        sum += dct[x, y];
                    }
                }
                double average = sum / 63;

                // 生成哈希
                var hash = new StringBuilder();
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        if (x == 0 && y == 0) continue;
                        hash.Append(dct[x, y] > average ? "1" : "0");
                    }
                }
                return hash.ToString();
            }
        }

        private double[,] ComputeDCT(double[,] pixels)
        {
            int size = 32;
            double[,] dct = new double[8, 8];

            for (int u = 0; u < 8; u++)
            {
                for (int v = 0; v < 8; v++)
                {
                    double sum = 0;
                    for (int x = 0; x < size; x++)
                    {
                        for (int y = 0; y < size; y++)
                        {
                            sum += pixels[x, y] *
                                   Math.Cos((2 * x + 1) * u * Math.PI / (2 * size)) *
                                   Math.Cos((2 * y + 1) * v * Math.PI / (2 * size));
                        }
                    }
                    dct[u, v] = sum * ((u == 0 ? 1.0 / Math.Sqrt(2) : 1.0) * (v == 0 ? 1.0 / Math.Sqrt(2) : 1.0)) / 4.0;
                }
            }
            return dct;
        }

        private string CalculateAverageHash(Image image)
        {
            using (var resized = new Bitmap(image, new Size(8, 8)))
            using (var grayImage = ToGrayscale(resized))
            {
                int total = 0;
                var pixels = new byte[64];
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        var pixel = grayImage.GetPixel(x, y);
                        int brightness = pixel.R;
                        pixels[y * 8 + x] = (byte)brightness;
                        total += brightness;
                    }
                }
                byte avg = (byte)(total / 64);
                var hash = new StringBuilder();
                foreach (var p in pixels)
                {
                    hash.Append(p >= avg ? "1" : "0");
                }
                return hash.ToString();
            }
        }

        private Bitmap ToGrayscale(Bitmap image)
        {
            var gray = new Bitmap(image.Width, image.Height);
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    int grayValue = (int)(pixel.R * 0.3 + pixel.G * 0.59 + pixel.B * 0.11);
                    gray.SetPixel(x, y, Color.FromArgb(grayValue, grayValue, grayValue));
                }
            }
            return gray;
        }

        private int HammingDistance(string hash1, string hash2)
        {
            if (hash1.Length != hash2.Length) return int.MaxValue;
            int distance = 0;
            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i]) distance++;
            }
            return distance;
        }

        private async void btnCheckDuplicates_Click(object sender, EventArgs e)
        {
           // 加载当前目录的缓存
            LoadVideoCache(txtDirectoryPath.Text);

            var videoFiles = GetAllVideoFiles(txtDirectoryPath.Text);
            if (videoFiles.Count == 0)
            {
                //MessageBox.Show("没有找到视频文件。");
                return;
            }

            var videoInfos = new Dictionary<string, VideoInfo>();
            progressBar.Maximum = videoFiles.Count;
            progressBar.Value = 0;
            lblProgress.Text = "正在分析视频...";

            foreach (var file in videoFiles)
            {
                var info = await GetVideoInfo(file);
                if (info != null)
                {
                    videoInfos[file] = info;
                }
                progressBar.Value++;
                lblProgress.Text = $"收集视频信息: {progressBar.Value}/{videoFiles.Count}";
                await Task.Delay(1);
            }

            // 保存更新后的缓存
            SaveVideoCache();

            // 继续执行原有的比较逻辑
            var groups = PreFilterVideos(videoInfos);
            var duplicates = new List<List<string>>();
            progressBar.Value = 0;
            progressBar.Maximum = groups.Count;

            foreach (var group in groups)
            {
                if (group.Count > 1)
                {
                    var groupDuplicates = await FindDuplicatesInGroup(group);
                    duplicates.AddRange(groupDuplicates);
                }
                progressBar.Value++;
                lblProgress.Text = $"比较视频组: {progressBar.Value}/{groups.Count}";
                await Task.Delay(1);
            }

            DisplayDuplicates(duplicates);
            lblProgress.Text = "检测完成。";
        }

        private class VideoInfo
        {
            public string Path { get; set; }
            public long FileSize { get; set; }
            public TimeSpan Duration { get; set; }
            public List<string> Hashes { get; set; }  // 存储pHash值
        }

        private async Task<VideoInfo> GetVideoInfo(string videoPath)
        {
            try
            {
                var fileInfo = new FileInfo(videoPath);

                // 检查缓存
                if (videoCache.TryGetValue(videoPath, out var cachedInfo))
                {
                    if (cachedInfo.LastModified == fileInfo.LastWriteTime &&
                        cachedInfo.FileSize == fileInfo.Length)
                    {
                        return new VideoInfo
                        {
                            Path = videoPath,
                            FileSize = cachedInfo.FileSize,
                            Duration = cachedInfo.Duration,
                            Hashes = cachedInfo.Hashes
                        };
                    }
                }

                var inputFile = new MediaFile(videoPath);
                var ffmpeg = new Engine(Path.Combine(Application.StartupPath, "ffmpeg.exe"));
                var metadata = await GetMetaDataWithTimeout(ffmpeg, inputFile, 3000);

                if (metadata == null) return null;

                var info = new VideoInfo
                {
                    Path = videoPath,
                    FileSize = fileInfo.Length,
                    Duration = metadata.Duration,
                    Hashes = await ExtractFrameHashes(videoPath, 5)  // 提取5个关键帧的pHash
                };

                // 更新缓存
                videoCache[videoPath] = new VideoCache
                {
                    FilePath = videoPath,
                    FileSize = fileInfo.Length,
                    Duration = metadata.Duration,
                    Hashes = info.Hashes,
                    LastModified = fileInfo.LastWriteTime
                };

                return info;
            }
            catch (Exception ex)
            {
                LogerrorMessage($"获取视频信息失败: {videoPath}: {ex.Message}");
                return null;
            }
        }

        private async Task<List<string>> ExtractFrameHashes(string videoPath, int frameCount)
        {
            var hashes = new List<string>();
            try
            {
                var inputFile = new MediaFile(videoPath);
                var ffmpeg = new Engine(Path.Combine(Application.StartupPath, "ffmpeg.exe"));
                var metadata = await GetMetaDataWithTimeout(ffmpeg, inputFile, 3000);
                if (metadata == null) return hashes;

                double totalSeconds = metadata.Duration.TotalSeconds;

                // 选择关键时间点进行采样
                var samplePoints = new List<double>
                {
                    totalSeconds * 0.2,  // 20%处
                    totalSeconds * 0.4,  // 40%处
                    totalSeconds * 0.5,  // 50%处
                    totalSeconds * 0.6,  // 60%处
                    totalSeconds * 0.8   // 80%处
                };

                foreach (var second in samplePoints)
                {
                    var options = new ConversionOptions { Seek = TimeSpan.FromSeconds(second) };
                    var tempFile = Path.GetTempFileName() + ".jpg";
                    var outputFile = new MediaFile(tempFile);

                    await ffmpeg.GetThumbnailAsync(inputFile, outputFile, options);

                    using (var image = Image.FromFile(tempFile))
                    {
                        string hash = CalculatePHash(image);
                        hashes.Add(hash);
                    }
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogerrorMessage($"提取帧哈希失败: {videoPath}: {ex.Message}");
            }
            return hashes;
        }

        private string CalculatePHash(Image image)
        {
            // 第一步：缩小尺寸
            using (var resized = new Bitmap(image, new Size(32, 32)))
            // 第二步：转换为灰度图
            using (var grayImage = ToGrayscale(resized))
            {
                // 第三步：计算DCT
                double[,] dctValues = ComputeDCT(grayImage);

                // 第四步：计算均值（不包括第一个DCT系数）
                double sum = 0;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        if (x == 0 && y == 0) continue; // 跳过第一个系数
                        sum += dctValues[x, y];
                    }
                }
                double average = sum / 63;

                // 第五步：生成哈希值
                var hash = new StringBuilder();
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        if (x == 0 && y == 0) continue;
                        hash.Append(dctValues[x, y] > average ? "1" : "0");
                    }
                }
                return hash.ToString();
            }
        }

        private double[,] ComputeDCT(Bitmap grayImage)
        {
            int size = 32;
            double[,] dct = new double[8, 8];
            double[,] pixels = new double[size, size];

            // 获取像素值
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[x, y] = grayImage.GetPixel(x, y).R;
                }
            }

            // 计算DCT
            for (int u = 0; u < 8; u++)
            {
                for (int v = 0; v < 8; v++)
                {
                    double sum = 0;
                    for (int x = 0; x < size; x++)
                    {
                        for (int y = 0; y < size; y++)
                        {
                            sum += pixels[x, y] *
                                   Math.Cos((2 * x + 1) * u * Math.PI / (2 * size)) *
                                   Math.Cos((2 * y + 1) * v * Math.PI / (2 * size));
                        }
                    }
                    dct[u, v] = sum * ((u == 0 ? 1.0 / Math.Sqrt(2) : 1.0) * (v == 0 ? 1.0 / Math.Sqrt(2) : 1.0)) / 4.0;
                }
            }
            return dct;
        }

        private bool AreVideosSimilar(List<string> hashes1, List<string> hashes2)
        {
            if (hashes1.Count == 0 || hashes2.Count == 0)
                return false;

            int similarFrames = 0;
            int requiredSimilarFrames = 3; // 要求至少3帧相似

            foreach (var hash1 in hashes1)
            {
                foreach (var hash2 in hashes2)
                {
                    int hammingDistance = CalculateHammingDistance(hash1, hash2);
                    if (hammingDistance <= 5) // 允许5位不同
                    {
                        similarFrames++;
                        if (similarFrames >= requiredSimilarFrames)
                        {
                            return true;
                        }
                        break;
                    }
                }
            }

            return false;
        }

        private int CalculateHammingDistance(string hash1, string hash2)
        {
            if (hash1.Length != hash2.Length) return int.MaxValue;

            int distance = 0;
            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i]) distance++;
            }
            return distance;
        }

        private List<List<VideoInfo>> PreFilterVideos(Dictionary<string, VideoInfo> videos)
        {
            var groups = new List<List<VideoInfo>>();

            // 按时长分组（允许2秒误差）
            var durationGroups = videos.Values
                .GroupBy(v => (int)(v.Duration.TotalSeconds / 2))
                .Where(g => g.Count() > 1);

            foreach (var durationGroup in durationGroups)
            {
                // 在时长组内按文件大小分组（允许5%误差）
                var sizeGroups = durationGroup
                    .GroupBy(v => v.FileSize / (1024 * 1024 * 5))
                    .Where(g => g.Count() > 1);

                groups.AddRange(sizeGroups.Select(g => g.ToList()));
            }

            return groups;
        }

        private async Task<List<List<string>>> FindDuplicatesInGroup(List<VideoInfo> group)
        {
            var duplicates = new List<List<string>>();
            var processed = new HashSet<string>();

            foreach (var video1 in group)
            {
                if (processed.Contains(video1.Path)) continue;

                var duplicateGroup = new List<string> { video1.Path };
                foreach (var video2 in group)
                {
                    if (video1.Path == video2.Path || processed.Contains(video2.Path)) continue;

                    // 检查文件大小相似度
                    double sizeDiff = Math.Abs(1.0 - (double)video1.FileSize / video2.FileSize);
                    if (sizeDiff > 0.05) continue; // 如果文件大小差异超过5%，跳过

                    // 检查时长相似度
                    double durationDiff = Math.Abs(video1.Duration.TotalSeconds - video2.Duration.TotalSeconds);
                    if (durationDiff > 2) continue; // 如果时长差异超过2秒，跳过

                    // 检查关键帧相似度
                    if (AreVideosSimilar(video1.Hashes, video2.Hashes))
                    {
                        duplicateGroup.Add(video2.Path);
                        processed.Add(video2.Path);
                    }
                }

                if (duplicateGroup.Count > 1)
                {
                    duplicates.Add(duplicateGroup);
                }
                processed.Add(video1.Path);
            }

            return duplicates;
        }

        private void DisplayDuplicates(List<List<string>> duplicates)
        {
            if (duplicates == null || duplicates.Count == 0)
            {
                //MessageBox.Show("未找到重复视频。");
                return;
            }

            treeViewFiles.BeginUpdate();

            // 首先清除所有节点的颜色
            ClearAllNodeColors(treeViewFiles.Nodes);

            // 创建一个颜色生成器
            var colorGenerator = new ColorGenerator();

            StringBuilder message = new StringBuilder();
            message.AppendLine($"找到 {duplicates.Count} 组重复视频：");

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

            // 显示结果消息
            //MessageBox.Show(message.ToString(), "重复视频检测结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            if (Directory.Exists(txtDirectoryPath.Text))
            {
                DirectoryInfo dir = new DirectoryInfo(txtDirectoryPath.Text);
                await ProcessDirectory(dir);
                LogMessage("处理完成。");
                LoadFileSystem(); // 重新加载文件系统，实时更新
            }
            else
            {
                LogMessage("目录不存在。");
            }
        }

        private async Task ProcessDirectory(DirectoryInfo dir, bool delimg = false)
        {
            // 计算进度条的最大值为当前目录及其所有子目录下的文件总数
            int totalFiles = CountFiles(dir);
            progressBar.Minimum = 0;
            progressBar.Maximum = totalFiles;

            int processedFiles = 0;

            // 递归处理目录
            processedFiles = await ProcessDirectoryRecursively(dir, processedFiles, totalFiles, delimg);
        }

        /// <summary>
        /// 递归处理目录中的所有文件（排除所有图片格式文件）
        /// </summary>
        /// <param name="dir">要处理的目录信息</param>
        /// <param name="processedFiles">已处理的文件数</param>
        /// <param name="totalFiles">总文件数（用于进度计算）</param>
        /// <param name="delimg">是否删除图片的标记</param>
        /// <returns>更新后的已处理文件数</returns>
        private async Task<int> ProcessDirectoryRecursively(DirectoryInfo dir, int processedFiles, int totalFiles, bool delimg = false)
        {
            try
            {
                // 定义要排除的图片扩展名列表（不区分大小写）
                var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg", ".jpeg", ".png", ".gif",
                    ".bmp", ".tiff", ".webp", ".svg"
                };

                // 处理当前目录下的所有非图片文件
                foreach (var file in dir.GetFiles().Where(f => !imageExtensions.Contains(f.Extension)))
                {
                    try
                    {
                        // 生成图片（或根据delimg参数删除图片）
                        await GenerateImage(file.FullName, delimg);

                        // 更新进度
                        processedFiles++;
                        progressBar.Value = Math.Min(processedFiles, progressBar.Maximum);

                        // 更新进度标签（UI线程安全方式）
                        if (lblProgress.InvokeRequired)
                        {
                            lblProgress.Invoke(new Action(() =>
                            {
                                lblProgress.Text = $"进度: {processedFiles}/{totalFiles}";
                                lblProgress.Refresh();
                            }));
                        }
                        else
                        {
                            lblProgress.Text = $"进度: {processedFiles}/{totalFiles}";
                            lblProgress.Refresh();
                        }

                        // 每处理10个文件更新一次UI（提高性能）
                        if (processedFiles % 10 == 0)
                        {
                            await Task.Delay(1); // 允许UI线程处理消息
                        }
                    }
                    catch (Exception ex)
                    {
                        // 记录单个文件处理错误
                        txtLog.AppendText($"处理文件失败 {file.FullName}: {ex.Message}\n");
                    }
                }

                // 递归处理子目录
                foreach (var subDir in dir.GetDirectories())
                {
                    try
                    {
                        processedFiles = await ProcessDirectoryRecursively(subDir, processedFiles, totalFiles, delimg);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        txtLog.AppendText($"无权限访问目录: {subDir.FullName}\n");
                    }
                }

                return processedFiles;
            }
            catch (Exception ex)
            {
                // 记录目录处理错误
                txtLog.AppendText($"处理目录失败 {dir.FullName}: {ex.Message}\n");
                return processedFiles;
            }
        }

        /// <summary>
        /// 递归计算目录中所有文件的数量（排除所有图片格式文件）
        /// </summary>
        /// <param name="dir">要计算的目录信息</param>
        /// <returns>排除图片文件后的文件总数</returns>
        private int CountFiles(DirectoryInfo dir)
        {
            try
            {
                // 定义要排除的图片扩展名列表（不区分大小写）
                var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg", ".jpeg", ".png", ".gif",
                    ".bmp", ".tiff", ".webp", ".svg"
                };

                // 计算当前目录下的文件数（排除所有图片格式）
                int count = dir.GetFiles()
                              .Count(file => !imageExtensions.Contains(file.Extension));

                // 递归计算所有子目录的文件数
                foreach (var subDir in dir.GetDirectories())
                {
                    count += CountFiles(subDir);
                }

                return count;
            }
            catch (UnauthorizedAccessException)
            {
                // 无权限访问的目录跳过不计
                return 0;
            }
            catch (Exception ex)
            {
                // 记录意外错误
                txtLog.AppendText($"计算文件数时出错({dir.FullName}): {ex.Message}\n");
                return 0;
            }
        }
        private async Task GenerateImage(string filePath, bool delimg = false)
        {
            if (delimg)
            {
                if (File.Exists(filePath + ".jpg"))
                {
                    try
                    {
                        File.Delete(filePath + ".jpg");
                    }
                    catch { }

                    LogMessage($"删除图像: {filePath}");
                }
            }
            else
            {
                // 检查最终图片是否已经存在
                if (File.Exists(filePath + ".jpg"))
                {
                    LogMessage($"图像已存在: {filePath}");
                }
                else
                {
                    try
                    {
                        // 记录开始生成图像的日志
                        LogMessage($"开始生成图像: {filePath}");

                        // 异步调用 getimgcap 生成缩略图
                        await getimgcap(filePath);

                        // 记录图像生成完成的日志
                        LogMessage($"图像生成完成: {filePath}");
                    }
                    catch (Exception ex)
                    {
                        // 记录生成图像过程中发生的错误
                        LogerrorMessage($"生成图像时出错: {filePath}: {ex.Message}");
                    }
                }
            }
        }

        private async Task<MetaData?> GetMetaDataWithTimeout(Engine ffmpeg, MediaFile inputFile, int timeoutMilliseconds)
        {
            var cts = new CancellationTokenSource();
            var token = cts.Token;

            var metadataTask = ffmpeg.GetMetaDataAsync(inputFile);
            var delayTask = Task.Delay(timeoutMilliseconds, token);

            var completedTask = await Task.WhenAny(metadataTask, delayTask);

            if (completedTask == delayTask)
            {
                // 超时，取消获取元数据的任务
                cts.Cancel();
                return null; // 超时处理
            }

            // 获取元数据成功，返回结果
            return await metadataTask;
        }

        public async Task getimgcap(string inputname)
        {
            if (File.Exists(inputname + ".jpg"))
            {
                LogMessage($"图像已存在: {inputname}");
            }
            else
            {
                string fileExtension = Path.GetExtension(inputname).ToLower();

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
                    try
                    {
                        var inputFile = new MediaFile(inputname);
                        var ffmpeg = new Engine(Application.StartupPath + @"\ffmpeg.exe");

                        var stopwatch = Stopwatch.StartNew();
                        // 设置超时机制
                        var metadata = await GetMetaDataWithTimeout(ffmpeg, inputFile, 3000);
                        stopwatch.Stop();
                        LogMessage($"获取元数据耗时: {stopwatch.ElapsedMilliseconds} ms");

                        if (metadata == null)
                        {
                            LogerrorMessage($"获取元数据超时放弃这个文件: {inputname} 建议重下或者删除该文件！");
                            return;
                        }

                        double totalSeconds = metadata.Duration.TotalSeconds;
                        int segmentDuration = (int)(totalSeconds / 9);

                        for (int i = 0; i < 9; i++)
                        {
                            Random random = new Random(Guid.NewGuid().GetHashCode());
                            int randomSecond = random.Next(i * segmentDuration, (i + 1) * segmentDuration);
                            var options = new ConversionOptions { Seek = TimeSpan.FromSeconds(randomSecond) };
                            // Add a prefix or suffix to avoid numeric-only filenames
                            var outputFile = new MediaFile(inputname + "_img_" + (i + 1).ToString() + ".jpg");
                            await ffmpeg.GetThumbnailAsync(inputFile, outputFile, options);
                        }

                        getjiugongge(inputname);
                        LogMessage($"合并截图完成: {inputname}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"出错: {ex.Message}");
                    }
                }
                else
                {
                    LogMessage($"不支持的文件格式: {inputname}");
                }
            }
        }

        public async void getjiugongge(string inputname)
        {
            try
            {
                Image img1 = Image.FromFile(inputname + "_img_1.jpg");
                int imgWidth = img1.Width * 3;
                int imgHeight = img1.Height * 3;
                img1.Dispose();

                Bitmap joinedBitmap = new Bitmap(imgWidth, imgHeight);
                Graphics graph = Graphics.FromImage(joinedBitmap);

                for (int i = 0; i < 9; i++)
                {
                    Image tmpimg = Image.FromFile(inputname + "_img_" + (i + 1) + ".jpg");
                    graph.DrawImage(tmpimg, (i % 3) * tmpimg.Width, (i / 3) * tmpimg.Height, tmpimg.Width, tmpimg.Height);
                    tmpimg.Dispose();
                }

                string outputFilePath = inputname + ".jpg";

                // 将图像缩小到原来的30%
                int newWidth = (int)(imgWidth * 0.3);
                int newHeight = (int)(imgHeight * 0.3);
                Bitmap resizedBitmap = new Bitmap(joinedBitmap, new Size(newWidth, newHeight));

                // 保存缩小后的图像
                resizedBitmap.Save(outputFilePath);
                resizedBitmap.Dispose();

                joinedBitmap.Dispose();
                graph.Dispose();

                await Task.Delay(1000);
                for (int i = 0; i < 9; i++)
                {
                    try
                    {
                        File.Delete(inputname + "_img_" + (i + 1) + ".jpg");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"删除文件时出错: {inputname + "_img_" + (i + 1) + ".jpg"}: {ex.Message}");
                    }
                }

                LogMessage($"最终图像保存为: {outputFilePath}");
            }
            catch
            {
                LogerrorMessage($"文件名太变态无法识别: {inputname} 建议重命名该文件！");
            }
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
                    await getimgcap(selectedFilePath);
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
                        await getimgcap(selectedFilePath);
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
            //在屏幕正中间弹出一张base64图片
            string base64Str = "/";

            if (radioButton2.Checked)
            {
                base64Str = "/";
            }

            // 将 Base64 字符串转换为字节数组
            byte[] imageBytes = Convert.FromBase64String(base64Str);

            // 使用字节数组创建图片
            using (MemoryStream ms = new MemoryStream(imageBytes))
            {
                Image image = Image.FromStream(ms);

                // 创建一个新的 Form 来显示图片
                Form imageForm = new Form
                {
                    FormBorderStyle = FormBorderStyle.None, // 无边框
                    StartPosition = FormStartPosition.CenterScreen, // 屏幕中央
                    Size = image.Size // 设置窗口大小为图片大小
                };

                // 创建一个 PictureBox 并将图片设置为其图像
                PictureBox pictureBox = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    Image = image,
                    SizeMode = PictureBoxSizeMode.Zoom // 自动调整图片大小
                };
                // 为 PictureBox 添加点击事件，点击时关闭图片窗口
                pictureBox.Click += (s, ev) => imageForm.Close();
                // 将 PictureBox 添加到 Form 中
                imageForm.Controls.Add(pictureBox);

                // 显示图片 Form
                imageForm.ShowDialog();
            }
        }
        #endregion

        Label supportLabel = new Label();

        private async void Form1_Load(object sender, EventArgs e)
        {
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

            await GetUpdateCfgAsync();

            var culture = System.Globalization.CultureInfo.CurrentCulture.Name;

            if (culture == "zh-CN")
            {
                radioButton1.Checked = true;
            }
            else
            {
                radioButton2.Checked = true;
            }
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
                if (Directory.Exists(txtDirectoryPath.Text))
                {
                    DirectoryInfo dir = new DirectoryInfo(txtDirectoryPath.Text);
                    await ProcessDirectory(dir, true);
                    LogMessage("处理完成。");
                    LoadFileSystem(); // 重新加载文件系统，实时更新
                }
                else
                {
                    LogMessage("目录不存在。");
                }
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveVideoCache();
            File.WriteAllText("lastDirectoryPath.txt", txtDirectoryPath.Text);
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

        // 添加视频缓存类
        private class VideoCache
        {
            public string FilePath { get; set; }
            public long FileSize { get; set; }
            public TimeSpan Duration { get; set; }
            public List<string> Hashes { get; set; }
            public DateTime LastModified { get; set; }
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

                    // 只加载指定目录下的缓存
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
                // 读取现有的完整缓存
                if (File.Exists(CACHE_FILE))
                {
                    string json = File.ReadAllText(CACHE_FILE);
                    allCache = JsonSerializer.Deserialize<Dictionary<string, VideoCache>>(json);
                }
                else
                {
                    allCache = new Dictionary<string, VideoCache>();
                }

                // 更新当前目录的缓存
                foreach (var kvp in videoCache)
                {
                    allCache[kvp.Key] = kvp.Value;
                }

                // 保存完整缓存
                string newJson = JsonSerializer.Serialize(allCache);
                File.WriteAllText(CACHE_FILE, newJson);
                LogerrorMessage($"已保存视频缓存，当前目录包含 {videoCache.Count} 个视频信息");
            }
            catch (Exception ex)
            {
                LogerrorMessage($"保存缓存失败: {ex.Message}");
            }
        }
    }
}
