using System;
using System.Collections.Generic;

namespace DOIMAGE
{
    public class VideoFingerprint
    {
        public string FilePath { get; set; }
        public List<ulong> FrameHashes { get; set; }

        public VideoFingerprint(string filePath)
        {
            FilePath = filePath;
            FrameHashes = new List<ulong>();
        }
    }
}
