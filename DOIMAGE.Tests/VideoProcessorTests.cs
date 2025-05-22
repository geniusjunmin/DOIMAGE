using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenCvSharp;
using DOIMAGE;
using System;
using System.IO;
using System.Collections.Generic;

namespace DOIMAGE.Tests
{
    [TestClass]
    public class VideoProcessorTests
    {
        private VideoProcessor _videoProcessor;

        [TestInitialize]
        public void TestInitialize()
        {
            _videoProcessor = new VideoProcessor();
        }

        private Mat CreateDummyMat(int rows, int cols, int channels, byte initialValue = 0)
        {
            MatType type;
            if (channels == 1) type = MatType.CV_8UC1;
            else if (channels == 3) type = MatType.CV_8UC3;
            else if (channels == 4) type = MatType.CV_8UC4;
            else throw new ArgumentException("Unsupported channel count for dummy Mat.");

            return new Mat(rows, cols, type, Scalar.All(initialValue));
        }
        
        private Mat CreateSpecificGrayscaleMat(int size, byte[] data)
        {
            if (data.Length != size * size)
                throw new ArgumentException("Data length must match size * size.");

            var mat = new Mat(size, size, MatType.CV_8UC1);
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    mat.Set(i, j, data[i * size + j]);
                }
            }
            return mat;
        }


        [TestMethod]
        public void ExtractFrames_InvalidPath_ThrowsFileNotFoundException()
        {
            string invalidPath = "non_existent_video.mp4";
            Assert.ThrowsException<FileNotFoundException>(() => _videoProcessor.ExtractFrames(invalidPath, 1));
        }

        [TestMethod]
        public void ExtractFrames_ValidVideo_ReturnsFrames_Conceptual()
        {
            // This test is conceptual as it requires a sample video file.
            // If a sample video (e.g., "sample.mp4") were available in the test execution directory:
            // string sampleVideoPath = "sample.mp4"; 
            // File.WriteAllBytes(sampleVideoPath, new byte[] { /* minimal valid video data */ }); // Create a dummy video file
            //
            // List<Mat> frames = null;
            // try
            // {
            //    frames = _videoProcessor.ExtractFrames(sampleVideoPath, 1);
            //    Assert.IsNotNull(frames);
            //    Assert.IsTrue(frames.Count > 0, "Expected at least one frame from a valid video.");
            //    // Further checks: frame dimensions, type, etc.
            // }
            // finally
            // {
            //    frames?.ForEach(frame => frame.Dispose());
            //    // if (File.Exists(sampleVideoPath)) File.Delete(sampleVideoPath);
            // }
            Assert.Inconclusive("Conceptual test: Requires a sample video file and valid OpenCvSharp video processing setup.");
        }

        [TestMethod]
        public void ConvertToGrayscale_ValidBGRFrame_ReturnsGrayscale()
        {
            Mat bgrFrame = null;
            Mat grayscaleFrame = null;
            try
            {
                bgrFrame = CreateDummyMat(10, 10, 3); // 3 channels for BGR
                grayscaleFrame = _videoProcessor.ConvertToGrayscale(bgrFrame);
                Assert.IsNotNull(grayscaleFrame);
                Assert.AreEqual(1, grayscaleFrame.Channels());
                Assert.AreEqual(bgrFrame.Rows, grayscaleFrame.Rows);
                Assert.AreEqual(bgrFrame.Cols, grayscaleFrame.Cols);
            }
            finally
            {
                bgrFrame?.Dispose();
                grayscaleFrame?.Dispose();
            }
        }

        [TestMethod]
        public void ConvertToGrayscale_ValidBGRAFrame_ReturnsGrayscale()
        {
            Mat bgraFrame = null;
            Mat grayscaleFrame = null;
            try
            {
                bgraFrame = CreateDummyMat(10, 10, 4); // 4 channels for BGRA
                grayscaleFrame = _videoProcessor.ConvertToGrayscale(bgraFrame);
                Assert.IsNotNull(grayscaleFrame);
                Assert.AreEqual(1, grayscaleFrame.Channels());
            }
            finally
            {
                bgraFrame?.Dispose();
                grayscaleFrame?.Dispose();
            }
        }
        
        [TestMethod]
        public void ConvertToGrayscale_AlreadyGrayscaleFrame_ReturnsClone()
        {
            Mat originalGrayscaleFrame = null;
            Mat resultGrayscaleFrame = null;
            try
            {
                originalGrayscaleFrame = CreateDummyMat(10, 10, 1); // 1 channel
                resultGrayscaleFrame = _videoProcessor.ConvertToGrayscale(originalGrayscaleFrame);
                Assert.IsNotNull(resultGrayscaleFrame);
                Assert.AreEqual(1, resultGrayscaleFrame.Channels());
                Assert.AreNotSame(originalGrayscaleFrame, resultGrayscaleFrame, "Should return a clone, not the same instance.");
                // Optionally, compare data if necessary, but structure is primary here.
            }
            finally
            {
                originalGrayscaleFrame?.Dispose();
                resultGrayscaleFrame?.Dispose();
            }
        }
        
        [TestMethod]
        public void ConvertToGrayscale_UnsupportedChannels_ThrowsArgumentException()
        {
            Mat unsupportedFrame = null;
            try
            {
                // Create a Mat with an unsupported number of channels (e.g., 2)
                // This is tricky as MatType doesn't directly support 2 channels easily for typical image formats.
                // We can simulate by trying to convert a Mat that is not 1, 3, or 4 channels.
                // However, the VideoProcessor's ConvertToGrayscale explicitly checks for 1, 3, 4.
                // A direct test would be creating a Mat and setting its channel property if possible,
                // but OpenCvSharp's Mat creation is tied to MatType.
                // For now, this test highlights the intent. A more robust way might involve advanced Mat manipulation if needed.
                // The current implementation correctly throws for channels not 1, 3, or 4.
                // This path is hard to hit with standard Mat creation if not 1, 3, or 4.
                // We'll assume the existing checks cover this; direct creation of a 2-channel image Mat is non-standard.
                 unsupportedFrame = new Mat(10, 10, MatType.CV_8UC2); // Example of a 2-channel Mat
                 Assert.ThrowsException<ArgumentException>(() => _videoProcessor.ConvertToGrayscale(unsupportedFrame));

            }
            catch(OpenCVException ex)
            {
                // Catching OpenCVException because creating a Mat with 2 channels (CV_8UC2) might be unsupported
                // by some OpenCV operations or not what CvtColor expects if not explicitly handled.
                // The ConvertToGrayscale method itself should throw ArgumentException before CvtColor if channels != 1,3,4.
                // This assertion ensures our method's validation is hit.
                 Assert.IsTrue(ex.Message.Contains("Unsupported number of channels") || ex.Message.Contains("could not be implicitly converted"), "Exception should indicate channel issue or conversion problem if ArgumentException isn't hit first.");

            }
            finally
            {
                unsupportedFrame?.Dispose();
            }
             // Re-asserting the specific exception type from our code if the above try-catch was for Mat creation issues
            // This is a bit complex due to how Mat creation and channel support works.
            // The primary goal is to test the explicit channel checks in ConvertToGrayscale.
            // If Mat creation with CV_8UC2 itself fails or is problematic, this test needs refinement.
            // Let's assume for a moment CV_8UC2 is a valid Mat but not one our ConvertToGrayscale supports.
            Mat twoChannelMat = null;
            try 
            {
                twoChannelMat = new Mat(10,10, MatType.CV_8UC2, Scalar.All(0));
                 Assert.ThrowsException<ArgumentException>(() => _videoProcessor.ConvertToGrayscale(twoChannelMat));
            }
            finally
            {
                twoChannelMat?.Dispose();
            }

        }


        [TestMethod]
        public void CalculatePHash_IdenticalSmallGrayscaleImages_ReturnSameHash()
        {
            Mat img1 = null;
            Mat img2 = null;
            try
            {
                // pHash input must be 32x32 internally after resize, but input to pHash can be smaller (e.g., 8x8)
                // The CalculatePHash method resizes to 32x32. Let's use 8x8 as initial.
                byte[] data = new byte[64];
                for(int i=0; i<64; ++i) data[i] = (byte)(i % 256); // Some arbitrary pattern

                img1 = CreateSpecificGrayscaleMat(8, data);
                img2 = CreateSpecificGrayscaleMat(8, data); // Identical data

                ulong hash1 = _videoProcessor.CalculatePHash(img1);
                ulong hash2 = _videoProcessor.CalculatePHash(img2);

                Assert.AreEqual(hash1, hash2, "Identical images should produce identical pHashes.");
            }
            finally
            {
                img1?.Dispose();
                img2?.Dispose();
            }
        }

        [TestMethod]
        public void CalculatePHash_DifferentSmallGrayscaleImages_ReturnDifferentHashes()
        {
            Mat img1 = null;
            Mat img2 = null;
            try
            {
                byte[] data1 = new byte[64];
                for(int i=0; i<64; ++i) data1[i] = (byte)(i % 256);
                
                byte[] data2 = new byte[64];
                for(int i=0; i<64; ++i) data2[i] = (byte)((i+10) % 256); // Slightly different data

                img1 = CreateSpecificGrayscaleMat(8, data1);
                img2 = CreateSpecificGrayscaleMat(8, data2);


                ulong hash1 = _videoProcessor.CalculatePHash(img1);
                ulong hash2 = _videoProcessor.CalculatePHash(img2);

                Assert.AreNotEqual(hash1, hash2, "Different images should ideally produce different pHashes.");
                // Note: pHash can have collisions for very similar images or by chance.
                // This test is more of a sanity check. A robust test would use images known to differ by a few bits in pHash.
            }
            finally
            {
                img1?.Dispose();
                img2?.Dispose();
            }
        }

        [TestMethod]
        public void CalculatePHash_NullInput_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _videoProcessor.CalculatePHash(null));
        }

        [TestMethod]
        public void CalculatePHash_MultiChannelInput_ThrowsArgumentException()
        {
            Mat multiChannelMat = null;
            try
            {
                multiChannelMat = CreateDummyMat(8, 8, 3); // 3-channel BGR
                Assert.ThrowsException<ArgumentException>(() => _videoProcessor.CalculatePHash(multiChannelMat));
            }
            finally
            {
                multiChannelMat?.Dispose();
            }
        }
    }
}
