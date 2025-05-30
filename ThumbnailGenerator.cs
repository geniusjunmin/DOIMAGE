using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using FFmpeg.NET;

namespace DOIMAGE
{
    public class ThumbnailGenerator
    {
        private readonly string _ffmpegPath;
        private readonly string _startupPath;
        private readonly Action<string> _logMessageAction;
        private readonly Action<string> _logErrorAction;

        public ThumbnailGenerator(string ffmpegPath, string startupPath, Action<string> logMessageAction, Action<string> logErrorAction)
        {
            _ffmpegPath = ffmpegPath;
            _startupPath = startupPath;
            _logMessageAction = logMessageAction;
            _logErrorAction = logErrorAction;
        }

        private void LogMessage(string message)
        {
            _logMessageAction?.Invoke(message);
        }

        private void LogerrorMessage(string message)
        {
            _logErrorAction?.Invoke(message);
        }

        private async Task<MetaData?> GetMetaDataWithTimeout(Engine ffmpeg, MediaFile inputFile, int timeoutMilliseconds)
        {
            var cts = new CancellationTokenSource();
            var token = cts.Token;

            var metadataTask = ffmpeg.GetMetaDataAsync(inputFile, token); // Pass token here
            var delayTask = Task.Delay(timeoutMilliseconds, token);

            var completedTask = await Task.WhenAny(metadataTask, delayTask);

            if (completedTask == delayTask)
            {
                cts.Cancel();
                LogerrorMessage($"获取元数据超时: {inputFile.FileInfo.FullName}");
                return null; 
            }
            try
            {
                return await metadataTask;
            }
            catch (OperationCanceledException)
            {
                LogerrorMessage($"元数据检索已取消: {inputFile.FileInfo.FullName}");
                return null;
            }
            catch (Exception ex)
            {
                LogerrorMessage($"获取元数据错误 {inputFile.FileInfo.FullName}: {ex.Message}");
                return null;
            }
        }

        public async Task GetImgCapAsync(string inputName, int quality = 75)
        {
            if (File.Exists(inputName + ".jpg"))
            {
                LogMessage($"图像已存在: {inputName}");
                return;
            }

            string fileExtension = Path.GetExtension(inputName).ToLower();
            string[] commonDomainExtensions = { ".com", ".net", ".org", ".edu", ".gov", ".mil", ".int", ".info", ".biz", ".co", ".us", ".uk", ".cn" };
            if (commonDomainExtensions.Any(ext => fileExtension.StartsWith(ext)))
            {
                fileExtension = "";
            }

            string[] videoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", "" };
            if (!Array.Exists(videoExtensions, ext => ext == fileExtension))
            {
                LogMessage($"不支持的文件格式: {inputName}");
                return;
            }

            try
            {
                var inputFile = new MediaFile(inputName);
                var ffmpeg = new Engine(_ffmpegPath);

                var stopwatch = Stopwatch.StartNew();
                var metadata = await GetMetaDataWithTimeout(ffmpeg, inputFile, 3000);
                stopwatch.Stop();
                LogMessage($"获取元数据耗时: {stopwatch.ElapsedMilliseconds} ms for {inputName}");

                if (metadata == null)
                {
                    LogerrorMessage($"获取元数据超时放弃这个文件: {inputName} 建议重下或者删除该文件！");
                    return;
                }

                double totalSeconds = metadata.Duration.TotalSeconds;
                if (totalSeconds <= 0) // Handle cases with invalid duration
                {
                    LogerrorMessage($"视频时长无效或为0: {inputName}");
                    return;
                }
                int segmentDuration = (int)(totalSeconds / 9);
                if (segmentDuration <= 0) segmentDuration = 1; // Ensure segment duration is at least 1s for very short videos


                for (int i = 0; i < 9; i++)
                {
                    Random random = new Random(Guid.NewGuid().GetHashCode());
                    int randomSecond = random.Next(i * segmentDuration, Math.Min((i + 1) * segmentDuration, (int)totalSeconds -1));
                    if (randomSecond <0) randomSecond =0; // ensure positive seek time

                    // Convert quality from 10-100 range to FFmpeg's 1-31 range (1=best)
                    int ffmpegQuality = 31 - (int)((quality - 1) * 0.34);
                    ffmpegQuality = Math.Clamp(ffmpegQuality, 1, 31);
                    var options = new ConversionOptions { 
                        Seek = TimeSpan.FromSeconds(randomSecond),
                        VideoBitRate = ffmpegQuality // Using VideoBitRate to control quality
                    };
                    var outputFile = new MediaFile(inputName + "_img_" + (i + 1).ToString() + ".jpg");
                    await ffmpeg.GetThumbnailAsync(inputFile, outputFile, options);
                }

                await GetJiugonggeAsync(inputName, quality); // Pass quality parameter
                LogMessage($"合并截图完成: {inputName}");
            }
            catch (Exception ex)
            {
                LogerrorMessage($"生成截图过程中出错 ({inputName}): {ex.Message}");
            }
        }

