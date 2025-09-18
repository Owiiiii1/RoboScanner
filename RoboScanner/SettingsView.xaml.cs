using System.Windows;
using System.Windows.Controls;
using RoboScanner.Localization;
using AppSettings = RoboScanner.Properties.Settings;
using System.Linq;
using RoboScanner.Models;

namespace RoboScanner.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            // выставить текущий язык
            switch (Loc.CurrentLanguage)
            {
                case "ru": RbRU.IsChecked = true; break;
                case "it": RbIT.IsChecked = true; break;
                default: RbEN.IsChecked = true; break;
            }

            // Заполнить выпадающий список групп 1..15
            if (CbRobotGroup != null)
            {
                var items = RobotGroups.All.Values.OrderBy(g => g.Index).ToList();
                CbRobotGroup.ItemsSource = items;
                CbRobotGroup.SelectedValue = RobotGroups.SelectedIndex;
                CbRobotGroup.SelectionChanged += CbRobotGroup_SelectionChanged;
            }
        }

        private void CbRobotGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbRobotGroup?.SelectedValue is int idx)
                RobotGroups.SetSelected(idx); // централизованно запоминаем выбор
        }

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string lang)
            {
                Loc.SetLanguage(lang);
                AppSettings.Default.UILang = lang;
                AppSettings.Default.Save();   // ← без доп. кнопки, сохраняем сразу
            }
        }

        private void OpenGroupSettings_Click(object sender, RoutedEventArgs e)
        {
            // SelectedValuePath = Index, так что SelectedValue — int
            if (CbRobotGroup?.SelectedValue is int idx)
            {
                var dlg = new GroupSettingsWindow(idx)
                {
                    Owner = Window.GetWindow(this)
                };
                dlg.ShowDialog();
                // тут ничего больше не нужно — модель уже обновлена внутри диалога
            }
            else
            {
                MessageBox.Show(Loc.Get("Msg.SelectGroup"), Loc.Get("Group.Window.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
