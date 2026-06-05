using System.Windows;
using System.Windows.Input;

namespace LamFiler
{
    public partial class DeleteConfirmWindow : Window
    {
        public bool Confirmed { get; private set; }

        public DeleteConfirmWindow(string message)
        {
            InitializeComponent();
            TitleText.Text = message;
            KeyDown += (_, e) => { if (e.Key == Key.Enter) { Confirmed = true; Close(); } };
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
