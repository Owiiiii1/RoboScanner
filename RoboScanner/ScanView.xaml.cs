using OpenCvSharp;
using OpenCvSharp.WpfExtensions; // для ToWriteableBitmap()
using RoboScanner.Models;
using RoboScanner.Services;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AppSettings = RoboScanner.Properties.Settings;
using CvRect = OpenCvSharp.Rect;


namespace RoboScanner.Views
{
    public partial class ScanView : UserControl
    {
        private readonly AppStateService _app = AppStateService.Instance;
        private readonly GroupsService _groups = GroupsService.Instance;
        private readonly LogService _log = LogService.Instance;
        private CaptureManager Capture => CaptureManager.Instance;


        private bool _isScanInProgress;

        private string L(string key, string fallback) =>
            (TryFindResource(key) as string) ?? fallback;

        public ScanView()
        {
            InitializeComponent();
            TxtResultLine.Text = "—";
            UpdateButtons();

            // Глобальный триггер запуска скана (живёт независимо от экрана)
            StartSignalWatcher.Instance.Triggered += async (_, __) =>
            {
                if (!_app.IsRunning) return;

                if (RelayGate.IsBusy)
                {
                    _log.Warn("Scan", $"Blocked by RelayGate: {RelayGate.Remaining.TotalSeconds:F1}s remaining");
                    return;
                }
                if (_isScanInProgress) return;

                _isScanInProgress = true;
                try
                {
                    // Запускаем на UI-диспетчере и ЖДЁМ реального Task из StartScanAsync
                    var op = Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await StartScanAsync();
                        }
                        catch (Exception ex)
                        {
                            _log.Error("Scan", "Unhandled error in StartScanAsync", ex);
                            throw;
                        }
                        return 0;
                    });
                    await op.Task;   // <-- вот это ключевое: дождались выполнения
                }
                finally
                {
                    _isScanInProgress = false;
                }
            };


        }

        private void UpdateButtons()
        {
            if (_app.IsRunning)
            {
                BtnStart.Content = L("Scan.Btn.Stop", "Stop");  // ← текст
                BtnStart.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                BtnStart.Foreground = Brushes.White;
               
                BtnScan.IsEnabled = true;
            }
            else
            {
                BtnStart.Content = L("Scan.Btn.Start", "Start"); // ← текст
                BtnStart.ClearValue(Button.BackgroundProperty);
                BtnStart.ClearValue(Button.ForegroundProperty);
               
                BtnScan.IsEnabled = false;
            }
        }


        private string Axis(string key, string fallback) =>
            (TryFindResource(key) as string) ?? fallback;

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!_app.IsRunning)
            {
                // START
                _app.IsRunning = true;
                _app.OpState = OperationState.Wait;
                UpdateButtons();

                try { StartSignalWatcher.Instance.Start(); }
                catch (Exception ex) { _log.Error("InputWatcher", "Failed to start", ex); }

                _log.Info("Application", "Start button clicked");
            }
            else
            {
                // STOP
                _app.IsRunning = false;
                _app.OpState = OperationState.Wait;
                UpdateButtons();

                await StartSignalWatcher.Instance.StopAsync();

                _log.Info("Application", "Stop button clicked");
            }
        }

        private async void BtnScanOnce_Click(object sender, RoutedEventArgs e)
        {
            if (RelayGate.IsBusy)
            {
                _log.Warn("Scan", $"Manual scan blocked by RelayGate: {RelayGate.Remaining.TotalSeconds:F1}s remaining");
                MessageBox.Show(
                    $"Scanning unavailable: relay pulse still active {RelayGate.Remaining.TotalSeconds:F0} sec.",
                    "Busy", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_isScanInProgress) return;
            _isScanInProgress = true;
            try { await StartScanAsync(); }
            finally { _isScanInProgress = false; }
        }

        // === Общая логика одноразового скана (кнопка и автозапуск вызывают сюда) ===
        private async Task StartScanAsync()
        {
            _log.Info("Scan", "Scan requested");

            if (!_app.IsRunning)
            {
                _log.Warn("Scan", "Scan attempted while program stopped");
                return;
            }

            // Статус: сканирование
            _app.OpState = OperationState.Scanning;
            UpdateButtons();

            try
            {
                // --- 1) Выбор камер по сохранённым Moniker ---
                var all = CameraDiscoveryService.Instance.ListVideoDevices();
                var saved1 = AppSettings.Default.Camera1Id;
                var saved2 = AppSettings.Default.Camera2Id;

                string? cam1 = CameraDiscoveryService.Instance.FindByMoniker(all, saved1)?.Moniker
                               ?? all.ElementAtOrDefault(0)?.Moniker;
                string? cam2 = CameraDiscoveryService.Instance.FindByMoniker(all, saved2)?.Moniker
                               ?? all.ElementAtOrDefault(1)?.Moniker;

                // Маленький паддинг вокруг найденного ROI, чтобы не резать объект "впритык"
                const int pad = 12;

                // --- 2) Локальная функция обработки одной камеры ---
                async Task ProcessOneAsync(string moniker, System.Windows.Controls.Image imgView, System.Windows.Controls.TextBlock noImgLabel)
                {
                    // 2.1 Захват исходного кадра (BGR)
                    using var frame = await Capture.CaptureOnceAsync(moniker);

                    // 2.2 Бинаризация ТОЛЬКО ДЛЯ ПОИСКА ROI (на экран её НЕ выводим)
                    //     Otsu + CLAHE обычно стабильнее для неоднородного освещения
                    var binOpt = new BinarizationService.Options
                    {
                        UseAdaptive = false,                 // -> Otsu
                        UseClahe = true,                  // локальное выравнивание контраста
                        ClaheClipLimit = 2.0,
                        ClaheTileGrid = new OpenCvSharp.Size(8, 8),
                        BlurKernel = 3,
                        Invert = true,                  // хотим белый объект на бинарке
                        KeepSingleObject = false                  // изображение НЕ меняем
                    };
                    using var bin = BinarizationService.Instance.Binarize(frame, binOpt);

                    // 2.3 Поиск крупнейшего объекта и получение его прямоугольника
                    if (BinarizationService.Instance.TryGetLargestObjectRoi(bin, out CvRect roi, minAreaPx: 0))
                    {
                        // Безопасный паддинг и ограничение границами
                        roi.X = Math.Max(0, roi.X - pad);
                        roi.Y = Math.Max(0, roi.Y - pad);
                        roi.Width = Math.Min(roi.Width + 2 * pad, frame.Cols - roi.X);
                        roi.Height = Math.Min(roi.Height + 2 * pad, frame.Rows - roi.Y);

                        _log.Info("ROI", $"Crop {roi.Width}x{roi.Height} at ({roi.X},{roi.Y})");

                        // 2.4 КРОП исходного кадра и показ (WPF любит BGRA)
                        using var crop = new Mat(frame, roi);
                        using var cropBgra = new Mat();
                        Cv2.CvtColor(crop, cropBgra, ColorConversionCodes.BGR2BGRA);
                        imgView.Source = cropBgra.ToWriteableBitmap();
                        noImgLabel.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        // 2.5 Если ROI не найден — покажем диагностическую бинарку (сразу видно, что не так)
                        using var dbg = new Mat();
                        Cv2.CvtColor(bin, dbg, ColorConversionCodes.GRAY2BGRA);
                        imgView.Source = dbg.ToWriteableBitmap();
                        noImgLabel.Visibility = Visibility.Collapsed;
                        _log.Warn("ROI", "ROI not found; showing diagnostic binary frame");
                    }
                }

                // --- 3) Обработка обеих камер ---
                if (cam1 != null) await ProcessOneAsync(cam1, ImgCam1, LblNoImg1);
                else
                {
                    ImgCam1.Source = null; LblNoImg1.Visibility = Visibility.Visible;
                    _log.Warn("Camera", "Camera1 is not selected or not found");
                }

                if (cam2 != null) await ProcessOneAsync(cam2, ImgCam2, LblNoImg2);
                else
                {
                    ImgCam2.Source = null; LblNoImg2.Visibility = Visibility.Visible;
                    _log.Warn("Camera", "Camera2 is not selected or not found");
                }
            }
            catch (Exception ex)
            {
                // Любая ошибка на этапе камер/ROI
                _log.Error("Camera", "Capture/ROI failed", ex);
                ImgCam1.Source = null; LblNoImg1.Visibility = Visibility.Visible;
                ImgCam2.Source = null; LblNoImg2.Visibility = Visibility.Visible;
            }

            // --- 4) Дальнейшая логика сканирования — БЕЗ ИЗМЕНЕНИЙ ---
            try
            {
                var sim = SimulateFromActiveGroups();
                if (sim == null)
                {
                    _log.Warn("Scan", "No active groups to simulate scan");
                    MessageBox.Show(
                        L("Scan.Alert.NoActiveGroups.Body",
                          "There are no active groups in the settings. Specify the dimensions for at least one group."),
                        L("Scan.Alert.NoActiveGroups.Title", "Warning"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);

                    _app.OpState = OperationState.Wait;
                    return;
                }

                var (groupIndex, groupName, x, y, z) = sim.Value;

                var rule = RulesService.Instance.Rules.FirstOrDefault(r => r.Index == groupIndex);
                var robotIdx = rule?.RobotGroup;

                int mappedPulseSec = 0;
                int startPulseSec = 0;

                if (robotIdx.HasValue)
                {
                    var rg = RoboScanner.Models.RobotGroups.Get(robotIdx.Value);
                    mappedPulseSec = (rg.PulseSeconds.HasValue && rg.PulseSeconds.Value > 0) ? rg.PulseSeconds.Value : 0;

                    // 1) Жмём привязанную группу
                    if (!string.IsNullOrWhiteSpace(rg.Host) && rg.PrimaryCoilAddress.HasValue)
                    {
                        try
                        {
                            await ModbusOutputService.PulseAsync(
                                host: rg.Host, port: rg.Port, unitId: rg.UnitId,
                                coilAddressOneBased: rg.PrimaryCoilAddress.Value,
                                pulseSeconds: mappedPulseSec > 0 ? mappedPulseSec : (int?)null
                            );
                            _log.Info("Relay", "Triggered mapped robot group",
                                new { RobotGroupIndex = robotIdx.Value, rg.Host, rg.UnitId, rg.PrimaryCoilAddress, PulseSeconds = mappedPulseSec });
                        }
                        catch (Exception ex)
                        {
                            _log.Error("Relay", "Failed to trigger mapped robot group", ex);
                        }
                    }
                    else
                    {
                        _log.Warn("Relay", $"Mapped robot group {robotIdx.Value} not configured (Host/Coil)");
                    }

                    // 2) Дополнительно «Старт робот» (гр.16), если это не та же группа
                    const int StartRobotIndex = 16;
                    if (robotIdx.Value != StartRobotIndex)
                    {
                        var start = RoboScanner.Models.RobotGroups.Get(StartRobotIndex);
                        startPulseSec =
                            (start.PulseSeconds.HasValue && start.PulseSeconds.Value > 0) ? start.PulseSeconds.Value :
                            (mappedPulseSec > 0 ? mappedPulseSec : 1);

                        if (!string.IsNullOrWhiteSpace(start.Host) && start.PrimaryCoilAddress.HasValue)
                        {
                            try
                            {
                                await ModbusOutputService.PulseAsync(
                                    host: start.Host, port: start.Port, unitId: start.UnitId,
                                    coilAddressOneBased: start.PrimaryCoilAddress.Value,
                                    pulseSeconds: startPulseSec
                                );
                                _log.Info("Relay", "Triggered Start Robot (group 16)",
                                    new { start.Host, start.UnitId, start.PrimaryCoilAddress, PulseSeconds = startPulseSec });
                            }
                            catch (Exception ex)
                            {
                                _log.Error("Relay", "Failed to trigger Start Robot (group 16)", ex);
                            }
                        }
                        else
                        {
                            _log.Warn("Relay", "Start Robot (group 16) not configured (Host/Coil)");
                        }
                    }
                }
                else
                {
                    _log.Warn("Relay", $"No robot-group mapping for scan-group {groupIndex}");
                }

                // Блокируем повторный старт на время активных реле (берём максимум)
                int blockSec = Math.Max(mappedPulseSec, startPulseSec);
                if (blockSec <= 0) blockSec = 1;
                RelayGate.BlockFor(TimeSpan.FromSeconds(blockSec));

                // Обновление UI/состояния
                var now = DateTime.Now;
                string xLbl = Axis("Scan.Axis.X", "X: ");
                string yLbl = Axis("Scan.Axis.Y", "Y: ");
                string zLbl = Axis("Scan.Axis.Z", "Z: ");
                TxtResultLine.Text = $"{groupName} — {xLbl}{x:F2}  {yLbl}{y:F2}  {zLbl}{z:F2}";

                ShowPlaceholders();
                _app.SetLastScan(groupIndex, x, y, z, now);

                _log.Info("Scan", "Scan completed",
                    new { Group = groupIndex, GroupName = groupName, X = x, Y = y, Z = z, At = now.ToString("o") });

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

                    return;
                }

                _app.OpState = OperationState.Done;
            }
            catch (Exception ex)
            {
                _log.Error("Scan", "Fatal error during scan", ex);
                _app.OpState = OperationState.Wait;
            }
            finally
            {
                UpdateButtons();
            }
        }





        /// <summary>
        /// Picks a random ACTIVE group and generates X/Y/Z:
        /// for each axis: [Max/2 .. Max]; if Max is null → [0 .. 100].
        /// </summary>
        private (int groupIndex, string groupName, double x, double y, double z)? SimulateFromActiveGroups()
        {
            var rules = RulesService.Instance.Rules
                .Where(r => r.IsActive)
                .Where(r => r.RobotGroup.HasValue)
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

        private void ShowPlaceholders(bool force = false)
        {
            // если уже есть фото — не трогаем (если явно не попросили)
            //if (!force && (ImgCam1.Source != null || ImgCam2.Source != null))
            //    return;

            //ImgCam1.Source = MakePlaceholder(800, 600, Colors.LightSteelBlue);
            //ImgCam2.Source = MakePlaceholder(800, 600, Colors.LightSkyBlue);
            //LblNoImg1.Visibility = Visibility.Collapsed;
            //LblNoImg2.Visibility = Visibility.Collapsed;
            foreach (var img in new[] { ImgCam1, ImgCam2 })
            {
                if (force || img.Source == null)
                {
                    img.Source = MakePlaceholder(800, 600, Colors.LightGray);
                }
                
            }

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
