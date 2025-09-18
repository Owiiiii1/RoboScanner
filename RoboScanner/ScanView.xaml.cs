using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoboScanner.Services;

namespace RoboScanner.Views
{
    public partial class ScanView : UserControl
    {
        private readonly AppStateService _app = AppStateService.Instance;
        private readonly GroupsService _groups = GroupsService.Instance;
        private readonly LogService _log = LogService.Instance;

        private string L(string key, string fallback) =>
    (TryFindResource(key) as string) ?? fallback;

        public ScanView()
        {
            InitializeComponent();
            TxtResultLine.Text = "—";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (_app.IsRunning)
            {
                // Start — green; Stop/Scan enabled
                BtnStart.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // #22C55E
                BtnStart.Foreground = Brushes.White;
                BtnStop.IsEnabled = true;
                BtnScan.IsEnabled = true;
            }
            else
            {
                // Reset Start visuals; Stop/Scan disabled
                BtnStart.ClearValue(Button.BackgroundProperty);
                BtnStart.ClearValue(Button.ForegroundProperty);
                BtnStop.IsEnabled = false;
                BtnScan.IsEnabled = false;
            }
        }

        private string Axis(string key, string fallback) =>
            (TryFindResource(key) as string) ?? fallback;

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            _app.IsRunning = true;
            _app.OpState = OperationState.Wait;
            UpdateButtons();
            _log.Info("Application", "Start button clicked");
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _app.IsRunning = false;
            _app.OpState = OperationState.Wait;
            UpdateButtons();
            _log.Info("Application", "Stop button clicked");
        }

        private void BtnScanOnce_Click(object sender, RoutedEventArgs e)
        {
            _log.Info("Scan", "Scan button clicked");

            if (!_app.IsRunning)
            {
                _log.Warn("Scan", "Scan attempted while program stopped");
                return;
            }

            _app.OpState = OperationState.Scanning;

            // ==== Simulated scan based on ACTIVE groups ====
            var sim = SimulateFromActiveGroups();
            if (sim == null)
            {
                MessageBox.Show(
                    L("Scan.Alert.NoActiveGroups.Body",
                      "There are no active groups in the settings. Specify the dimensions for at least one group."),
                    L("Scan.Alert.NoActiveGroups.Title", "Warning"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                _log.Warn("Scan", "No active groups to simulate scan");
                _app.OpState = OperationState.Wait;
                return;
            }


            var (groupIndex, groupName, x, y, z) = sim.Value;
            var now = DateTime.Now;
            

            // Result line: <GroupName> — X..  Y..  Z..
            string xLbl = Axis("Scan.Axis.X", "X: ");
            string yLbl = Axis("Scan.Axis.Y", "Y: ");
            string zLbl = Axis("Scan.Axis.Z", "Z: ");
            TxtResultLine.Text = $"{groupName} — {xLbl}{x:F2}  {yLbl}{y:F2}  {zLbl}{z:F2}";

            // Placeholder images (replace with real frames)
            ShowPlaceholders();

            // Update global state (not UI-bound here, just for history/status)
            _app.SetLastScan(groupIndex, x, y, z, now);
            _app.OpState = OperationState.Done;

            // Log
            _log.Info("Scan", "Scan completed",
                new { Group = groupIndex, GroupName = groupName, X = x, Y = y, Z = z, At = now.ToString("o") });

            // Group accounting + auto-pause on limit
            var add = _groups.AddItemToGroup(groupIndex, x, y, z, now);
            ScanHistoryService.Instance.Add(new ScanRecord(now, groupIndex, groupName, x, y, z));

            if (add.justReachedLimit)
            {
                _log.Warn("Scan",
                    $"Group limit reached; paused. Name={add.stat.Name}, Index={add.stat.Index}, Count={add.stat.Count}, Limit={add.stat.Limit}",
                    new { Group = add.stat.Index, Count = add.stat.Count, Limit = add.stat.Limit });

                _app.IsRunning = false;
                _app.OpState = OperationState.Wait;
                UpdateButtons();

                MessageBox.Show(
                    string.Format(L("Scan.Alert.GroupOverflow.Body",
                                    "The «{0}» group is full ({1}). Scanning has been paused."),
                                  add.stat.Name, add.stat.Count),
                    L("Scan.Alert.GroupOverflow.Title", "Attention"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

        }

        /// <summary>
        /// Picks a random ACTIVE group and generates X/Y/Z:
        /// for each axis: [Max/2 .. Max]; if Max is null → [0 .. 100].
        /// </summary>
        private (int groupIndex, string groupName, double x, double y, double z)? SimulateFromActiveGroups()
        {
            // берём только ВКЛЮЧЁННЫЕ группы
            var rules = RulesService.Instance.Rules
                .Where(r => r.IsActive)                 // ключевое изменение
                .Where(r => r.RobotGroup.HasValue)   // раскомментируй, если нужно требовать привязку к «робо-группе»
                .ToList();

            if (rules.Count == 0) return null;

            var rnd = new Random();
            var r = rules[rnd.Next(rules.Count)];

            double Gen(double? max)
            {
                if (max.HasValue)
                {
                    double lo = max.Value * 0.5;
                    double hi = max.Value;
                    return Math.Round(lo + rnd.NextDouble() * (hi - lo), 2);
                }
                return Math.Round(rnd.NextDouble() * 100.0, 2);
            }

            double x = Gen(r.MaxX);
            double y = Gen(r.MaxY);
            double z = Gen(r.MaxZ);

            string name = string.IsNullOrWhiteSpace(r.Name) ? $"Group {r.Index}" : r.Name;
            return (r.Index, name, x, y, z);
        }


        private void ShowPlaceholders()
        {
            ImgCam1.Source = MakePlaceholder(800, 600, Colors.LightSteelBlue);
            ImgCam2.Source = MakePlaceholder(800, 600, Colors.LightSkyBlue);
            LblNoImg1.Visibility = Visibility.Collapsed;
            LblNoImg2.Visibility = Visibility.Collapsed;
        }

        private BitmapSource MakePlaceholder(int w, int h, Color color)
        {
            var dpi = 96;
            var stride = w * 4;
            var pixels = new byte[h * stride];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    pixels[i + 0] = color.B;
                    pixels[i + 1] = color.G;
                    pixels[i + 2] = color.R;
                    pixels[i + 3] = 255;
                }
            return BitmapSource.Create(w, h, dpi, dpi,
                System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
        }
    }
}
