using System;
using System.Collections.Generic;
using System.Linq; // Added for FirstOrDefault
using OpenCvSharp; // Required for Mat

namespace DOIMAGE
{
    public class DuplicateDetector
    {
        private List<VideoFingerprint> _processedVideos = new List<VideoFingerprint>();
        private readonly VideoProcessor _videoProcessor = new VideoProcessor();

        public VideoFingerprint ProcessVideo(string videoPath, int intervalSeconds)
        {
            // Check if the video has already been processed
            VideoFingerprint existingFingerprint = _processedVideos.FirstOrDefault(vf => vf.FilePath == videoPath);
            if (existingFingerprint != null)
            {
                return existingFingerprint; // Return existing if found
            }

            // If not found, process it
            List<Mat> extractedFrames = null;
            VideoFingerprint videoFingerprint = new VideoFingerprint(videoPath);

            try
            {
                extractedFrames = _videoProcessor.ExtractFrames(videoPath, intervalSeconds);

                foreach (Mat frame in extractedFrames)
                {
                    Mat grayscaleFrame = null;
                    try
                    {
                        grayscaleFrame = _videoProcessor.ConvertToGrayscale(frame);
                        ulong pHash = _videoProcessor.CalculatePHash(grayscaleFrame);
                        videoFingerprint.FrameHashes.Add(pHash);
                    }
                    finally
                    {
                        grayscaleFrame?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new VideoProcessingException($"Error processing video {videoPath}: {ex.Message}", ex);
            }
            finally
            {
                if (extractedFrames != null)
                {
                    foreach (Mat frame in extractedFrames)
                    {
                        frame.Dispose(); 
                    }
                }
            }

            _processedVideos.Add(videoFingerprint); // Add the new fingerprint to the list
            return videoFingerprint;
        }

        public void AddVideoToLibrary(string videoPath, int intervalSeconds)
        {
            // ProcessVideo will handle checking if it already exists and add if not.
            ProcessVideo(videoPath, intervalSeconds);
        }

        public List<string> FindDuplicates(string newVideoPath, int intervalSeconds, int maxHashDistance, double minMatchPercentage)
        {
            // Process the new video (or get its existing fingerprint). This also adds it to _processedVideos if new.
            // If ProcessVideo throws (e.g. file not found), the exception will propagate as required.
            VideoFingerprint newVideoFingerprint = ProcessVideo(newVideoPath, intervalSeconds);

            List<string> duplicateVideoPaths = new List<string>();

            foreach (VideoFingerprint existingVideoFingerprint in _processedVideos)
            {
                // Skip self-comparison
                if (existingVideoFingerprint.FilePath == newVideoFingerprint.FilePath)
                {
                    continue;
                }

                if (AreVideosSimilar(newVideoFingerprint, existingVideoFingerprint, maxHashDistance, minMatchPercentage))
                {
                    duplicateVideoPaths.Add(existingVideoFingerprint.FilePath);
                }
            }

            return duplicateVideoPaths;
        }


        public List<VideoFingerprint> GetProcessedVideoFingerprints()
        {
            return new List<VideoFingerprint>(_processedVideos);
        }

        public static int CalculateHammingDistance(ulong hash1, ulong hash2)
        {
            ulong xorResult = hash1 ^ hash2;
            int distance = 0;
            while (xorResult > 0)
            {
                distance += (int)(xorResult & 1);
                xorResult >>= 1;
            }
            return distance;
        }

        public bool AreVideosSimilar(VideoFingerprint video1, VideoFingerprint video2, int maxHashDistance, double minMatchPercentage)
        {
            if (video1 == null || video2 == null)
            {
                return false; 
            }

            bool video1HashesEmpty = video1.FrameHashes == null || video1.FrameHashes.Count == 0;
            bool video2HashesEmpty = video2.FrameHashes == null || video2.FrameHashes.Count == 0;

            if (video1HashesEmpty && video2HashesEmpty)
            {
                return true; 
            }

            if (video1HashesEmpty || video2HashesEmpty)
            {
                return false; 
            }

            if (minMatchPercentage < 0.0 || minMatchPercentage > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(minMatchPercentage), "minMatchPercentage must be between 0.0 and 1.0.");
            }
             if (maxHashDistance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHashDistance), "maxHashDistance cannot be negative.");
            }

            int comparisonFrames = Math.Min(video1.FrameHashes.Count, video2.FrameHashes.Count);

            if (comparisonFrames == 0) 
            {
                 return minMatchPercentage == 0.0; 
            }

            int matchingFrames = 0;
            for (int i = 0; i < comparisonFrames; i++)
            {
                int distance = CalculateHammingDistance(video1.FrameHashes[i], video2.FrameHashes[i]);
                if (distance <= maxHashDistance)
                {
                    matchingFrames++;
                }
            }

            double actualMatchPercentage = (double)matchingFrames / comparisonFrames;
            return actualMatchPercentage >= minMatchPercentage;
        }
    }
}
