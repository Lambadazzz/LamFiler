using System.IO;

namespace LamFiler.Services
{
    /// <summary>Тип записанного действия.</summary>
    public enum FileActionType { Rename, Delete, Move, Copy, CreateFolder }

    /// <summary>Одна запись в истории — достаточно данных для отмены.</summary>
    public class FileAction
    {
        public FileActionType Type { get; init; }

        // Rename: OldPath=старый путь, NewPath=новый путь
        // Delete: OldPath=оригинальный путь, NewPath=резервная копия во временной папке
        // Move:   OldPath=откуда, NewPath=куда переместили
        // Copy:   NewPath=созданная копия (просто удалить при отмене)
        // CreateFolder: NewPath=путь созданной папки
        public string OldPath { get; init; } = "";
        public string NewPath { get; init; } = "";

        public override string ToString() => Type switch
        {
            FileActionType.Rename       => $"Rename {Path.GetFileName(OldPath)} → {Path.GetFileName(NewPath)}",
            FileActionType.Delete       => $"Delete {Path.GetFileName(OldPath)}",
            FileActionType.Move         => $"Move {Path.GetFileName(OldPath)}",
            FileActionType.Copy         => $"Copy {Path.GetFileName(NewPath)}",
            FileActionType.CreateFolder => $"New folder {Path.GetFileName(NewPath)}",
            _                           => ""
        };
    }

    public class UndoService
    {
        private const int MaxSteps = 50;

        // Временная папка для "удалённых" файлов — используется вместо корзины
        public static readonly string TrashDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LamFiler", "Trash");

        private readonly Stack<FileAction> _history = new();

        public bool CanUndo => _history.Count > 0;

        public void Push(FileAction action)
        {
            _history.Push(action);
            while (_history.Count > MaxSteps)
            {
                // Удаляем самую старую запись — нужно перебрать стек
                var tmp = new Stack<FileAction>(_history.Reverse().Skip(1));
                _history.Clear();
                foreach (var a in tmp) _history.Push(a);
            }
        }

        public FileAction? Peek() => _history.TryPeek(out var a) ? a : null;

        /// <summary>Выполняет отмену последнего действия. Возвращает null при успехе или сообщение об ошибке.</summary>
        public string? Undo()
        {
            if (!_history.TryPop(out var action)) return null;

            try
            {
                switch (action.Type)
                {
                    case FileActionType.Rename:
                        // Новое имя → старое имя
                        if (File.Exists(action.NewPath))
                            File.Move(action.NewPath, action.OldPath);
                        else if (Directory.Exists(action.NewPath))
                            Directory.Move(action.NewPath, action.OldPath);
                        break;

                    case FileActionType.Delete:
                        // Восстановить из временной папки
                        if (File.Exists(action.NewPath))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(action.OldPath)!);
                            File.Move(action.NewPath, action.OldPath);
                        }
                        else if (Directory.Exists(action.NewPath))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(action.OldPath)!);
                            Directory.Move(action.NewPath, action.OldPath);
                        }
                        break;

                    case FileActionType.Move:
                        // Вернуть обратно
                        if (File.Exists(action.NewPath))
                            File.Move(action.NewPath, action.OldPath);
                        else if (Directory.Exists(action.NewPath))
                            Directory.Move(action.NewPath, action.OldPath);
                        break;

                    case FileActionType.Copy:
                        // Удалить созданную копию
                        if (File.Exists(action.NewPath))
                            File.Delete(action.NewPath);
                        else if (Directory.Exists(action.NewPath))
                            Directory.Delete(action.NewPath, recursive: true);
                        break;

                    case FileActionType.CreateFolder:
                        // Удалить созданную папку (только если пустая)
                        if (Directory.Exists(action.NewPath))
                        {
                            if (!Directory.EnumerateFileSystemEntries(action.NewPath).Any())
                                Directory.Delete(action.NewPath);
                            else
                                return $"Папка «{Path.GetFileName(action.NewPath)}» не пустая — нельзя отменить создание.";
                        }
                        break;
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
