namespace LamFiler.Models
{
    public class ScriptEntry
    {
        public string Label   { get; set; } = "";
        public string Command { get; set; } = "";
        public string Args    { get; set; } = "";
        // {path} в Args заменяется на полный путь выбранного файла/папки
    }
}
