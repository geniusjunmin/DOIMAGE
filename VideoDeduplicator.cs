using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using OpenCvSharp;
using FFmpeg.NET;
using FFmpeg.NET.Enums;
using System.Windows.Forms;

namespace DOIMAGE
{
    public class VideoDeduplicator
    {
        private const int HashSize = 16;
        private const int FrameSampleCount = 10; // Sample 10 frames from video
        private double VisualThreshold = 0.75;
        private double AudioThreshold = 0.85;

        public void SetThresholds(double visualThreshold, double audioThreshold)
        {
            VisualThreshold = Math.Clamp(visualThreshold, 0.5, 1.0);
            AudioThreshold = Math.Clamp(audioThreshold, 0.7, 1.0);
        }

        public async Task<List<List<string>>> FindDuplicatesInGroup(List<VideoInfo> videos)
        {
            var duplicates = new List<List<string>>();
            var processed = new HashSet<string>();
            
            for (int i = 0; i < videos.Count; i++)
            {
                if (processed.Contains(videos[i].Path)) continue;
                
                var duplicateGroup = new List<string> { videos[i].Path };
                
                for (int j = i + 1; j < videos.Count; j++)
                {
                    if (processed.Contains(videos[j].Path)) continue;
                    
                    
                    double durationDiff = Math.Abs(videos[i].Duration.TotalSeconds - videos[j].Duration.TotalSeconds);
                    if (durationDiff > 2) continue;
                    
                    if (await AreVideosSimilar(videos[i].Path, videos[j].Path))
                    {
                        duplicateGroup.Add(videos[j].Path);
                        processed.Add(videos[j].Path);
                    }
                }
                
                if (duplicateGroup.Count > 1)
                {
                    duplicates.Add(duplicateGroup);
                }
                
                processed.Add(videos[i].Path);
            }
            
            return duplicates;
        }

        public bool AreVideosSimilar(VideoInfo video1, VideoInfo video2)
        {
            // 优先检查AverageHash (完全匹配)
            if (!string.IsNullOrEmpty(video1.AverageHash) && 
                !string.IsNullOrEmpty(video2.AverageHash) &&
                video1.AverageHash == video2.AverageHash)
            {
                return true;
            }

            // 其次检查视觉特征
            var visualSimilarity = CompareVisualHashes(video1.PerceptualHashes, video2.PerceptualHashes);
            
            // 最后检查音频特征
            var audioSimilarity = CompareAudioFingerprints(video1.AudioFingerprint, video2.AudioFingerprint);

            // 放宽视觉相似度要求，只要有AverageHash或音频匹配就认为相似
            return (visualSimilarity >= VisualThreshold * 0.8) ||
                   (audioSimilarity >= AudioThreshold * 0.9);
        }

        private double CompareVisualHashes(List<string> hashes1, List<string> hashes2)
        {
            if (!hashes1.Any() || !hashes2.Any())
                return 0;

            int matches = 0;
            int minFrames = Math.Min(hashes1.Count, hashes2.Count);

            for (int i = 0; i < minFrames; i++)
            {
                if (CalculateHammingDistance(hashes1[i], hashes2[i]) <= 5)
                    matches++;
            }

            return (double)matches / minFrames;
        }

        private double CompareAudioFingerprints(string fp1, string fp2)
        {
            if (string.IsNullOrEmpty(fp1) || string.IsNullOrEmpty(fp2))
                return 0;

            return fp1 == fp2 ? 1.0 : 0;
        }

        public async Task<bool> AreVideosSimilar(string videoPath1, string videoPath2)
        {
            // Compare visual similarity
            var visualSimilarity = CompareVisualContent(videoPath1, videoPath2);

            // Compare audio fingerprint
            var audioSimilarity = await CompareAudioContent(videoPath1, videoPath2);

            // Combined similarity score
            return (visualSimilarity >= VisualThreshold) &&
                   (audioSimilarity >= AudioThreshold);
        }

