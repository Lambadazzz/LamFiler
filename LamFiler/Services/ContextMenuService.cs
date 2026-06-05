using LamFiler.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LamFiler.Services
{
    public static class FileContextMenuService
    {
        private static readonly string ScriptsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts.json");

        // ── Скрипты из JSON ─────────────────────────────────────────────────
        public static List<ScriptEntry> LoadScripts()
        {
            if (!File.Exists(ScriptsPath)) return new();
            try
            {
                var json = File.ReadAllText(ScriptsPath);
                return JsonSerializer.Deserialize<List<ScriptEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new();
            }
            catch { return new(); }
        }

        public static void RunScript(ScriptEntry script, string targetPath)
        {
            var args = script.Args.Replace("{path}", $"\"{targetPath}\"");
            var ext  = Path.GetExtension(script.Command).ToLower();

            var psi = ext switch
            {
                ".ps1" => new ProcessStartInfo("powershell.exe",
                              $"-NoProfile -ExecutionPolicy Bypass -File \"{script.Command}\" {args}")
                          { UseShellExecute = false },
                ".bat" => new ProcessStartInfo("cmd.exe",
                              $"/c \"{script.Command}\" {args}")
                          { UseShellExecute = false },
                _      => new ProcessStartInfo(script.Command, args)
                          { UseShellExecute = true }
            };

            psi.CreateNoWindow = false;
            try { Process.Start(psi); }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка запуска скрипта:\n{ex.Message}",
                    "Lambada", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        // ── «Открыть с помощью» из реестра Windows ─────────────────────────
        public class OpenWithEntry
        {
            public string DisplayName { get; set; } = "";
            public string ExePath     { get; set; } = "";
        }

        public static List<OpenWithEntry> GetOpenWithApps(string filePath)
        {
            var result = new List<OpenWithEntry>();
            var ext    = Path.GetExtension(filePath).ToLower();
            if (string.IsNullOrEmpty(ext)) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.<ext>\OpenWithList
            TryReadOpenWithList(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\OpenWithList",
                Registry.CurrentUser, result, seen);

            // 2. HKCR\.<ext>\OpenWithProgids
            using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
            if (extKey != null)
            {
                using var progIdsKey = extKey.OpenSubKey("OpenWithProgids");
                if (progIdsKey != null)
                {
                    foreach (var progId in progIdsKey.GetValueNames())
                        TryResolveProgId(progId, result, seen);
                }
            }

            // 3. HKCR\Applications — приложения зарегистрированные явно
            TryReadOpenWithList(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\OpenWithProgids",
                Registry.CurrentUser, result, seen);

            return result.Take(8).ToList(); // не более 8 пунктов
        }

        private static void TryReadOpenWithList(string keyPath, RegistryKey root,
            List<OpenWithEntry> result, HashSet<string> seen)
        {
            try
            {
                using var key = root.OpenSubKey(keyPath);
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    var exe = key.GetValue(name) as string ?? name;
                    if (exe == "MRUList" || string.IsNullOrWhiteSpace(exe)) continue;
                    AddExeEntry(exe, result, seen);
                }
            }
            catch { }
        }

        private static void TryResolveProgId(string progId, List<OpenWithEntry> result,
            HashSet<string> seen)
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
                if (key == null) return;
                var cmd = key.GetValue(null) as string ?? "";
                var exe = ExtractExePath(cmd);
                if (!string.IsNullOrEmpty(exe)) AddExeEntry(exe, result, seen);
            }
            catch { }
        }

        private static void AddExeEntry(string exeOrName, List<OpenWithEntry> result,
            HashSet<string> seen)
        {
            // exeOrName может быть просто "notepad.exe" или полным путём
            var exePath = exeOrName;
            if (!Path.IsPathRooted(exePath))
            {
                // ищем в PATH
                exePath = FindInPath(exeOrName) ?? exeOrName;
            }

            var display = Path.GetFileNameWithoutExtension(exePath);
            if (!seen.Add(display.ToLower())) return;

            result.Add(new OpenWithEntry { DisplayName = display, ExePath = exePath });
        }

        private static string ExtractExePath(string cmd)
        {
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                var end = cmd.IndexOf('"', 1);
                return end > 0 ? cmd[1..end] : cmd.Trim('"');
            }
            var space = cmd.IndexOf(' ');
            return space > 0 ? cmd[..space] : cmd;
        }

        private static string? FindInPath(string exe)
        {
            var dirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
            foreach (var dir in dirs)
            {
                var full = Path.Combine(dir.Trim(), exe);
                if (File.Exists(full)) return full;
            }
            return null;
        }

        public static void OpenWith(string exePath, string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exePath, $"\"{filePath}\"")
                    { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось открыть:\n{ex.Message}",
                    "Lambada", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        // ── Открыть файл программой по умолчанию ───────────────────────────
        public static void OpenDefault(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { }
        }

        // ── Открыть в Проводнике (выделить файл) ───────────────────────────
        public static void RevealInExplorer(string path)
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }

        // ── Копировать путь в буфер ─────────────────────────────────────────
        public static void CopyPath(string path)
        {
            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    System.Windows.Clipboard.SetDataObject(path, true));
            }
            catch { }
        }
    }
}
