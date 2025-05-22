using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Added for median calculation

namespace DOIMAGE
{
    public class VideoProcessingException : Exception
    {
        public VideoProcessingException(string message) : base(message) { }
        public VideoProcessingException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class VideoProcessor
    {
        public List<Mat> ExtractFrames(string videoPath, int intervalSeconds)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found.", videoPath);
            }

            List<Mat> frames = new List<Mat>();
            VideoCapture videoCapture = null;

            try
            {
                videoCapture = new VideoCapture(videoPath);
                if (!videoCapture.IsOpened())
                {
                    throw new VideoProcessingException($"Error opening video file: {videoPath}. It might be corrupted or an unsupported format.");
                }

                double fps = videoCapture.Fps;
                if (fps <= 0)
                {
                    throw new VideoProcessingException($"Could not determine FPS for video: {videoPath}. FPS reported as {fps}.");
                }

                int frameSkip = (int)(fps * intervalSeconds);
                if (frameSkip < 1)
                {
                    frameSkip = 1; // Process at least one frame if interval is too short or FPS is high
                }

                Mat frame = new Mat();
                long currentFramePos = 0;
                long totalFrames = videoCapture.FrameCount;

                while (true)
                {
                    // Setting frame position can be slow; only do it if necessary or if it's the primary way to skip.
                    // An alternative is to read frames sequentially and skip them in the loop.
                    // However, for large skips, setting PosFrames is more efficient.
                    if (currentFramePos > 0) // No need to set for the first frame
                    {
                         // Check if currentFramePos is within the valid range of frames
                        if (totalFrames > 0 && currentFramePos >= totalFrames) {
                            break; // Past the last frame
                        }
                        videoCapture.PosFrames = currentFramePos;
                    }

                    if (!videoCapture.Read(frame) || frame.Empty())
                    {
                        break; // End of video or error reading frame
                    }

                    frames.Add(frame.Clone());

                    currentFramePos += frameSkip;
                     if (totalFrames > 0 && currentFramePos >= totalFrames)
                    {
                        break; 
                    }
                    // If FrameCount is not available (some streams), this loop relies on Read returning false.
                }
            }
            catch (Exception ex)
            {
                if (!(ex is FileNotFoundException) && !(ex is VideoProcessingException))
                {
                    throw new VideoProcessingException($"An error occurred during video processing: {ex.Message}", ex);
                }
                throw;
            }
            finally
            {
                videoCapture?.Dispose();
                // frame is disposed automatically by OpenCvSharp when it goes out of scope or by its owner Mat if cloned.
            }

            return frames;
        }

        public Mat ConvertToGrayscale(Mat frame)
        {
            if (frame == null || frame.Empty())
            {
                throw new ArgumentNullException(nameof(frame), "Input frame cannot be null or empty.");
            }

            Mat grayscaleMat = new Mat();
            if (frame.Channels() == 3)
            {
                Cv2.CvtColor(frame, grayscaleMat, ColorConversionCodes.BGR2GRAY);
            }
            else if (frame.Channels() == 4)
            {
                Cv2.CvtColor(frame, grayscaleMat, ColorConversionCodes.BGRA2GRAY);
            }
            else if (frame.Channels() == 1)
            {
                // Already grayscale, just clone it
                grayscaleMat = frame.Clone();
            }
            else
            {
                throw new ArgumentException($"Unsupported number of channels: {frame.Channels()}. Expected BGR (3), BGRA (4), or Grayscale (1).", nameof(frame));
            }
            return grayscaleMat;
        }

        public ulong CalculatePHash(Mat grayscaleFrame)
        {
            if (grayscaleFrame == null || grayscaleFrame.Empty())
            {
                throw new ArgumentNullException(nameof(grayscaleFrame), "Grayscale frame cannot be null or empty.");
            }

            if (grayscaleFrame.Channels() != 1)
            {
                throw new ArgumentException("Input frame for pHash must be single-channel (grayscale).", nameof(grayscaleFrame));
            }

            Mat resized = new Mat();
            Cv2.Resize(grayscaleFrame, resized, new Size(32, 32), 0, 0, InterpolationFlags.Linear);

            Mat resizedFloat = new Mat();
            resized.ConvertTo(resizedFloat, MatType.CV_32F);

            Mat dctResult = new Mat();
            Cv2.Dct(resizedFloat, dctResult, DctFlags.Forward);

            Mat dctRoi = new Mat(dctResult, new Rect(0, 0, 8, 8));

            List<float> coefficients = new List<float>(64);
            for (int r = 0; r < dctRoi.Rows; r++)
            {
                for (int c = 0; c < dctRoi.Cols; c++)
                {
                    coefficients.Add(dctRoi.At<float>(r, c));
                }
            }

            coefficients.Sort();
            float median;
            // For 64 elements, median is the average of 31st and 32nd element (0-indexed) after sorting
            if (coefficients.Count % 2 == 0)
            {
                median = (coefficients[coefficients.Count / 2 - 1] + coefficients[coefficients.Count / 2]) / 2.0f;
            }
            else
            {
                median = coefficients[coefficients.Count / 2];
            }
            
            ulong hash = 0;
            int bitIndex = 0;
            for (int r = 0; r < 8; r++) // Iterate through the 8x8 ROI
            {
                for (int c = 0; c < 8; c++)
                {
                    if (dctRoi.At<float>(r, c) > median)
                    {
                        hash |= (1UL << bitIndex);
                    }
                    bitIndex++;
                }
            }
            
            // Dispose Mat objects that are not returned
            resized.Dispose();
            resizedFloat.Dispose();
            dctResult.Dispose();
            dctRoi.Dispose();

            return hash;
        }
    }
}
