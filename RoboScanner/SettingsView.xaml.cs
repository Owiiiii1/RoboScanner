using System.Windows;
using System.Windows.Controls;
using RoboScanner.Localization;
using AppSettings = RoboScanner.Properties.Settings;

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
    }
}
