using System;
using System.Collections.Generic;

namespace DOIMAGE
{
    public class VideoCache
    {
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public TimeSpan Duration { get; set; }
        public List<string> PerceptualHashes { get; set; } = new List<string>();
        public string AudioFingerprint { get; set; } = string.Empty;
        public string ColorHistogram { get; set; } = string.Empty;
        public string AverageHash { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
    }
}
