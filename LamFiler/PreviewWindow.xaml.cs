using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LamFiler
{
    public partial class PreviewWindow : Window
    {
        private double           _imgScale     = 1.0;
        private bool             _seekDragging = false;
        private DispatcherTimer? _videoTimer;
        private bool             _isPlaying    = false;
        private string           _currentPath  = "";

        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tiff", ".ico" };

        private static readonly HashSet<string> TextExts = new(StringComparer.OrdinalIgnoreCase)
            { ".txt", ".md", ".log", ".csv", ".json", ".xml", ".html", ".htm",
              ".cs", ".py", ".js", ".ts", ".css", ".scss", ".java", ".cpp", ".c",
              ".h", ".sh", ".bat", ".ps1", ".yaml", ".yml", ".toml", ".ini",
              ".config", ".csproj", ".sln", ".gitignore", ".env", ".sql" };

        private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };

        private static readonly HashSet<string> PdfExts = new(StringComparer.OrdinalIgnoreCase)
            { ".pdf" };

        public PreviewWindow(string filePath)
        {
            InitializeComponent();
            NavigateTo(filePath);
        }

        // ════════════════════════════════════════════════════════════════════
        //  ПУБЛИЧНЫЙ МЕТОД — обновить предпросмотр без переоткрытия окна
        // ════════════════════════════════════════════════════════════════════

        public void NavigateTo(string path)
        {
            if (_currentPath == path) return;
            _currentPath = path;
            ResetPanels();
            TitleText.Text = Path.GetFileName(path);
            var ext = Path.GetExtension(path);

            if (ImageExts.Contains(ext))      ShowImage(path);
            else if (TextExts.Contains(ext))  ShowText(path);
            else if (PdfExts.Contains(ext))   ShowPdf(path);
            else if (VideoExts.Contains(ext)) ShowVideo(path);
            else                              ShowUnsupported(ext);
        }

        private void ResetPanels()
        {
            _videoTimer?.Stop();
            _videoTimer = null;
            MediaEl.Stop();
            MediaEl.Source = null;
            _isPlaying = false;
            BtnPlay.Content = "▶";
            SeekBar.Value = 0;
            TimeLabel.Text = "";

            PreviewImg.Source = null;
            _imgScale = 1.0;
            UpdateImageScale();

            PreviewText.Document = new FlowDocument();

            ImageScroll.Visibility      = Visibility.Collapsed;
            TextGrid.Visibility         = Visibility.Collapsed;
            WebView.Visibility          = Visibility.Collapsed;
            VideoGrid.Visibility        = Visibility.Collapsed;
            UnsupportedPanel.Visibility = Visibility.Collapsed;
        }

        // ════════════════════════════════════════════════════════════════════
        //  ИЗОБРАЖЕНИЕ
        // ════════════════════════════════════════════════════════════════════

        private void ShowImage(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource   = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                PreviewImg.Source = bmp;
                UpdateImageScale();
                ImageScroll.Visibility = Visibility.Visible;
            }
            catch { ShowUnsupported(Path.GetExtension(path)); }
        }

        private void UpdateImageScale()
            => PreviewImg.LayoutTransform = new ScaleTransform(_imgScale, _imgScale);

        private void PreviewImg_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _imgScale = e.Delta > 0
                ? Math.Min(_imgScale * 1.15, 10.0)
                : Math.Max(_imgScale / 1.15, 0.05);
            UpdateImageScale();
            e.Handled = true;
        }

        // ════════════════════════════════════════════════════════════════════
        //  ТЕКСТ / КОД
        // ════════════════════════════════════════════════════════════════════

        private void ShowText(string path)
        {
            try
            {
                const int maxBytes = 512 * 1024;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bytes    = new byte[Math.Min(fs.Length, maxBytes)];
                var read     = fs.Read(bytes, 0, bytes.Length);
                var content  = System.Text.Encoding.UTF8.GetString(bytes, 0, read);
                if (fs.Length > maxBytes) content += "\n\n... (файл обрезан для предпросмотра)";

                var doc  = new FlowDocument();
                var para = new Paragraph(new Run(content))
                {
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize   = 13,
                    LineHeight = 20,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    Margin     = new Thickness(0),
                    Padding    = new Thickness(0)
                };
                doc.Blocks.Add(para);
                doc.PageWidth        = 4000;
                PreviewText.Document = doc;
                TextGrid.Visibility  = Visibility.Visible;
            }
            catch { ShowUnsupported(Path.GetExtension(path)); }
        }

        // ════════════════════════════════════════════════════════════════════
        //  PDF
        // ════════════════════════════════════════════════════════════════════

        private async void ShowPdf(string path)
        {
            try
            {
                await WebView.EnsureCoreWebView2Async();
                WebView.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
                WebView.Visibility = Visibility.Visible;
            }
            catch
            {
                ShowUnsupported(".pdf");
                UnsupportedText.Text = "PDF предпросмотр недоступен.\nОткройте двойным кликом.";
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  ВИДЕО
        // ════════════════════════════════════════════════════════════════════

        private void ShowVideo(string path)
        {
            MediaEl.Source  = new Uri(path);
            MediaEl.Play();
            _isPlaying      = true;
            BtnPlay.Content = "⏸";
            VideoGrid.Visibility = Visibility.Visible;

            _videoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _videoTimer.Tick += VideoTimer_Tick;
            _videoTimer.Start();
        }

        private void MediaEl_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (MediaEl.NaturalDuration.HasTimeSpan)
                SeekBar.Maximum = MediaEl.NaturalDuration.TimeSpan.TotalSeconds;
        }

        private void MediaEl_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            VideoGrid.Visibility = Visibility.Collapsed;
            ShowUnsupported(Path.GetExtension(MediaEl.Source?.LocalPath ?? ""));
        }

        private void VideoTimer_Tick(object? sender, EventArgs e)
        {
            if (_seekDragging || !_isPlaying) return;
            if (MediaEl.NaturalDuration.HasTimeSpan)
            {
                SeekBar.Value  = MediaEl.Position.TotalSeconds;
                var total      = MediaEl.NaturalDuration.TimeSpan;
                TimeLabel.Text = $"{FormatTime(MediaEl.Position)} / {FormatTime(total)}";
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying) { MediaEl.Pause(); _isPlaying = false; BtnPlay.Content = "▶"; }
            else            { MediaEl.Play();  _isPlaying = true;  BtnPlay.Content = "⏸"; }
        }

        private void SeekBar_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _seekDragging = true;
        private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _seekDragging = false;
            MediaEl.Position = TimeSpan.FromSeconds(SeekBar.Value);
        }

        private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_seekDragging) MediaEl.Position = TimeSpan.FromSeconds(SeekBar.Value);
        }

        private static string FormatTime(TimeSpan t)
            => t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes}:{t.Seconds:D2}";

        // ════════════════════════════════════════════════════════════════════
        //  UNSUPPORTED
        // ════════════════════════════════════════════════════════════════════

        private void ShowUnsupported(string ext)
        {
            UnsupportedIcon.Text = GetIconForExt(ext);
            UnsupportedText.Text = $"Предпросмотр для {ext} недоступен";
            UnsupportedPanel.Visibility = Visibility.Visible;
        }

        private static string GetIconForExt(string ext) => ext.ToLower() switch
        {
            ".exe" or ".msi"                              => "⚙",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦",
            ".doc" or ".docx"                             => "📝",
            ".xls" or ".xlsx"                             => "📊",
            ".ppt" or ".pptx"                             => "📋",
            ".mp3" or ".wav" or ".flac" or ".ogg"        => "🎵",
            _                                             => "📄"
        };

        // ════════════════════════════════════════════════════════════════════
        //  ПОЗИЦИОНИРОВАНИЕ — прилипает справа к Owner
        // ════════════════════════════════════════════════════════════════════

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        public void SnapToOwner()
        {
            if (Owner == null) return;
            Left   = Owner.Left + Owner.ActualWidth;
            Top    = Owner.Top;
            Height = Owner.ActualHeight;
        }

        // ════════════════════════════════════════════════════════════════════
        //  КЛАВИШИ / ЗАКРЫТИЕ
        // ════════════════════════════════════════════════════════════════════

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _videoTimer?.Stop();
            MediaEl.Stop();
            MediaEl.Source = null;
        }
    }
}
