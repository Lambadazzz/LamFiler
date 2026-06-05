using LamFiler.Services;
using System.IO;
using System.Windows;

namespace LamFiler
{
    public partial class UploadWindow : Window
    {
        private readonly List<string> _paths;
        private CancellationTokenSource _cts = new();

        public UploadWindow(List<string> paths)
        {
            InitializeComponent();
            _paths = paths;
            Loaded += async (_, _) => await StartUploadAsync();
        }

        private async Task StartUploadAsync()
        {
            BtnCancel.IsEnabled = true;

            try
            {
                for (int i = 0; i < _paths.Count; i++)
                {
                    var path   = _paths[i];
                    var name   = System.IO.Path.GetFileName(path);
                    var isDir  = Directory.Exists(path);
                    var prefix = _paths.Count > 1 ? $"[{i + 1}/{_paths.Count}] " : "";

                    TitleText.Text = $"{prefix}☁ {name}";

                    if (isDir)
                    {
                        var prog = new Progress<(int done, int total)>(p =>
                        {
                            ProgressBar.Value = (double)p.done / p.total * 100;
                            StatusText.Text   = $"{p.done} / {p.total} файлов";
                        });
                        await GoogleDriveService.UploadFolderAsync(path, prog, _cts.Token);
                    }
                    else
                    {
                        var prog = new Progress<double>(p =>
                        {
                            ProgressBar.Value = p;
                            StatusText.Text   = $"{p:F0}%";
                        });
                        await GoogleDriveService.UploadFileAsync(path, prog, _cts.Token);
                    }

                    // Удаляем временный zip если он был создан архиватором
                    if (path.StartsWith(System.IO.Path.GetTempPath()) &&
                        path.EndsWith(".zip") && File.Exists(path))
                        File.Delete(path);
                }

                TitleText.Text    = "✅ Загрузка завершена!";
                StatusText.Text   = $"Загружено файлов: {_paths.Count}";
                ProgressBar.Value = 100;
                BtnCancel.Content = "Закрыть";
            }
            catch (OperationCanceledException)
            {
                TitleText.Text    = "Загрузка отменена.";
                StatusText.Text   = "";
                BtnCancel.Content = "Закрыть";
            }
            catch (Exception ex)
            {
                TitleText.Text    = "❌ Ошибка загрузки";
                StatusText.Text   = ex.Message;
                BtnCancel.Content = "Закрыть";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (BtnCancel.Content as string == "Закрыть")
                Close();
            else
                _cts.Cancel();
        }
    }
}
