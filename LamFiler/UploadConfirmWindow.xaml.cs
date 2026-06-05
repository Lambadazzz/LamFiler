using System.IO;
using System.IO.Compression;
using System.Windows;

namespace LamFiler
{
    public partial class UploadConfirmWindow : Window
    {
        private readonly string _folderPath;
        public bool   Confirmed  { get; private set; }
        public string UploadPath { get; private set; } = "";

        public UploadConfirmWindow(string folderPath)
        {
            InitializeComponent();
            _folderPath = folderPath;

            var name  = System.IO.Path.GetFileName(folderPath);
            var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
            var size  = files.Sum(f => new FileInfo(f).Length);

            FolderNameText.Text = $"📁  {name}";
            FileCountText.Text  = files.Length.ToString();
            TotalSizeText.Text  = FormatSize(size);
        }

        private void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            if (ChkArchive.IsChecked == true)
            {
                // Создаём zip во Temp
                var zipPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    System.IO.Path.GetFileName(_folderPath) + ".zip");

                // Удаляем старый если есть
                if (File.Exists(zipPath)) File.Delete(zipPath);

                BtnUpload.IsEnabled  = false;
                BtnCancel.IsEnabled  = false;
                BtnUpload.Content    = "Упаковка...";

                try
                {
                    ZipFile.CreateFromDirectory(_folderPath, zipPath,
                        CompressionLevel.Optimal, includeBaseDirectory: true);
                    UploadPath = zipPath;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка архивации:\n{ex.Message}", "Lambada",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    BtnUpload.IsEnabled = true;
                    BtnCancel.IsEnabled = true;
                    BtnUpload.Content   = "☁  Загрузить";
                    return;
                }
            }
            else
            {
                UploadPath = _folderPath;
            }

            Confirmed = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)                return $"{bytes} B";
            if (bytes < 1024 * 1024)         return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
