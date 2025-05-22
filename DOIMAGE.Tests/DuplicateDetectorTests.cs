using Microsoft.VisualStudio.TestTools.UnitTesting;
using DOIMAGE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection; // For accessing private members in tests

namespace DOIMAGE.Tests
{
    [TestClass]
    public class DuplicateDetectorTests
    {
        private DuplicateDetector _detector;

        [TestInitialize]
        public void TestInitialize()
        {
            _detector = new DuplicateDetector();
        }

        // Helper to manually add to _processedVideos for testing FindDuplicates logic directly
        private void AddFingerprintToDetector(DuplicateDetector detector, VideoFingerprint fingerprint)
        {
            var field = typeof(DuplicateDetector).GetField("_processedVideos", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = field.GetValue(detector) as List<VideoFingerprint>;
            list.Add(fingerprint);
        }
        
        // Helper to manually set _processedVideos for testing FindDuplicates logic directly
        private void SetFingerprintsInDetector(DuplicateDetector detector, List<VideoFingerprint> fingerprints)
        {
            var field = typeof(DuplicateDetector).GetField("_processedVideos", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(detector, fingerprints);
        }


        [TestMethod]
        public void CalculateHammingDistance_ZeroForSameHash()
        {
            ulong hash = 0x123456789ABCDEF0;
            Assert.AreEqual(0, DuplicateDetector.CalculateHammingDistance(hash, hash));
        }

        [TestMethod]
        public void CalculateHammingDistance_CorrectForKnownValues()
        {
            Assert.AreEqual(2, DuplicateDetector.CalculateHammingDistance(0b1100, 0b1010)); // 12 vs 10
            Assert.AreEqual(1, DuplicateDetector.CalculateHammingDistance(0b1, 0b0));        // 1 vs 0
            Assert.AreEqual(64, DuplicateDetector.CalculateHammingDistance(ulong.MaxValue, ulong.MinValue));
            Assert.AreEqual(0, DuplicateDetector.CalculateHammingDistance(0,0));
        }

        [TestMethod]
        public void CalculateHammingDistance_Symmetric()
        {
            ulong hash1 = 0xF0F0F0F0F0F0F0F0;
            ulong hash2 = 0x0F0F0F0F0F0F0F0F;
            Assert.AreEqual(DuplicateDetector.CalculateHammingDistance(hash1, hash2), DuplicateDetector.CalculateHammingDistance(hash2, hash1));
        }


        [TestMethod]
        public void AreVideosSimilar_IdenticalFingerprints_ReturnsTrue()
        {
            var fp1 = new VideoFingerprint("video1.mp4") { FrameHashes = new List<ulong> { 0x1, 0x2, 0x3 } };
            var fp2 = new VideoFingerprint("video2.mp4") { FrameHashes = new List<ulong> { 0x1, 0x2, 0x3 } };
            Assert.IsTrue(_detector.AreVideosSimilar(fp1, fp2, 0, 1.0));
        }

        [TestMethod]
        public void AreVideosSimilar_SlightlyDifferentHashes_WithinThreshold_ReturnsTrue()
        {
            var fp1 = new VideoFingerprint("v1") { FrameHashes = new List<ulong> { 0x1, 0x2, 0x3 } }; // 0011
            var fp2 = new VideoFingerprint("v2") { FrameHashes = new List<ulong> { 0x1, 0x2, 0x7 } }; // 0111 (dist 1 from 0x3)
            // 2 out of 3 frames are identical (dist 0), 1 frame has dist 1.
            // All frames have dist <= 1. So 100% match if maxHashDistance=1.
            Assert.IsTrue(_detector.AreVideosSimilar(fp1, fp2, maxHashDistance: 1, minMatchPercentage: 0.99)); // 3/3 match
            Assert.IsTrue(_detector.AreVideosSimilar(fp1, fp2, maxHashDistance: 1, minMatchPercentage: 0.66)); // 2/3 would be 0.66, 3/3 is 1.0
        }

        [TestMethod]
        public void AreVideosSimilar_SlightlyDifferentHashes_OutsideThreshold_ReturnsFalse()
        {
            var fp1 = new VideoFingerprint("v1") { FrameHashes = new List<ulong> { 0x1, 0x2, 0x3 } };
            var fp2 = new VideoFingerprint("v2") { FrameHashes = new List<ulong> { 0x1, 0x2, 0x7 } }; // dist 1 for 3rd frame
            Assert.IsFalse(_detector.AreVideosSimilar(fp1, fp2, maxHashDistance: 0, minMatchPercentage: 1.0)); // Needs all frames dist 0
            Assert.IsFalse(_detector.AreVideosSimilar(fp1, fp2, maxHashDistance: 0, minMatchPercentage: 0.70)); // 2/3 match (66%) not enough
        }
        
        [TestMethod]
        public void AreVideosSimilar_DifferentNumberOfHashes_HandledCorrectly()
        {
            var fp1 = new VideoFingerprint("v1") { FrameHashes = new List<ulong> { 0x1, 0x2, 0x3, 0x4 } };
            var fp2 = new VideoFingerprint("v2") { FrameHashes = new List<ulong> { 0x1, 0x2 } };
            // Compares first 2 frames. Both match with dist 0. 2/2 = 100%
            Assert.IsTrue(_detector.AreVideosSimilar(fp1, fp2, maxHashDistance: 0, minMatchPercentage: 1.0));
            Assert.IsTrue(_detector.AreVideosSimilar(fp2, fp1, maxHashDistance: 0, minMatchPercentage: 1.0)); // Symmetric

            var fp3 = new VideoFingerprint("v3") { FrameHashes = new List<ulong> { 0x1, 0x99 } }; // 0x99 is different
            // Compares first 2 frames. 1 matches, 1 differs. 1/2 = 50%
            Assert.IsFalse(_detector.AreVideosSimilar(fp1, fp3, maxHashDistance: 0, minMatchPercentage: 1.0));
            Assert.IsTrue(_detector.AreVideosSimilar(fp1, fp3, maxHashDistance: 0, minMatchPercentage: 0.5));
            Assert.IsFalse(_detector.AreVideosSimilar(fp1, fp3, maxHashDistance: 0, minMatchPercentage: 0.51));
        }

        [TestMethod]
        public void AreVideosSimilar_EmptyHashes_ReturnsAppropriateValue()
        {
            var fpEmpty1 = new VideoFingerprint("empty1") { FrameHashes = new List<ulong>() };
            var fpEmpty2 = new VideoFingerprint("empty2") { FrameHashes = new List<ulong>() };
            var fpNonEmpty = new VideoFingerprint("nonempty") { FrameHashes = new List<ulong> { 0x1 } };

            Assert.IsTrue(_detector.AreVideosSimilar(fpEmpty1, fpEmpty2, 0, 1.0), "Two empty should be similar");
            Assert.IsTrue(_detector.AreVideosSimilar(fpEmpty1, fpEmpty2, 0, 0.0), "Two empty should be similar even with 0% threshold");
            Assert.IsFalse(_detector.AreVideosSimilar(fpEmpty1, fpNonEmpty, 0, 1.0), "Empty and non-empty should not be similar");
            Assert.IsFalse(_detector.AreVideosSimilar(fpNonEmpty, fpEmpty1, 0, 0.0), "Non-empty and empty should not be similar if threshold > 0, unless comparisonFrames is 0 and minMatch is 0");
            
            // Specific check for minMatchPercentage = 0 when comparisonFrames is 0
            Assert.IsTrue(_detector.AreVideosSimilar(fpEmpty1, fpEmpty2, 0, 0.0)); // Both empty, comparisonFrames = 0, should be true if minMatchPercentage is 0.0
            
            // When one is empty, comparisonFrames is 0, so it depends on the interpretation of (0 matching / 0 total)
            // Current implementation: if one is empty and other not, returns false.
            // If both are empty, returns true.
            // If comparisonFrames results in 0 (because one list is empty, or both are), then minMatchPercentage == 0.0 makes it true.
            // Let's test the specific logic:
            // if (comparisonFrames == 0) return minMatchPercentage == 0.0;
            // This means if one is empty, and other not, comparisonFrames is 0.
            // So, AreVideosSimilar(fpEmpty1, fpNonEmpty, 0, 0.0) should be true according to that line.
            // However, the preceding checks:
            //  if (video1HashesEmpty && video2HashesEmpty) return true;
            //  if (video1HashesEmpty || video2HashesEmpty) return false; <--- This takes precedence
            // So, empty vs non-empty is always false.
            Assert.IsFalse(_detector.AreVideosSimilar(fpEmpty1, fpNonEmpty, 0, 0.0), "Empty vs NonEmpty is false due to early exit");
        }
        
        [TestMethod]
        public void AreVideosSimilar_NullFingerprintsOrHashes_HandlesGracefully()
        {
            var fp1 = new VideoFingerprint("v1") { FrameHashes = new List<ulong> { 1 } };
            Assert.IsFalse(_detector.AreVideosSimilar(null, fp1, 0, 1.0));
            Assert.IsFalse(_detector.AreVideosSimilar(fp1, null, 0, 1.0));
            Assert.IsFalse(_detector.AreVideosSimilar(null, null, 0, 1.0)); // Current logic returns false, could be true.

            var fpNoHashes1 = new VideoFingerprint("v_no_hashes1") { FrameHashes = null };
            var fpNoHashes2 = new VideoFingerprint("v_no_hashes2") { FrameHashes = null };
            Assert.IsTrue(_detector.AreVideosSimilar(fpNoHashes1, fpNoHashes2, 0, 1.0)); // Both null hashes, similar
            Assert.IsFalse(_detector.AreVideosSimilar(fp1, fpNoHashes1, 0, 1.0));
        }
        
        [TestMethod]
        public void AreVideosSimilar_ArgumentValidation_ThrowsExceptions()
        {
            var fp1 = new VideoFingerprint("v1") { FrameHashes = new List<ulong> { 1 } };
            var fp2 = new VideoFingerprint("v2") { FrameHashes = new List<ulong> { 1 } };

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _detector.AreVideosSimilar(fp1, fp2, 0, -0.1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _detector.AreVideosSimilar(fp1, fp2, 0, 1.1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => _detector.AreVideosSimilar(fp1, fp2, -1, 0.5));
        }


        [TestMethod]
        public void FindDuplicates_NoExistingVideos_ReturnsEmptyList_Conceptual()
        {
            // This test depends on how ProcessVideo behaves with a non-existent file.
            // If ProcessVideo throws FileNotFoundException, then FindDuplicates will also throw it.
            // For this test to pass as "returns empty list", ProcessVideo must not throw
            // or it must be mocked.
            // Given current constraints, if "new_video.mp4" doesn't exist, ProcessVideo will throw.

            // To test the logic of FindDuplicates itself (i.e., comparing against an empty _processedVideos list):
            var detector = new DuplicateDetector(); // Fresh detector, _processedVideos is empty
            var newFp = new VideoFingerprint("new_video.mp4") { FrameHashes = { 1, 2, 3 } };
            
            // We need to simulate that "new_video.mp4" was processed and resulted in newFp,
            // and that it's now the ONLY video in _processedVideos when FindDuplicates starts its loop.
            // The refined ProcessVideo adds the video. So if we could mock it to return newFp for "new_video.mp4",
            // then _processedVideos would contain only newFp.
            // Then the loop `foreach (VideoFingerprint existingVideoFingerprint in _processedVideos)`
            // would find only newFp, and the `if (existingVideoFingerprint.FilePath == newVideoFingerprint.FilePath)`
            // check would cause it to be skipped, resulting in an empty list.

            // Using reflection to simulate the state *after* newFp has been processed and added:
            SetFingerprintsInDetector(detector, new List<VideoFingerprint> { newFp });

            // Now, call FindDuplicates. It will use the newFp we "pre-processed" and "added".
            // The internal call to ProcessVideo("new_video.mp4",...) should find newFp from our setup
            // and return it, not try to re-process.
            List<string> duplicates = detector.FindDuplicates("new_video.mp4", 10, 0, 1.0);
            Assert.IsNotNull(duplicates);
            Assert.AreEqual(0, duplicates.Count);
            Assert.Inconclusive("Test relies on ProcessVideo behavior for non-existent files or needs robust mocking of VideoProcessor. Current version uses reflection to test FindDuplicates's core loop logic assuming ProcessVideo works as designed (checks existing).");
        }


        [TestMethod]
        public void FindDuplicates_WithSimilarVideo_ReturnsDuplicate()
        {
            var detector = new DuplicateDetector();
            var existingFp = new VideoFingerprint("exist.mp4") { FrameHashes = { 0x1, 0x2, 0x3 } };
            var newFpTemplate = new VideoFingerprint("new_video.mp4") { FrameHashes = { 0x1, 0x2, 0x7 } }; // Similar to existingFp

            // Manually add existingFp to the internal list
            AddFingerprintToDetector(detector, existingFp);

            // To test FindDuplicates correctly without ProcessVideo doing real work and throwing errors for "new_video.mp4":
            // 1. We need ProcessVideo("new_video.mp4", ...) to return newFpTemplate.
            // 2. And newFpTemplate should be added to _processedVideos by ProcessVideo.
            // This is hard without injection or more complex mocking.
            // So, let's assume new_video.mp4 IS "processed" and is newFpTemplate.
            // We add it to the list as if ProcessVideo did its job for new_video.mp4
            AddFingerprintToDetector(detector, newFpTemplate); // Simulate it's been processed and added

            // Now, when FindDuplicates calls ProcessVideo("new_video.mp4", ...), it should find newFpTemplate
            // (because its path matches "new_video.mp4") and return it.
            // Then it iterates through _processedVideos which now contains [existingFp, newFpTemplate].
            // - Compares newFpTemplate with existingFp: similar, "exist.mp4" added.
            // - Compares newFpTemplate with newFpTemplate: skipped (self-comparison).
            List<string> duplicates = detector.FindDuplicates("new_video.mp4", 10, 1, 0.66);
            
            Assert.IsNotNull(duplicates);
            Assert.AreEqual(1, duplicates.Count);
            Assert.AreEqual("exist.mp4", duplicates[0]);
             Assert.Inconclusive("Test relies on ProcessVideo behavior for non-existent files or needs robust mocking of VideoProcessor. Current version uses reflection to test FindDuplicates's core loop logic assuming ProcessVideo works as designed (checks existing and adds).");
        }
        
        [TestMethod]
        public void FindDuplicates_NewVideoAlreadyInLibrary_NotComparedWithItself_AndNoOtherDuplicates()
        {
            var detector = new DuplicateDetector();
            var video1Fp = new VideoFingerprint("video1.mp4") { FrameHashes = { 0x1, 0x2, 0x3 } };

            // Simulate video1.mp4 is already in the library
            AddFingerprintToDetector(detector, video1Fp);

            // When FindDuplicates is called for "video1.mp4", its ProcessVideo call should find video1Fp.
            // The loop will then compare video1Fp (as newVideoFingerprint) against all in _processedVideos.
            // Since _processedVideos only contains video1Fp, it will only self-compare, which should be skipped.
            List<string> duplicates = detector.FindDuplicates("video1.mp4", 10, 0, 1.0);

            Assert.IsNotNull(duplicates);
            Assert.AreEqual(0, duplicates.Count, "Should not find itself as a duplicate.");
            Assert.Inconclusive("Test relies on ProcessVideo behavior for non-existent files or needs robust mocking of VideoProcessor. Current version uses reflection to test FindDuplicates's core loop logic assuming ProcessVideo works as designed (checks existing and adds).");
        }
        
        [TestMethod]
        public void AddVideoToLibrary_AddsFingerprintIfNotExists()
        {
            // This test is difficult without mocking ProcessVideo or having a valid sample video.
            // If we call AddVideoToLibrary("dummy.mp4", 10), it will call ProcessVideo.
            // ProcessVideo will try to extract frames, possibly failing.
            // Assuming ProcessVideo could run (e.g. with a mock that returns a dummy fingerprint):
            // var detector = new DuplicateDetector();
            // detector.AddVideoToLibrary("dummy.mp4", 10);
            // Assert.AreEqual(1, detector.GetProcessedVideoFingerprints().Count);
            // detector.AddVideoToLibrary("dummy.mp4", 10); // Call again
            // Assert.AreEqual(1, detector.GetProcessedVideoFingerprints().Count, "Should not add if path already exists.");
            Assert.Inconclusive("Test requires mocking VideoProcessor or a valid sample video environment.");
        }
    }
}
