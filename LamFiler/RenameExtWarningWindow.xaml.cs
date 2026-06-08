using System.Windows;
using System.Windows.Input;

namespace LamFiler
{
    public partial class RenameExtWarningWindow : Window
    {
        /// <summary>true — пользователь подтвердил переименование</summary>
        public bool Confirmed { get; private set; }

        /// <summary>true — пользователь поставил галочку "Больше не спрашивать"</summary>
        public bool DontAskAgain => ChkDontAsk.IsChecked == true;

        public RenameExtWarningWindow(string oldName, string newName)
        {
            InitializeComponent();
            var oldExt = System.IO.Path.GetExtension(oldName).ToLowerInvariant();
            var newExt = System.IO.Path.GetExtension(newName).ToLowerInvariant();
            MessageText.Text =
                $"Вы меняете расширение файла с \"{oldExt}\" на \"{newExt}\".\n" +
                $"Файл может перестать открываться в привязанной программе.";
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)  { Confirmed = true; Close(); }
                if (e.Key == Key.Escape) { Close(); }
            };
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
