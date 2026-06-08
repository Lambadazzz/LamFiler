using System.IO;
using System.Text.Json;

namespace LamFiler.Models
{
    public class AppSettings
    {
        // ── Подсказки / предупреждения ─────────────────────────────────────
        /// <summary>Показывать предупреждение при смене расширения файла.</summary>
        public bool WarnOnExtensionChange { get; set; } = true;

        // TODO: [Настройки] добавить страницу Settings с переключателями подсказок:
        //   - WarnOnExtensionChange  "Предупреждать при смене расширения файла"
        //   (при добавлении новых warn-флагов — регистрировать их здесь)

        // ── Персистентность ───────────────────────────────────────────────
        private static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LamFiler", "settings.json");

        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { /* повреждённый файл — используем дефолты */ }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(this, _opts));
            }
            catch { }
        }
    }
}
