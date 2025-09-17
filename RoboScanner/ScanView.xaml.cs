using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RoboScanner.Services;

namespace RoboScanner.Views
{
    public partial class ScanView : UserControl

    {
        private bool _running = false;
        private readonly LogService _log = LogService.Instance;
        private string S(string key) => (string)FindResource(key);

        private readonly GroupsService _groups = GroupsService.Instance;

        private readonly AppStateService _app = AppStateService.Instance;


        public ScanView()
        {
            InitializeComponent();
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            _running = true;
            _app.IsRunning = true;
            TxtProgramState.Text = S("Scan.Program.Running");   // Запущена / Running / In esecuzione
            TxtRunState.Text = S("Scan.State.Wait");        // Ожидание / Waiting / In attesa
            ChipProgram.Background = new SolidColorBrush(Color.FromRgb(209, 250, 229)); // зелёный оттенок
            _log.Info("App", "Scanning process started");
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _running = false;
            _app.IsRunning = false;
            TxtProgramState.Text = S("Scan.Program.Stopped");   // Остановлена / Stopped / Arrestato
            TxtRunState.Text = S("Scan.State.Wait");
            ChipProgram.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255)); // обратно к синему оттенку
            _log.Info("App", "Scannig process aborted");
        }

        private void BtnScanOnce_Click(object sender, RoutedEventArgs e)
        {
            _log.Info("App", "Scanned ones");
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

            TxtLastGroup.Text = $"Group {group}";
            TxtLastTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            TxtX.Text = x.ToString("F2");
            TxtY.Text = y.ToString("F2");
            TxtZ.Text = z.ToString("F2");

            // добавляем в статистику группы и проверяем лимит
            var add = _groups.AddItemToGroup(group, x, y, z, DateTime.Now);
            if (add.justReachedLimit)
            {
                // ставим «паузу»
                _log.Warn("Scan", $"Лимит {add.stat.Limit} достигнут в группе «{add.stat.Name}» (#{add.stat.Index}). Сканирование поставлено на паузу.",
                          new { Group = add.stat.Index, Count = add.stat.Count, Limit = add.stat.Limit });

                _running = false;                         // останавливаем цикл
                _app.IsRunning = false;
                TxtProgramState.Text = S("Scan.Program.Stopped");
                TxtRunState.Text = S("Scan.State.Wait");
                MessageBox.Show($"Группа «{add.stat.Name}» переполнена ({add.stat.Count}). Сканирование поставлено на паузу.",
                                "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            TxtRunState.Text = S("Scan.State.Done");

            _log.Info("ScanView", "DONE",
                new { Group = group, X = x, Y = y, Z = z, At = DateTime.Now.ToString("o") });
        }
    }
}

