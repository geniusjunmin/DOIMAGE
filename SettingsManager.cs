using System;
using System.IO;
using System.Text;
using System.Globalization;

namespace DOIMAGE
{
    public class SettingsManager
    {
        private const string SettingsFileName = "settings.ini";
        private const string LastDirectoryFileName = "lastDirectoryPath.txt";
        private readonly Action<string>? _logErrorAction;

        public SettingsManager(Action<string>? logErrorAction = null)
        {
            _logErrorAction = logErrorAction;
        }

        public AppSettings LoadSettings()
        {
            var appSettings = new AppSettings();
            bool settingsFileExists = File.Exists(SettingsFileName);

            // Load last directory path
            if (File.Exists(LastDirectoryFileName))
            {
                try
                {
                    appSettings.LastDirectoryPath = File.ReadAllText(LastDirectoryFileName);
                }
                catch (Exception ex)
                {
                    _logErrorAction?.Invoke($"读取最后目录路径失败: {ex.Message}");
                }
            }

            // Load quality and language from settings.ini
            if (settingsFileExists)
            {
                try
                {
                    var lines = File.ReadAllLines(SettingsFileName);
                    if (lines.Length > 0) appSettings.WasSuccessfullyLoaded = true; // Mark as loaded if file has content

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("Quality=", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(line.Substring("Quality=".Length), out int quality))
                            {
                                appSettings.Quality = quality;
                            }
                        }
                        else if (line.StartsWith("Language=", StringComparison.OrdinalIgnoreCase))
                        {
                            appSettings.Language = line.Substring("Language=".Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logErrorAction?.Invoke($"读取设置 ({SettingsFileName}) 失败: {ex.Message}");
                    appSettings.WasSuccessfullyLoaded = false; // Ensure it's false on error
                }
            }

            // If settings.ini didn't exist or was empty, and no language was set, determine default language
            if (!appSettings.WasSuccessfullyLoaded && string.IsNullOrEmpty(appSettings.Language) || appSettings.Language == new AppSettings().Language /* check if it's still default value from constructor*/ )
            {
                // Set default language based on culture if not loaded from file
                // Only set if it wasn't explicitly loaded.
                string currentCultureName = CultureInfo.CurrentCulture.Name;
                if (currentCultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
                {
                    appSettings.Language = "zh-CN";
                }
                else
                {
                    appSettings.Language = "en-US"; // Default to English for other cultures
                }
            }
            
            return appSettings;
        }

        public void SaveSettings(AppSettings settings)
        {
            // Save last directory path
            if (!string.IsNullOrEmpty(settings.LastDirectoryPath))
            {
                try
                {
                    File.WriteAllText(LastDirectoryFileName, settings.LastDirectoryPath);
                }
                catch (Exception ex)
                {
                    _logErrorAction?.Invoke($"保存最后目录路径失败: {ex.Message}");
                }
            }

            // Save quality and language to settings.ini
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Quality={settings.Quality}");
                sb.AppendLine($"Language={settings.Language}");
                File.WriteAllText(SettingsFileName, sb.ToString());
            }
            catch (Exception ex)
            {
                _logErrorAction?.Invoke($"保存设置 ({SettingsFileName}) 失败: {ex.Message}");
            }
        }
    }
} 