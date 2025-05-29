using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FFmpeg.NET;
using System.Diagnostics; // Added for Process
using System.Threading; // Added for CancellationTokenSource

namespace DOIMAGE
{
    public class VideoProcessor
    {
        private const double WEIGHT_VISUAL_PHASH = 0.4;
        private const double WEIGHT_VISUAL_AHASH = 0.2;
        private const double WEIGHT_AUDIO = 0.3;
        private const double WEIGHT_COLOR_HISTOGRAM = 0.1;
        public const double SIMILARITY_THRESHOLD = 0.75; // Overall threshold to consider as duplicate
        private const int PHASH_HAMMING_DISTANCE_THRESHOLD = 5;
        private const int MIN_SIMILAR_PHASH_FRAMES = 3; // Min number of pHash frames that need to be similar

        private Action<string> _logErrorAction; // For logging errors
        private string _ffmpegPath;

        public VideoProcessor(string ffmpegPath, Action<string> logErrorAction)
        {
            _ffmpegPath = ffmpegPath;
            _logErrorAction = logErrorAction;
        }

        private void LogerrorMessage(string message)
        {
            _logErrorAction?.Invoke(message);
        }

        public string CalculatePerceptualHash(Image image)
        {
            using (var resized = new Bitmap(image, new Size(32, 32)))
            using (var grayImage = ToGrayscale(resized))
            {
                double[,] pixels = new double[32, 32];
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        pixels[x, y] = grayImage.GetPixel(x, y).R;
                    }
                }
                double[,] dct = ComputeDCT(pixels);
                double sum = 0;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        if (x == 0 && y == 0) continue;
                        sum += dct[x, y];
                    }
                }
                double average = sum / 63;
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

        public async Task<string> ExtractColorHistogramsAsync(string videoPath, int frameCount)
        {
            var allFrameHistograms = new StringBuilder();
            var tempFilesToDelete = new List<string>();
            try
            {
                var inputFile = new MediaFile(videoPath);
                var ffmpeg = new Engine(_ffmpegPath);
                var metadata = await GetMetaDataWithTimeout(ffmpeg, inputFile, 3000);
                if (metadata == null || metadata.Duration.TotalSeconds <= 0)
                {
                    LogerrorMessage($"Could not get metadata or duration for {videoPath} for color histogram.");
                    return string.Empty;
                }
                double totalSeconds = metadata.Duration.TotalSeconds;
                var samplePoints = new List<double>();
                if (frameCount <= 0) frameCount = 1;
                if (totalSeconds < frameCount)
                {
                    for(int i=0; i < (int)totalSeconds; ++i) samplePoints.Add(i + 0.5);
                }
                else
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        samplePoints.Add(totalSeconds * (i + 1.0) / (frameCount + 1.0));
                    }
                }
                if (!samplePoints.Any())
                {
                     samplePoints.Add(totalSeconds / 2.0);
                }
                foreach (var second in samplePoints)
                {
                    var options = new ConversionOptions { Seek = TimeSpan.FromSeconds(second) };
                    var tempFile = Path.GetTempFileName() + ".jpg";
                    tempFilesToDelete.Add(tempFile);
                    var outputFile = new MediaFile(tempFile);
                    await ffmpeg.GetThumbnailAsync(inputFile, outputFile, options);
                    if (!File.Exists(tempFile) || new FileInfo(tempFile).Length == 0)
                    {
                        LogerrorMessage($"FFmpeg failed to extract frame at {second}s for {videoPath}. Skipping frame.");
                        continue;
                    }
                    using (var image = Image.FromFile(tempFile))
                    using (var bitmap = new Bitmap(image))
                    {
                        const int numBins = 4;
                        int[] rHist = new int[numBins];
                        int[] gHist = new int[numBins];
                        int[] bHist = new int[numBins];
                        int sampledPixels = 0;
                        for (int y = 0; y < bitmap.Height; y += 5)
                        {
                            for (int x = 0; x < bitmap.Width; x += 5)
                            {
                                Color pixel = bitmap.GetPixel(x, y);
                                int rBin = Math.Min(numBins - 1, (pixel.R * numBins) / 256);
                                int gBin = Math.Min(numBins - 1, (pixel.G * numBins) / 256);
                                int bBin = Math.Min(numBins - 1, (pixel.B * numBins) / 256);
                                rHist[rBin]++;
                                gHist[gBin]++;
                                bHist[bBin]++;
                                sampledPixels++;
                            }
                        }
                        if (sampledPixels == 0) continue;
                        var frameHistString = new StringBuilder();
                        for(int i=0; i<numBins; ++i) frameHistString.AppendFormat(CultureInfo.InvariantCulture, "{0:F4}{1}", (double)rHist[i]/sampledPixels, i == numBins -1 ? "" : ",");
                        frameHistString.Append(";");
                        for(int i=0; i<numBins; ++i) frameHistString.AppendFormat(CultureInfo.InvariantCulture, "{0:F4}{1}", (double)gHist[i]/sampledPixels, i == numBins -1 ? "" : ",");
                        frameHistString.Append(";");
                        for(int i=0; i<numBins; ++i) frameHistString.AppendFormat(CultureInfo.InvariantCulture, "{0:F4}{1}", (double)bHist[i]/sampledPixels, i == numBins -1 ? "" : ",");
                        if (allFrameHistograms.Length > 0)
                        {
                            allFrameHistograms.Append("|");
                        }
                        allFrameHistograms.Append(frameHistString.ToString());
                    }
                }
                return allFrameHistograms.ToString();
            }
            catch (Exception ex)
            {
                LogerrorMessage($"Error extracting color histograms for {videoPath}: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                foreach (var tempFile in tempFilesToDelete)
                {
                    if (File.Exists(tempFile))
                    {
                        try { File.Delete(tempFile); }
                        catch (Exception ex) { LogerrorMessage($"Error deleting temp histogram file {tempFile}: {ex.Message}"); }
                    }
                }
            }
        }

        public double[,] ComputeDCT(double[,] pixels)
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

        public string CalculateAverageHash(Image image)
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

        public Bitmap ToGrayscale(Bitmap image)
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

        public int HammingDistance(string hash1, string hash2)
        {
            if (hash1 == null || hash2 == null || hash1.Length != hash2.Length) return int.MaxValue;
            int distance = 0;
            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i]) distance++;
            }
            return distance;
        }

        public async Task<string> ExtractAudioFeaturesAsync(string videoPath)
        {
            string tempWavPath = string.Empty;
            try
            {
                tempWavPath = Path.GetTempFileName() + ".wav";
                string arguments = $"-i \"{videoPath}\" -ss 0 -t 60 -vn -acodec pcm_s16le -ar 44100 -ac 1 \"{tempWavPath}\"";
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };
                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    string errorOutput = await process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());
                    if (process.ExitCode != 0)
                    {
                        LogerrorMessage($"FFmpeg error for {videoPath}: {errorOutput}");
                        return string.Empty;
                    }
                }
                if (!File.Exists(tempWavPath) || new FileInfo(tempWavPath).Length == 0)
                {
                    LogerrorMessage($"FFmpeg produced an empty or missing WAV file for {videoPath}.");
                    return string.Empty;
                }
                byte[] audioBytes = await File.ReadAllBytesAsync(tempWavPath);
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hashBytes = sha256.ComputeHash(audioBytes);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in hashBytes)
                    {
                        sb.Append(b.ToString("x2"));
                    }
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                LogerrorMessage($"Error extracting audio features for {videoPath}: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempWavPath) && File.Exists(tempWavPath))
                {
                    try { File.Delete(tempWavPath); }
                    catch (Exception ex) { LogerrorMessage($"Error deleting temporary WAV file {tempWavPath}: {ex.Message}"); }
                }
            }
        }
        
        public async Task<VideoInfo?> GetVideoInfo(string videoPath, Dictionary<string, VideoCache> videoCache, Action<string, VideoCache> updateCacheAction)
        {
            try
            {
                var fileInfo = new FileInfo(videoPath);
                if (videoCache.TryGetValue(videoPath, out var cachedInfo))
                {
                    if (cachedInfo.LastModified == fileInfo.LastWriteTime && cachedInfo.FileSize == fileInfo.Length)
                    {
                        return new VideoInfo
                        {
                            Path = videoPath,
                            FileSize = cachedInfo.FileSize,
                            Duration = cachedInfo.Duration,
                            PerceptualHashes = cachedInfo.PerceptualHashes ?? new List<string>(),
                            AudioFingerprint = cachedInfo.AudioFingerprint ?? string.Empty,
                            ColorHistogram = cachedInfo.ColorHistogram ?? string.Empty,
                            AverageHash = cachedInfo.AverageHash ?? string.Empty
                        };
                    }
                }

                var inputFile = new MediaFile(videoPath);
                var ffmpeg = new Engine(_ffmpegPath);
                var metadata = await GetMetaDataWithTimeout(ffmpeg, inputFile, 3000);
                if (metadata == null) return null;

                var info = new VideoInfo
                {
                    Path = videoPath,
                    FileSize = fileInfo.Length,
                    Duration = metadata.Duration,
                    AudioFingerprint = await ExtractAudioFeaturesAsync(videoPath),
                    ColorHistogram = await ExtractColorHistogramsAsync(videoPath, 5),
                };

                var visualFeatures = await ExtractFrameHashes(videoPath, 5);
                info.PerceptualHashes = visualFeatures.Item1 ?? new List<string>();
                info.AverageHash = visualFeatures.Item2 ?? string.Empty;

                var newCacheEntry = new VideoCache
                {
                    FilePath = videoPath,
                    FileSize = fileInfo.Length,
                    Duration = metadata.Duration,
                    PerceptualHashes = info.PerceptualHashes,
                    AudioFingerprint = info.AudioFingerprint,
                    ColorHistogram = info.ColorHistogram,
                    AverageHash = info.AverageHash,
                    LastModified = fileInfo.LastWriteTime
                };
                updateCacheAction(videoPath, newCacheEntry);

                return info;
            }
            catch (Exception ex)
            {
                LogerrorMessage($"获取视频信息失败: {videoPath}: {ex.Message}");
                return null;
            }
        }

        public async Task<Tuple<List<string>, string>> ExtractFrameHashes(string videoPath, int frameCount)
        {
            var perceptualHashes = new List<string>();
            string representativeAHash = string.Empty;
            var tempFilesToDelete = new List<string>();
            try
            {
                var inputFile = new MediaFile(videoPath);
                var ffmpeg = new Engine(_ffmpegPath);
                var metadata = await GetMetaDataWithTimeout(ffmpeg, inputFile, 3000);
                if (metadata == null || metadata.Duration.TotalSeconds <= 0)
                {
                    LogerrorMessage($"Could not get metadata for {videoPath} for frame hash extraction.");
                    return Tuple.Create(perceptualHashes, representativeAHash);
                }
                double totalSeconds = metadata.Duration.TotalSeconds;
                var samplePoints = new List<double>
                {
                    totalSeconds * 0.2,
                    totalSeconds * 0.4,
                    totalSeconds * 0.5,
                    totalSeconds * 0.6,
                    totalSeconds * 0.8
                };
                for (int i = 0; i < samplePoints.Count; i++)
                {
                    var second = samplePoints[i];
                    var options = new ConversionOptions { Seek = TimeSpan.FromSeconds(second) };
                    var tempFile = Path.GetTempFileName() + ".jpg";
                    tempFilesToDelete.Add(tempFile);
                    var outputFile = new MediaFile(tempFile);
                    await ffmpeg.GetThumbnailAsync(inputFile, outputFile, options);
                    if (!File.Exists(tempFile) || new FileInfo(tempFile).Length == 0)
                    {
                        LogerrorMessage($"FFmpeg failed to extract frame at {second}s for {videoPath} (ExtractFrameHashes). Skipping frame.");
                        continue;
                    }
                    using (var image = Image.FromFile(tempFile))
                    {
                        string pHash = CalculatePerceptualHash(image);
                        perceptualHashes.Add(pHash);
                        if (i == 2) // Middle frame (50% mark)
                        {
                            representativeAHash = CalculateAverageHash(image);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogerrorMessage($"提取帧哈希失败: {videoPath}: {ex.Message}");
            }
            finally
            {
                foreach (var tempFile in tempFilesToDelete)
                {
                    if (File.Exists(tempFile))
                    {
                        try { File.Delete(tempFile); }
                        catch (Exception exDel) { LogerrorMessage($"Error deleting temp file {tempFile} in ExtractFrameHashes: {exDel.Message}"); }
                    }
                }
            }
            return Tuple.Create(perceptualHashes, representativeAHash);
        }
        
        public double CalculateSimilarityScore(VideoInfo info1, VideoInfo info2)
        {
            if (info1 == null || info2 == null) return 0.0;

            double visualSimilarity = 0;
            int similarPHashFrames = 0;
            if (info1.PerceptualHashes != null && info2.PerceptualHashes != null)
            {
                foreach (var hash1 in info1.PerceptualHashes)
                {
                    foreach (var hash2 in info2.PerceptualHashes)
                    {
                        if (HammingDistance(hash1, hash2) <= PHASH_HAMMING_DISTANCE_THRESHOLD)
                        {
                            similarPHashFrames++;
                            break; 
                        }
                    }
                }
            }
            double pHashScore = (info1.PerceptualHashes.Any() && info2.PerceptualHashes.Any()) 
                                ? (double)similarPHashFrames / Math.Min(info1.PerceptualHashes.Count, info2.PerceptualHashes.Count)
                                : 0.0;
            pHashScore = Math.Min(1.0, pHashScore); // Cap at 1.0

            double aHashScore = (!string.IsNullOrEmpty(info1.AverageHash) && !string.IsNullOrEmpty(info2.AverageHash))
                                ? 1.0 - (double)HammingDistance(info1.AverageHash, info2.AverageHash) / info1.AverageHash.Length
                                : 0.0;
            aHashScore = Math.Max(0.0, aHashScore); // Ensure non-negative

            visualSimilarity = (pHashScore * 0.7) + (aHashScore * 0.3); 

            double audioSimilarity = 0;
            if (!string.IsNullOrEmpty(info1.AudioFingerprint) && info1.AudioFingerprint == info2.AudioFingerprint)
            {
                audioSimilarity = 1.0;
            }

            double colorSimilarity = 0;
            if (!string.IsNullOrEmpty(info1.ColorHistogram) && !string.IsNullOrEmpty(info2.ColorHistogram))
            {
                 colorSimilarity = CompareColorHistograms(info1.ColorHistogram, info2.ColorHistogram);
            }
            
            double overallScore = (visualSimilarity * (WEIGHT_VISUAL_PHASH + WEIGHT_VISUAL_AHASH)) +
                                  (audioSimilarity * WEIGHT_AUDIO) +
                                  (colorSimilarity * WEIGHT_COLOR_HISTOGRAM);

            // Additional condition: if pHash similarity is very high, boost score
            if (similarPHashFrames >= MIN_SIMILAR_PHASH_FRAMES && pHashScore > 0.8)
            {
                overallScore = Math.Min(1.0, overallScore + 0.1); // Boost score slightly, cap at 1.0
            }

            return overallScore;
        }

        private double CompareColorHistograms(string histStr1, string histStr2)
        {
            if (string.IsNullOrEmpty(histStr1) || string.IsNullOrEmpty(histStr2)) return 0.0;

            var frameHists1 = histStr1.Split('|');
            var frameHists2 = histStr2.Split('|');
            double totalSimilarity = 0;
            int comparedFrames = 0;

            for (int i = 0; i < Math.Min(frameHists1.Length, frameHists2.Length); i++)
            {
                var ch1 = ParseHistogramFrame(frameHists1[i]);
                var ch2 = ParseHistogramFrame(frameHists2[i]);
                if (ch1 == null || ch2 == null) continue;

                double frameSimilarity = 0;
                for (int j = 0; j < 3; j++) // R, G, B
                {
                    frameSimilarity += CalculateChannelSimilarity(ch1[j], ch2[j]);
                }
                totalSimilarity += frameSimilarity / 3.0; // Average similarity for R, G, B
                comparedFrames++;
            }

            return comparedFrames > 0 ? totalSimilarity / comparedFrames : 0.0;
        }
        
        private List<double[]> ParseHistogramFrame(string frameHistStr)
        {
            try
            {
                var channels = frameHistStr.Split(';');
                if (channels.Length != 3) return null;
                var parsedHist = new List<double[]>();
                foreach (var channel in channels)
                {
                    parsedHist.Add(channel.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray());
                }
                return parsedHist;
            }
            catch { return null; } 
        }

        private double CalculateChannelSimilarity(double[] hist1, double[] hist2)
        {
            if (hist1.Length != hist2.Length) return 0.0;
            // Using Bhattacharyya coefficient for similarity
            double bCoefficient = 0;
            for (int i = 0; i < hist1.Length; i++)
            {
                bCoefficient += Math.Sqrt(hist1[i] * hist2[i]);
            }
            return bCoefficient; 
        }

        private async Task<MetaData?> GetMetaDataWithTimeout(Engine ffmpeg, MediaFile inputFile, int timeoutMilliseconds)
        {
            var cts = new CancellationTokenSource();
            var token = cts.Token;
            var metadataTask = ffmpeg.GetMetaDataAsync(inputFile, token);
            var delayTask = Task.Delay(timeoutMilliseconds, token);
            var completedTask = await Task.WhenAny(metadataTask, delayTask);
            if (completedTask == delayTask)
            {
                cts.Cancel();
                LogerrorMessage($"Timeout getting metadata for {inputFile.FileInfo.FullName}");
                return null;
            }
            try
            {
                return await metadataTask; 
            }
            catch (OperationCanceledException)
            {
                LogerrorMessage($"Metadata retrieval was canceled for {inputFile.FileInfo.FullName}");
                return null;
            }
            catch (Exception ex)
            {
                 LogerrorMessage($"Error getting metadata for {inputFile.FileInfo.FullName}: {ex.Message}");
                 return null;
            }
        }
    }
} 