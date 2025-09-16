using System.Windows;
using System.Windows.Controls;
using RoboScanner.Services;

namespace RoboScanner.Views
{
    public partial class LogView : UserControl
    {
        private readonly LogService _log = LogService.Instance;

        public LogView()
        {
            InitializeComponent();
            DataContext = _log;
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e) => _log.OpenLogFile();
        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e) => _log.OpenLogFolder();

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _log.Entries.Clear(); // очищаем только список на экране (файл остаётся)
        }

        private void BtnOpenViewer_Click(object sender, RoutedEventArgs e)
        {
            var w = new LogViewerWindow(LogService.Instance.LogPath)
            {
                Owner = Window.GetWindow(this)
            };
            w.Show();
        }

    }
}
