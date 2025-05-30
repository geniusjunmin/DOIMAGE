namespace DOIMAGE
{
    public class AppSettings
    {
        public string? LastDirectoryPath { get; set; }
        public int Quality { get; set; } = 75; // Default quality
        public string Language { get; set; } = "zh-CN"; // Default language (e.g., Chinese simplified)
        public bool WasSuccessfullyLoaded { get; set; } = false; // Indicates if settings were loaded from file vs. defaults
    }
} 