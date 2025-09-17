using System.Windows;
using System.Windows.Controls;
using RoboScanner.Views;

namespace RoboScanner
{
    public partial class MainWindow : Window
    {
        // Кэшируем вью, чтобы не терять состояние при переключении
        private ScanView? _scan;
        private GroupSetupView? _groupSetup;
        private GroupsView? _groups;
        private SettingsView? _settings;
        private LogView? _log;
        private StatsView? _stats;

        public MainWindow()
        {
            InitializeComponent();
            ShowView("Scan"); // стартовый экран
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag)
                ShowView(tag);
        }

        private void ShowView(string tag)
        {
            switch (tag)
            {
                case "Scan":
                    MainContent.Content = _scan ??= new ScanView();
                    break;
                case "GroupSetup":
                    MainContent.Content = _groupSetup ??= new GroupSetupView();
                    break;
                case "Groups":
                    MainContent.Content = _groups ??= new GroupsView();
                    break;
                case "Settings":
                    MainContent.Content = _settings ??= new SettingsView();
                    break;
                case "Log":
                    MainContent.Content = _log ??= new LogView();
                    break;
                case "Stats":
                    MainContent.Content = _stats ??= new StatsView();
                    break;
            }
        }
    }
}
