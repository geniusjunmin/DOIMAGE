using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DOIMAGE
{
    public class UIManager
    {
        private RichTextBox _txtLog;
        private ProgressBar _progressBar;
        private Label _lblProgress;
        private PictureBox _pictureBoxPreview;
        private RadioButton _rbLanguageChinese; // To check which language is selected for setnullimage
        private Form _form; // Required for Invoke

        private ColorGenerator _colorGenerator;

        public UIManager(Form form, RichTextBox txtLog, ProgressBar progressBar, Label lblProgress, PictureBox pictureBoxPreview, RadioButton rbLanguageChinese)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _txtLog = txtLog ?? throw new ArgumentNullException(nameof(txtLog));
            _progressBar = progressBar ?? throw new ArgumentNullException(nameof(progressBar));
            _lblProgress = lblProgress ?? throw new ArgumentNullException(nameof(lblProgress));
            _pictureBoxPreview = pictureBoxPreview ?? throw new ArgumentNullException(nameof(pictureBoxPreview));
            _rbLanguageChinese = rbLanguageChinese ?? throw new ArgumentNullException(nameof(rbLanguageChinese));
            _colorGenerator = new ColorGenerator();
        }

        public void LogMessage(string message)
        {
            if (_txtLog.InvokeRequired)
            {
                _txtLog.Invoke((MethodInvoker)delegate
                {
                    AppendLogText(message);
                });
            }
            else
            {
                AppendLogText(message);
            }
        }

        public void LogErrorMessage(string message)
        {
            if (_txtLog.InvokeRequired)
            {
                _txtLog.Invoke((MethodInvoker)delegate
                {
                    AppendLogText(message, true);
                });
            }
            else
            {
                AppendLogText(message, true);
            }
        }

        private void AppendLogText(string message, bool isError = false)
        {
            // Actual appending logic, ensures it runs on UI thread via Invoke pattern above
            // The original LogMessage was commented out, LogerrorMessage was not.
            // Assuming we want consistent logging style.
            _txtLog.AppendText($"{DateTime.Now}: {message}\n");
            _txtLog.ScrollToCaret();
        }


        public Task UpdateThumbnailProgress(int processed, int total)
        {
            if (_form.IsHandleCreated && !_form.IsDisposed)
            {
                try
                {
                    return _form.Invoke((Func<Task>)(async () => 
                    {
                        if (!_progressBar.IsDisposed)
                        {
                            _progressBar.Minimum = 0;
                            _progressBar.Maximum = total;
                            _progressBar.Value = Math.Min(processed, total);
                        }
                        if (!_lblProgress.IsDisposed)
                        {
                            _lblProgress.Text = $"缩略图进度: {processed}/{total}";
                            _lblProgress.Refresh();
                        }
                        await Task.CompletedTask;
                    }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
            return Task.CompletedTask;
        }

        public string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public void SetNullImage()
        {
            try
            {
                Bitmap image = new Bitmap(400, 300);
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.LightGray);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    using (Pen pen = new Pen(Color.DarkGray, 2))
                    {
                        g.DrawRectangle(pen, 10, 10, image.Width - 20, image.Height - 20);
                    }

                    Font font = new Font("微软雅黑", 16, FontStyle.Bold);
                    SolidBrush textBrush = new SolidBrush(Color.DarkSlateGray);
                    string text = _rbLanguageChinese.Checked ? "当前图片不存在" : "Image Not Available";
                    SizeF textSize = g.MeasureString(text, font);
                    PointF location = new PointF((image.Width - textSize.Width) / 2, (image.Height - textSize.Height) / 2);
                    g.DrawString(text, font, textBrush, location);

                    using (Font iconFont = new Font("Segoe UI Emoji", 48))
                    {
                        string icon = "❌";
                        SizeF iconSize = g.MeasureString(icon, iconFont);
                        g.DrawString(icon, iconFont, textBrush, (image.Width - iconSize.Width) / 2, (image.Height - iconSize.Height) / 2 - 60);
                    }
                }
                _pictureBoxPreview.Image = image;
            }
            catch (Exception ex)
            {
                LogErrorMessage($"生成空图片时出错: {ex.Message}");
                try
                {
                     // Fallback error image
                    Bitmap errorBmp = new Bitmap(_pictureBoxPreview.Width > 0 ? _pictureBoxPreview.Width : 100, _pictureBoxPreview.Height > 0 ? _pictureBoxPreview.Height : 100);
                    using (Graphics g = Graphics.FromImage(errorBmp))
                    {
                        g.Clear(Color.Red);
                        g.DrawString("ERROR", new Font("Arial", 12), Brushes.White, 10, 10);
                    }
                    _pictureBoxPreview.Image = errorBmp;
                }
                catch {} // Nested try-catch to prevent crash during error display
            }
        }

        public Color GetNextGroupColor()
        {
            return _colorGenerator.GetNextColor();
        }

        public bool IsDarkColor(Color color)
        {
            double brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000.0;
            return brightness < 128;
        }

        // Nested class for color generation
        private class ColorGenerator
        {
            private readonly List<Color> predefinedColors;
            private int currentIndex = 0;
            private Random random = new Random();

            public ColorGenerator()
            {
                predefinedColors = new List<Color>
                {
                    Color.FromArgb(255, 182, 193), Color.FromArgb(152, 251, 152),
                    Color.FromArgb(135, 206, 250), Color.FromArgb(255, 218, 185),
                    Color.FromArgb(221, 160, 221), Color.FromArgb(255, 255, 176),
                    Color.FromArgb(176, 224, 230), Color.FromArgb(255, 160, 122),
                    Color.FromArgb(216, 191, 216), Color.FromArgb(240, 230, 140),
                    Color.FromArgb(255, 192, 203), Color.FromArgb(173, 216, 230),
                    Color.FromArgb(144, 238, 144), Color.FromArgb(255, 222, 173),
                    Color.FromArgb(250, 250, 210), Color.FromArgb(211, 211, 211),
                    Color.FromArgb(230, 230, 250), Color.FromArgb(255, 228, 225),
                    Color.FromArgb(245, 245, 220), Color.FromArgb(240, 248, 255)
                };
            }

            public Color GetNextColor()
            {
                if (currentIndex < predefinedColors.Count)
                {
                    return predefinedColors[currentIndex++];
                }
                return GeneratePastelColor();
            }

            private Color GeneratePastelColor()
            {
                int red = random.Next(180, 255);
                int green = random.Next(180, 255);
                int blue = random.Next(180, 255);
                return Color.FromArgb(red, green, blue);
            }
        }
    }
} 