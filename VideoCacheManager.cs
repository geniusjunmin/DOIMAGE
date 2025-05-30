using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DOIMAGE
{
    public class VideoCacheManager
    {
        private const string CACHE_FILE = "video_cache.json";
        private Dictionary<string, VideoCache> _videoCache = new Dictionary<string, VideoCache>();
        private Action<string> _logErrorAction;

        public VideoCacheManager(Action<string> logErrorAction)
        {
            _logErrorAction = logErrorAction ?? throw new ArgumentNullException(nameof(logErrorAction));
        }

        public Dictionary<string, VideoCache> GetCache()
        {
            return _videoCache;
        }

        public void LoadVideoCache(string directoryPath)
        {
            try
            {
                if (File.Exists(CACHE_FILE))
                {
                    string json = File.ReadAllText(CACHE_FILE);
                    var allCache = JsonSerializer.Deserialize<Dictionary<string, VideoCache>>(json);
                    _videoCache = allCache
                        .Where(kvp => kvp.Key.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    _logErrorAction?.Invoke($"已加载目录缓存，包含 {_videoCache.Count} 个视频信息");
                }
            }
            catch (Exception ex)
            {
                _logErrorAction?.Invoke($"加载缓存失败: {ex.Message}");
                _videoCache = new Dictionary<string, VideoCache>();
            }
        }

        public void SaveVideoCache()
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
                foreach (var kvp in _videoCache)
                {
                    allCache[kvp.Key] = kvp.Value;
                }
                string newJson = JsonSerializer.Serialize(allCache);
                File.WriteAllText(CACHE_FILE, newJson);
                _logErrorAction?.Invoke($"已保存视频缓存，当前目录包含 {_videoCache.Count} 个视频信息");
            }
            catch (Exception ex)
            {
                _logErrorAction?.Invoke($"保存缓存失败: {ex.Message}");
            }
        }

        public void UpdateCache(string path, VideoCache cacheEntry)
        {
            _videoCache[path] = cacheEntry;
        }
    }
} 