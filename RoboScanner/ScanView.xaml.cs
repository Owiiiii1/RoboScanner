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

        // Собрать опции бинаризации из Сохранённых настроек (AppSettings)
        private BinarizationService.Options BuildBinOptionsFromSettings()
        {
            // нормализация, как в калибровке
            int block = AppSettings.Default.AdaptiveBlockSize;
            if (block < 3) block = 3;
            if ((block & 1) == 0) block += 1;

            int blur = AppSettings.Default.BlurKernel;
            if (blur < 0) blur = 0;
            if (blur != 0 && (blur & 1) == 0) blur += 1;

            int tiles = Math.Max(1, AppSettings.Default.ClaheTileGrid);
            double clip = AppSettings.Default.ClaheClipLimit > 0 ? AppSettings.Default.ClaheClipLimit : 3.5;

            return new BinarizationService.Options
            {
                UseAdaptive = AppSettings.Default.UseAdaptive,
                AdaptiveBlockSize = block,
                AdaptiveC = AppSettings.Default.AdaptiveC,
                UseClahe = AppSettings.Default.UseClahe,
                ClaheClipLimit = clip,
                ClaheTileGrid = new OpenCvSharp.Size(tiles, tiles),
                BlurKernel = blur,
                Invert = AppSettings.Default.Invert,
                KeepSingleObject = false // мы ищем ROI, изображение не трогаем
            };
        }

        // Перевод процента площади из настроек в пиксели
        private int ComputeMinAreaPxFromSettings(int cols, int rows)
        {
            double pct = Math.Max(0, AppSettings.Default.MinAreaPct); // % кадра
            int area = Math.Max(1, cols * rows);
            int fromPct = (int)Math.Round(area * (pct / 100.0));
            return Math.Max(500, fromPct); // предохранитель, как в калибровке
        }



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

            // Будем сюда складывать миллиметры; по умолчанию 0
            double lengthMm = 0;  // TOP.Width  -> X
            double widthMm = 0;  // TOP.Height -> Y
            double heightMm = 0;  // SIDE.Height-> Z

            // Флаги диагностики
            bool roiTopFound = false;
            bool roiSideFound = false;
            bool calibTopOk = false;
            bool calibSideOk = false;


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

              

                //// ПАДДИНГ ТОЛЬКО ДЛЯ ДИСПЛЕЯ, НЕ ДЛЯ ИЗМЕРЕНИЯ!
                //static OpenCvSharp.Rect InflateAndClip(OpenCvSharp.Rect r, int pad, int w, int h)
                //{
                //    int x = Math.Max(0, r.X - pad);
                //    int y = Math.Max(0, r.Y - pad);
                //    int rw = Math.Min(r.Width + 2 * pad, w - x);
                //    int rh = Math.Min(r.Height + 2 * pad, h - y);
                //    return new OpenCvSharp.Rect(x, y, rw, rh);
                //}

                // --- 2) Локальная функция обработки одной камеры ---
                // --- 2) Локальная функция обработки одной камеры ---
                // ВОЗВРАЩАЕМ roiMeasure — БЕЗ ПАДДИНГА
                async Task<(bool ok, OpenCvSharp.Rect roiMeasure, int cols, int rows)> ProcessOneAsync(
                        string moniker,
                        System.Windows.Controls.Image imgView,
                        System.Windows.Controls.TextBlock noImgLabel)
                {
                    using var frame = await Capture.CaptureOnceAsync(moniker); // BGR

                    var binOpt = BuildBinOptionsFromSettings();               // <<<<<<
                    using var bin = BinarizationService.Instance.Binarize(frame, binOpt);

                    int minAreaPx = ComputeMinAreaPxFromSettings(frame.Cols, frame.Rows); // <<<<<<

                    if (BinarizationService.Instance.TryGetLargestObjectRoi(bin, out var roi, minAreaPx)) // <<<<<<
                    {
                        // Показ – с паддингом (только для UI)
                        const int pad = 12;
                        int x = Math.Max(0, roi.X - pad);
                        int y = Math.Max(0, roi.Y - pad);
                        int w = Math.Min(roi.Width + 2 * pad, frame.Cols - x);
                        int h = Math.Min(roi.Height + 2 * pad, frame.Rows - y);

                        using (var crop = new Mat(frame, new OpenCvSharp.Rect(x, y, w, h)))
                        using (var bgra = new Mat())
                        {
                            Cv2.CvtColor(crop, bgra, ColorConversionCodes.BGR2BGRA);
                            imgView.Source = bgra.ToWriteableBitmap();
                            noImgLabel.Visibility = Visibility.Collapsed;
                        }

                        return (true, roi, frame.Cols, frame.Rows); // измерения — из НЕрасширенного roi
                    }
                    else
                    {
                        using var dbg = new Mat();
                        Cv2.CvtColor(bin, dbg, ColorConversionCodes.GRAY2BGRA);
                        imgView.Source = dbg.ToWriteableBitmap();
                        noImgLabel.Visibility = Visibility.Collapsed;
                        return (false, default, frame.Cols, frame.Rows);
                    }
                }



                // --- 3) Обработка обеих камер ---
                (bool okTop, CvRect roiTop, int wTop, int hTop) = (false, default, 0, 0);
                (bool okSide, CvRect roiSide, int wSide, int hSide) = (false, default, 0, 0);

                // Камера 1 — считаем TOP (длина/ширина)
                if (cam1 != null)
                {
                    (okTop, roiTop, wTop, hTop) = await ProcessOneAsync(cam1, ImgCam1, LblNoImg1);
                }
                else
                {
                    ImgCam1.Source = null; LblNoImg1.Visibility = Visibility.Visible;
                    _log.Warn("Camera", "Camera1 is not selected or not found");
                }

                // Камера 2 — считаем SIDE (высота)
                if (cam2 != null)
                {
                    (okSide, roiSide, wSide, hSide) = await ProcessOneAsync(cam2, ImgCam2, LblNoImg2);
                }
                else
                {
                    ImgCam2.Source = null; LblNoImg2.Visibility = Visibility.Visible;
                    _log.Warn("Camera", "Camera2 is not selected or not found");
                }

                // === Пересчёт px -> мм по калибровке (0, если что-то не так) ===
                //double kTop = AppSettings.Default.KTop;
                //double kSide = AppSettings.Default.KSide;
                //double distTopMm = AppSettings.Default.TopDistanceMm;
                //double distSideMm = AppSettings.Default.SideDistanceMm;

                //double mmPerPxTop = (kTop > 0 && distTopMm > 0) ? kTop * distTopMm : 0;
                //double mmPerPxSide = (kSide > 0 && distSideMm > 0) ? kSide * distSideMm : 0;

                double mmPerPxTop = AppSettings.Default.MmPerPxTop;   // мм/px для верхней камеры
                double mmPerPxSide = AppSettings.Default.MmPerPxSide;  // мм/px для боковой

                // определяем, были ли найдены ROI (okTop/okSide у тебя получены из ProcessOneAsync)
                roiTopFound = okTop;
                roiSideFound = okSide;
                calibTopOk = (mmPerPxTop > 0);
                calibSideOk = (mmPerPxSide > 0);

                // TOP -> длина/ширина (или 0)
                if (roiTopFound && calibTopOk)
                {
                    lengthMm = roiTop.Width * mmPerPxTop;
                    widthMm = roiTop.Height * mmPerPxTop;
                }
                else
                {
                    lengthMm = 0;
                    widthMm = 0;
                    if (!roiTopFound) _log.Warn("Scan.TOP", "ROI not found → set Length/Width = 0");
                    else _log.Warn("Scan.TOP", "Calibration missing → set Length/Width = 0 (KTop or TopDistanceMm <= 0)");
                }

                // SIDE -> высота (или 0)
                if (roiSideFound && calibSideOk)
                {
                    heightMm = roiSide.Height * mmPerPxSide;
                }
                else
                {
                    heightMm = 0;
                    if (!roiSideFound) _log.Warn("Scan.SIDE", "ROI not found → set Height = 0");
                    else _log.Warn("Scan.SIDE", "Calibration missing → set Height = 0 (KSide or SideDistanceMm <= 0)");
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





                // === Select group by actual measured dimensions (by MAX, bottom-up) ===
                var selectedRule = RoboScanner.Services.PartGroupSelector.SelectByMax(
                    RulesService.Instance.Rules,
                    lengthMm,   // X
                    widthMm,    // Y
                    heightMm    // Z
                );

                if (selectedRule == null)
                {
                    _log.Warn("Scan", "No valid groups (active + RobotGroup + MaxX/MaxY/MaxZ).");
                    MessageBox.Show(
                        L("Scan.Alert.NoActiveGroups.Body",
                          "There are no active groups in the settings. Specify the dimensions for at least one group."),
                        L("Scan.Alert.NoActiveGroups.Title", "Warning"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);

                    _app.OpState = OperationState.Wait;
                    return;
                }

                // result fields (как было у sim) — теперь из выбранного правила + реальные размеры
                int groupIndex = selectedRule.Index;
                string groupName = string.IsNullOrWhiteSpace(selectedRule.Name) ? $"Group {groupIndex}" : selectedRule.Name;
                double x = lengthMm;
                double y = widthMm;
                double z = heightMm;

                // робот-группа (nullable → int?)
                int? robotIdx = selectedRule.RobotGroup;


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
