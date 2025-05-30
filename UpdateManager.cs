using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms; // For MessageBox and Application.StartupPath (though StartupPath will be passed in)

namespace DOIMAGE
{
    public class UpdateManager
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string RemoteUrl = "http://rd.junhoo.net:55667/DOIMAGE.txt";
        private const string LocalConfigFilePath = "updcfg";
        private const string UpdateExeName = "updateapp.exe";

        private readonly string _startupPath;
        private readonly Action<string> _logErrorAction;

        public UpdateManager(string startupPath, Action<string> logErrorAction)
        {
            _startupPath = startupPath ?? throw new ArgumentNullException(nameof(startupPath));
            _logErrorAction = logErrorAction ?? throw new ArgumentNullException(nameof(logErrorAction));
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                string remoteContent = await httpClient.GetStringAsync(RemoteUrl);
                string localContent = File.Exists(LocalConfigFilePath) ? await File.ReadAllTextAsync(LocalConfigFilePath) : string.Empty;

                if (remoteContent != localContent)
                {
                    await File.WriteAllTextAsync(LocalConfigFilePath, remoteContent);
                    ExtractAndRunUpdater();
                }
            }
            catch (HttpRequestException httpEx)
            {
                _logErrorAction?.Invoke($"更新检查失败 (HTTP): {httpEx.Message}");
            }
            catch (IOException ioEx)
            {
                _logErrorAction?.Invoke($"更新检查失败 (IO): {ioEx.Message}");
            }
            catch (Exception ex)
            {
                _logErrorAction?.Invoke($"更新检查失败 (未知): {ex.Message}");
            }
        }

        private void ExtractAndRunUpdater()
        {
            string updateExePath = Path.Combine(_startupPath, UpdateExeName);
            if (!File.Exists(updateExePath))
            {
                try
                {
                    string zipResourceName = "DOIMAGE.updateapp.zip"; // Ensure this matches embedded resource name
                    string zipPath = Path.Combine(_startupPath, "updateapp.zip");

                    using (Stream? resourceStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(zipResourceName))
                    {
                        if (resourceStream == null)
                        {
                            MessageBox.Show($"无法找到嵌入的更新资源: {zipResourceName}", "更新错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            _logErrorAction?.Invoke($"致命错误: 嵌入的更新资源 {zipResourceName} 未找到。");
                            return;
                        }
                        using (FileStream fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                        {
                            resourceStream.CopyTo(fileStream);
                        }
                    }

                    ZipFile.ExtractToDirectory(zipPath, _startupPath, true); // Overwrite files if they exist
                    File.Delete(zipPath);
                    _logErrorAction?.Invoke("更新程序已成功解压。");
                }
                catch (Exception ex)
                {
                    _logErrorAction?.Invoke($"解压更新程序失败: {ex.Message}");
                    MessageBox.Show($"解压更新程序失败: {ex.Message}", "更新错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return; // Do not proceed to run updater if extraction failed
                }
            }

            try
            {
                System.Diagnostics.Process.Start(updateExePath);
            }
            catch (Exception ex)
            {
                 _logErrorAction?.Invoke($"启动更新程序失败: {ex.Message}");
                 MessageBox.Show($"启动更新程序失败: {ex.Message}. 请在程序目录下手动运行 {UpdateExeName}。", "更新错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
} 