        public async Task GetJiugonggeAsync(string inputName, int quality = 75) // Added quality parameter
        {
            try
            {
                if (!File.Exists(inputName + "_img_1.jpg"))
                {
                    LogerrorMessage($"九宫格的第一个分片不存在: {inputName}_img_1.jpg. 取消合并.");
                    return;
                }
                Image img1 = Image.FromFile(inputName + "_img_1.jpg");
                int imgWidth = img1.Width * 3;
                int imgHeight = img1.Height * 3;
                img1.Dispose();

                Bitmap joinedBitmap = new Bitmap(imgWidth, imgHeight);
                using (Graphics graph = Graphics.FromImage(joinedBitmap))
                {
                    for (int i = 0; i < 9; i++)
                    {
                        string tempImgPath = inputName + "_img_" + (i + 1) + ".jpg";
                        if (!File.Exists(tempImgPath)) 
                        {
                             LogerrorMessage($"分片丢失: {tempImgPath}. 使用空白图像替代.");
                             using (Bitmap blankTile = new Bitmap(imgWidth/3, imgHeight/3)) 
                             using (Graphics blankGraphics = Graphics.FromImage(blankTile))
                             {
                                blankGraphics.Clear(Color.LightGray);
                                graph.DrawImage(blankTile, (i % 3) * (imgWidth/3), (i / 3) * (imgHeight/3), imgWidth/3, imgHeight/3);
                             }
                             continue;
                        }
                        using (Image tmpimg = Image.FromFile(tempImgPath))
                        {
                            graph.DrawImage(tmpimg, (i % 3) * tmpimg.Width, (i / 3) * tmpimg.Height, tmpimg.Width, tmpimg.Height);
                        }
                    }
                }

                string outputFilePath = inputName + ".jpg";
                // Calculate scale factor based on quality (10-100 maps to 0.2-1.0)
                float scaleFactor = 0.1f + (quality - 1) * 0.008888f;
                scaleFactor = Math.Clamp(scaleFactor, 0.1f, 1.0f);
                
                int newWidth = (int)(imgWidth * scaleFactor);
                int newHeight = (int)(imgHeight * scaleFactor);
                if (newWidth <=0 || newHeight <=0) {
                    LogerrorMessage($"计算得到的缩放后尺寸无效 ({newWidth}x{newHeight}) for {inputName}. 使用原始尺寸.");
                    newWidth = imgWidth;
                    newHeight = imgHeight;
                }

                using (Bitmap resizedBitmap = new Bitmap(joinedBitmap, new Size(newWidth, newHeight)))
                {
                    // Get JPEG encoder and set quality
                    ImageCodecInfo jpgEncoder = ImageCodecInfo.GetImageEncoders()
                        .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
                    
                    EncoderParameters encoderParams = new EncoderParameters(1);
                    // Reduce quality further by using 70% of slider value (10-100 -> 7-70)
                    int jpegQuality = (int)(quality * 0.7);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
                    
                    resizedBitmap.Save(outputFilePath, jpgEncoder, encoderParams);
                }
                
                joinedBitmap.Dispose();

                await Task.Delay(500); // Shorter delay
                for (int i = 0; i < 9; i++)
                {
                    try
                    {
                        string tempImgPath = inputName + "_img_" + (i + 1) + ".jpg";
                        if (File.Exists(tempImgPath)) File.Delete(tempImgPath);
                    }
                    catch (Exception ex)
                    {
                        LogerrorMessage($"删除临时文件时出错: {inputName + "_img_" + (i + 1) + ".jpg"}: {ex.Message}");
                    }
                }
                LogMessage($"最终图像保存为: {outputFilePath}");
            }
            catch (Exception ex) // Catch specific exceptions if possible
            {
                LogerrorMessage($"文件名太变态无法识别或合并处理失败: {inputName} 建议重命名该文件！详细错误: {ex.Message}");
            }
        }
        
