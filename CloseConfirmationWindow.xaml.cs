using NetShiftST.Core;
using System.Windows;

namespace NetShiftST
{
    public partial class CloseConfirmationWindow : Window
    {
        public CloseAction SelectedAction { get; private set; } = CloseAction.Ask;
        public bool DontAskAgain => DontAskAgainCheckBox.IsChecked == true;

        public CloseConfirmationWindow()
        {
            InitializeComponent();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = CloseAction.MinimizeToTray;
            DialogResult = true;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = CloseAction.Exit;
            DialogResult = true;
        }
    }
}