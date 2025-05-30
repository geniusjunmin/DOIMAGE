using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DOIMAGE
{
    public class VideoCache
    {
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public TimeSpan Duration { get; set; }
        public List<string> PerceptualHashes { get; set; }
        public string AudioFingerprint { get; set; }
        public string ColorHistogram { get; set; }
        public string AverageHash { get; set; }
        public DateTime LastModified { get; set; }
    }
}
