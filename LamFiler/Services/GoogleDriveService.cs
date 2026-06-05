using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using System.IO;

namespace LamFiler.Services
{
    public static class GoogleDriveService
    {
        private static readonly string[] Scopes = { DriveService.Scope.DriveFile };
        private static readonly string AppName  = "Lambada";
        private static readonly string SecretPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client_secret.json");
        private static readonly string TokenPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Lambada", "token");

        private static DriveService? _service;

        // ── Авторизация ─────────────────────────────────────────────────────
        public static async Task<DriveService> GetServiceAsync()
        {
            if (_service != null) return _service;

            UserCredential credential;
            await using var stream = new FileStream(SecretPath, FileMode.Open, FileAccess.Read);
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(TokenPath, true));

            _service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName       = AppName
            });
            return _service;
        }

        // ── Загрузка одного файла ───────────────────────────────────────────
        public static async Task<string> UploadFileAsync(
            string localPath,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            var service  = await GetServiceAsync();
            var fileInfo = new FileInfo(localPath);

            var meta = new Google.Apis.Drive.v3.Data.File
            {
                Name    = fileInfo.Name,
                Parents = new[] { "root" }
            };

            var mimeType = GetMimeType(fileInfo.Extension);

            await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read);
            var request = service.Files.Create(meta, stream, mimeType);
            request.Fields = "id, name, webViewLink";

            request.ProgressChanged += p =>
            {
                if (p.Status == UploadStatus.Uploading && fileInfo.Length > 0)
                    progress?.Report((double)p.BytesSent / fileInfo.Length * 100);
            };

            var result = await request.UploadAsync(ct);

            if (result.Status == UploadStatus.Failed)
                throw new Exception($"Ошибка загрузки: {result.Exception?.Message}");

            return request.ResponseBody?.WebViewLink ?? "";
        }

        // ── Загрузка папки рекурсивно ───────────────────────────────────────
        public static async Task UploadFolderAsync(
            string localPath,
            IProgress<(int done, int total)>? progress = null,
            CancellationToken ct = default,
            string? parentId = null)
        {
            var service = await GetServiceAsync();

            // Создаём папку на Drive
            var folderMeta = new Google.Apis.Drive.v3.Data.File
            {
                Name     = System.IO.Path.GetFileName(localPath),
                MimeType = "application/vnd.google-apps.folder",
                Parents  = parentId != null ? new[] { parentId } : new[] { "root" }
            };
            var folderReq  = service.Files.Create(folderMeta);
            folderReq.Fields = "id";
            var folder = await folderReq.ExecuteAsync(ct);

            var files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
            int done  = 0;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                await UploadFileAsync(file, null, ct);
                done++;
                progress?.Report((done, files.Length));
            }
        }

        // ── MIME type по расширению ─────────────────────────────────────────
        private static string GetMimeType(string ext) => ext.ToLower() switch
        {
            ".pdf"  => "application/pdf",
            ".png"  => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"  => "image/gif",
            ".mp4"  => "video/mp4",
            ".mp3"  => "audio/mpeg",
            ".txt"  => "text/plain",
            ".html" => "text/html",
            ".zip"  => "application/zip",
            _       => "application/octet-stream"
        };
    }
}
