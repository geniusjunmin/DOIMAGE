using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms; // Keep for Image class, or specify System.Drawing.Image

namespace DOIMAGE
{
    public static class FileSystemUtils
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint GetLongPathName(string shortPath, StringBuilder longPathBuffer, uint bufferLength);

        public static string GetProperLongPathName(string shortPath)
        {
            StringBuilder longPathBuffer = new StringBuilder(260); // MAX_PATH
            uint result = GetLongPathName(shortPath, longPathBuffer, (uint)longPathBuffer.Capacity);
            if (result > 0 && result < longPathBuffer.Capacity)
            {
                return longPathBuffer.ToString();
            }
            return shortPath; // Return original if failed
        }

        public static List<string> GetAllVideoFiles(string directoryPath, Action<string> logErrorAction)
        {
            var videoFiles = new List<string>();
            // Ensure empty string is not treated as an extension for all files if specific extensions are listed.
            // If "" means "extensionless files that are videos", then it's fine.
            // For clarity, it might be better to check MIME types or use a more robust way to identify videos if "" is problematic.
            var extensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", "" }; 
            try
            {
                foreach (var file in Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (extensions.Contains(ext))
                    {
                        videoFiles.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                logErrorAction?.Invoke($"遍历目录 {directoryPath} 时出错: {ex.Message}");
            }
            return videoFiles;
        }

        public static string GetBase64String(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("指定的文件不存在。", path);
            }
            byte[] buffer = File.ReadAllBytes(path);
            return Convert.ToBase64String(buffer);
        }

        public static bool IsVideoFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            // Consider if "" (extensionless) should always be true or if there's a better check.
            string[] videoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", "" }; 
            return Array.Exists(videoExtensions, e => e == ext);
        }

        public static Image? LoadImageWithoutLock(string filePath, Action<string>? logErrorAction = null)
        {
            if (!File.Exists(filePath))
            {
                logErrorAction?.Invoke($"尝试加载的图片文件不存在: {filePath}");
                return null;
            }
            try
            {
                byte[] imageData = File.ReadAllBytes(filePath);
                using (var stream = new MemoryStream(imageData))
                {
                    return Image.FromStream(stream);
                }
            }
            catch (Exception ex)
            {
                logErrorAction?.Invoke($"加载图片失败 {filePath}: {ex.Message}");
                return null;
            }
        }
    }
} 