        public async Task GenerateImageAsync(string filePath, bool delimg = false)
        {
            if (delimg)
            {
                string jpgFile = filePath + ".jpg";
                if (File.Exists(jpgFile))
                {
                    try
                    {
                        File.Delete(jpgFile);
                        LogMessage($"删除图像: {filePath}");
                    }
                    catch(Exception ex) 
                    { 
                        LogerrorMessage($"删除图像失败 {jpgFile}: {ex.Message}");
                    }
                }
            }
            else
            {
                if (File.Exists(filePath + ".jpg"))
                {
                    LogMessage($"图像已存在: {filePath}");
                }
                else
                {
                    try
                    {
                        LogMessage($"开始生成图像: {filePath}");
                        await GetImgCapAsync(filePath);
                        // LogMessage is called within GetImgCapAsync and GetJiugonggeAsync
                    }
                    catch (Exception ex)
                    {
                        LogerrorMessage($"生成图像时出错: {filePath}: {ex.Message}");
                    }
                }
            }
        }

        public int CountFiles(DirectoryInfo dir)
        {
            try
            {
                var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".svg" }; 
                int count = dir.GetFiles().Count(file => !imageExtensions.Contains(file.Extension)); 
                foreach (var subDir in dir.GetDirectories())
                {
                    count += CountFiles(subDir);
                }
                return count;
            }
            catch (UnauthorizedAccessException)
            {
                LogerrorMessage($"无权限访问目录 (CountFiles): {dir.FullName}");
                return 0;
            }
            catch (Exception ex)
            {
                LogerrorMessage($"计算文件数时出错({dir.FullName}): {ex.Message}");
                return 0;
            }
        }

        public async Task<int> ProcessDirectoryRecursivelyAsync(DirectoryInfo dir, int processedFiles, int totalFiles, Func<int, int, Task> updateProgress, bool delimg = false)
        {
            try
            {
                var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".svg" };

                foreach (var file in dir.GetFiles().Where(f => !imageExtensions.Contains(f.Extension)))
                {
                    try
                    {
                        await GenerateImageAsync(file.FullName, delimg);
                        processedFiles++;
                        await updateProgress(processedFiles, totalFiles);
                        if (processedFiles % 10 == 0) await Task.Delay(1);
                    }
                    catch (Exception ex)
                    {
                        LogerrorMessage($"处理文件失败 {file.FullName}: {ex.Message}");
                    }
                }
                foreach (var subDir in dir.GetDirectories())
                {
                    try
                    {
                        processedFiles = await ProcessDirectoryRecursivelyAsync(subDir, processedFiles, totalFiles, updateProgress, delimg);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        LogerrorMessage($"无权限访问目录: {subDir.FullName}");
                    }
                }
                return processedFiles;
            }
            catch (Exception ex)
            {
                LogerrorMessage($"处理目录失败 {dir.FullName}: {ex.Message}");
                return processedFiles;
            }
        }

        public async Task ProcessDirectoryAsync(string directoryPath, Func<int, int, Task> updateProgress, bool delimg = false)
        {
            if (!Directory.Exists(directoryPath))
            {
                LogerrorMessage("目录不存在。");
                return;
            }
            DirectoryInfo dir = new DirectoryInfo(directoryPath);
            int totalFiles = CountFiles(dir);
            await updateProgress(0, totalFiles); // Initialize progress
            await ProcessDirectoryRecursivelyAsync(dir, 0, totalFiles, updateProgress, delimg);
            LogMessage("缩略图处理完成。");
        }
    }
}
