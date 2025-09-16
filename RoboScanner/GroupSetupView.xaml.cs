using System.Windows;
using System.Windows.Controls;
using RoboScanner.Services;

namespace RoboScanner.Views
{
    public partial class GroupSetupView : UserControl
    {
        private readonly RulesService _rules = RulesService.Instance;
        private string S(string key) => (string)FindResource(key);

        public GroupSetupView()
        {
            InitializeComponent();
            DataContext = _rules;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // На всякий случай убедимся, что редактирование завершено
            GridRules.CommitEdit(DataGridEditingUnit.Cell, true);
            GridRules.CommitEdit(DataGridEditingUnit.Row, true);

            _rules.Save();
            MessageBox.Show(S("GroupSetup.SaveOk.Body"), S("GroupSetup.SaveOk.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            _rules.Load();
            MessageBox.Show(S("GroupSetup.LoadOk.Body"), S("GroupSetup.LoadOk.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(S("GroupSetup.ResetConfirm.Body"), S("GroupSetup.ResetConfirm.Title"),
        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _rules.ResetDefaults();
            }
        }
    }
}
