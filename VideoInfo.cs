using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DOIMAGE
{
    public class VideoInfo
    {
        public string Path { get; set; }
        public long FileSize { get; set; }
        public TimeSpan Duration { get; set; }
        public List<string> PerceptualHashes { get; set; } = new List<string>();
        public string AudioFingerprint { get; set; } = string.Empty;
        public string ColorHistogram { get; set; } = string.Empty;
        public string AverageHash { get; set; } = string.Empty;
    }
}
