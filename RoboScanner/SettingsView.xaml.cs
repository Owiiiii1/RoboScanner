using System.Windows;
using System.Windows.Controls;
using RoboScanner.Localization;

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
                case "en": RbEN.IsChecked = true; break;
                case "it": RbIT.IsChecked = true; break;
                default: RbRU.IsChecked = true; break;
            }
        }

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string lang)
            {
                Loc.SetLanguage(lang);
                // опционально: сохранить выбор в файл/настройки, чтобы восстановить при старте
                // Properties.Settings.Default.UILang = lang; Properties.Settings.Default.Save();
            }
        }
    }
}