        private double CompareVisualContent(string path1, string path2)
        {
            var hashes1 = ExtractVisualHashes(path1);
            var hashes2 = ExtractVisualHashes(path2);

            if (!hashes1.Any() || !hashes2.Any())
                return 0;

            int matches = 0;
            int minFrames = Math.Min(hashes1.Count, hashes2.Count);

            for (int i = 0; i < minFrames; i++)
            {
                if (CalculateHammingDistance(hashes1[i], hashes2[i]) <= 5)
                    matches++;
            }

            return (double)matches / minFrames;
        }

        private List<string> ExtractVisualHashes(string videoPath)
        {
            var frameHashes = new List<string>();
            using (var capture = new VideoCapture(videoPath))
            {
                if (!capture.IsOpened())
                    return frameHashes;

                double duration = capture.FrameCount / capture.Fps;
                var samplePoints = GetSamplePoints(duration);

                foreach (var second in samplePoints)
                {
                    capture.PosFrames = (int)(second * capture.Fps);
                    using (Mat frame = new Mat())
                    {
                        if (capture.Read(frame) && !frame.Empty())
                        {
                            frameHashes.Add(CalculateFrameHash(frame));
                        }
                    }
                }
            }
            return frameHashes;
        }

        private List<double> GetSamplePoints(double duration)
        {
            // Sample key points throughout the video
            var points = new List<double>();
            for (int i = 1; i <= FrameSampleCount; i++)
            {
                points.Add(duration * i / (FrameSampleCount + 1));
            }
            return points;
        }

        private async Task<double> CompareAudioContent(string path1, string path2)
        {
            try
            {
                var audio1 = await GetAudioFingerprint(path1);
                var audio2 = await GetAudioFingerprint(path2);

                if (string.IsNullOrEmpty(audio1) || string.IsNullOrEmpty(audio2))
                    return 0;

                return audio1 == audio2 ? 1.0 : 0;
            }
            catch
            {
                return 0;
            }
        }

        private async Task<string> GetAudioFingerprint(string videoPath)
        {
            try
            {
                var tempFile = Path.GetTempFileName();
                try
                {
                    // Extract audio using FFmpeg
                    var ffmpeg = new Engine(Path.Combine(Application.StartupPath, "ffmpeg.exe"));
                    var input = new MediaFile(videoPath);
                    var output = new MediaFile(tempFile);

                    // Convert to WAV with standard settings
                    var options = new ConversionOptions
                    {
                        AudioSampleRate = AudioSampleRate.Hz44100,
                        AudioBitRate = 128000
                    };

                    // Extract audio using ExecuteAsync with FFmpeg arguments
                    var args = $"-i \"{input.FileInfo.FullName}\" -vn -ac 1 -ar 44100 -ab 128k \"{output.FileInfo.FullName}\"";
                    await ffmpeg.ExecuteAsync(args);

                    // Compute MD5 hash of audio file
                    using (var md5 = MD5.Create())
                    using (var stream = File.OpenRead(tempFile))
                    {
                        var hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLower();
                    }
                }
                finally
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private string CalculateFrameHash(Mat frame)
        {
            using (Mat gray = new Mat())
            using (Mat resized = new Mat())
            using (Mat blurred = new Mat())
            {
                // Convert to grayscale
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

                // Resize with high-quality interpolation
                Cv2.Resize(gray, resized, new OpenCvSharp.Size(HashSize, HashSize), 0, 0, InterpolationFlags.Cubic);

                // Apply Gaussian blur to reduce noise
                Cv2.GaussianBlur(resized, blurred, new OpenCvSharp.Size(3, 3), 1);

                // Calculate median instead of average for better robustness
                byte[] pixels = new byte[HashSize * HashSize];
                blurred.GetArray(out pixels);
                Array.Sort(pixels);
                byte median = pixels[pixels.Length / 2];

                // Generate hash with threshold around median
                return string.Concat(pixels.Select(p => p > median ? "1" : "0"));
            }
        }

        private int CalculateHammingDistance(string hash1, string hash2)
        {
            if (hash1.Length != hash2.Length)
                return int.MaxValue;

            return hash1.Zip(hash2, (a, b) => a != b ? 1 : 0).Sum();
        }
    }
}
