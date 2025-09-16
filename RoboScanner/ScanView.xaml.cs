using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RoboScanner.Views
{
    public partial class ScanView : UserControl
    {
        private bool _running = false;
        private string S(string key) => (string)FindResource(key);

        public ScanView()
        {
            InitializeComponent();
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            _running = true;
            TxtProgramState.Text = S("Scan.Program.Running");   // Запущена / Running / In esecuzione
            TxtRunState.Text = S("Scan.State.Wait");        // Ожидание / Waiting / In attesa
            ChipProgram.Background = new SolidColorBrush(Color.FromRgb(209, 250, 229)); // зелёный оттенок
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _running = false;
            TxtProgramState.Text = S("Scan.Program.Stopped");   // Остановлена / Stopped / Arrestato
            TxtRunState.Text = S("Scan.State.Wait");
            ChipProgram.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255)); // обратно к синему оттенку
        }

        private void BtnScanOnce_Click(object sender, RoutedEventArgs e)
        {
            if (!_running)
            {
                TxtRunState.Text = S("Scan.State.Wait");
                return;
            }

            TxtRunState.Text = S("Scan.State.Scanning");

            // Здесь позже будет реальный вызов: сделать кадры → измерить → определить группу.
            // Сейчас поставим заглушку:
            var rnd = new Random();
            int group = rnd.Next(1, 16);
            double x = Math.Round(rnd.NextDouble() * 100, 2);
            double y = Math.Round(rnd.NextDouble() * 100, 2);
            double z = Math.Round(rnd.NextDouble() * 100, 2);

            TxtLastGroup.Text = $"Группа {group}";
            TxtLastTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            TxtX.Text = x.ToString("F2");
            TxtY.Text = y.ToString("F2");
            TxtZ.Text = z.ToString("F2");

            TxtRunState.Text = S("Scan.State.Done");
        }
    }
}

