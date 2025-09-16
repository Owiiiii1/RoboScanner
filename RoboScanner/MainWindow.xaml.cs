using System.Windows;
using System.Windows.Controls;
using RoboScanner.Views;

namespace RoboScanner
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            NavigateToScan(); // стартуем с раздела "Сканирование"
        }

        private void ClearActive()
        {
            BtnScan.Tag = null;
            BtnGroupSetup.Tag = null;
            BtnGroups.Tag = null;
            BtnSettings.Tag = null;
            BtnLog.Tag = null;
            BtnStats.Tag = null;
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e) => NavigateToScan();
        private void BtnGroupSetup_Click(object sender, RoutedEventArgs e) => Navigate(new GroupSetupView(), BtnGroupSetup);
        private void BtnGroups_Click(object sender, RoutedEventArgs e) => Navigate(new GroupsView(), BtnGroups);
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => Navigate(new SettingsView(), BtnSettings);
        private void BtnLog_Click(object sender, RoutedEventArgs e) => Navigate(new LogView(), BtnLog);
        private void BtnStats_Click(object sender, RoutedEventArgs e) => Navigate(new StatsView(), BtnStats);

        private void NavigateToScan() => Navigate(new ScanView(), BtnScan);

        private void Navigate(UserControl view, Button activeButton)
        {
            ClearActive();
            activeButton.Tag = "Active";
            ContentHost.Content = view;
        }
    }
}
