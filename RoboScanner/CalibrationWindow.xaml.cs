using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using RoboScanner.Services;
using AppSettings = RoboScanner.Properties.Settings;
using CvRect = OpenCvSharp.Rect;

namespace RoboScanner
{
    public partial class CalibrationWindow : System.Windows.Window
    {
        private CaptureManager Capture => CaptureManager.Instance;
        private readonly LogService _log = LogService.Instance;

        // последние «сырые» пиксели с кнопки «Калибровать»
        private int _topPxW = 0;   // ширина ROI TOP (px)
        private int _topPxH = 0;   // высота  ROI TOP (px)
        private int _sidePxH = 0;  // высота  ROI SIDE(px)

        // какая сторона TOP мы интерпретировали как «Длина»
        private enum TopMap { WidthIsLength, HeightIsLength }
        private TopMap _topMapping = TopMap.WidthIsLength;



        public CalibrationWindow()
        {
            InitializeComponent();
            LoadBinSettingsToUi();

            // Показать текущие сохранённые коэффициенты (если есть)
            LblTop.Text = (AppSettings.Default.MmPerPxTop > 0) ? $"mm/px(top)={AppSettings.Default.MmPerPxTop:0.###}" : "mm/px(top) не задан";
            LblSide.Text = (AppSettings.Default.MmPerPxSide > 0) ? $"mm/px(side)={AppSettings.Default.MmPerPxSide:0.###}" : "mm/px(side) не задан";

            // В поля «итоговых размеров» (редактируемые) поставим что-нибудь осмысленное
            // Если есть «номиналы» — возьмём их как стартовые
            TxtOutL.Text = AppSettings.Default.RefLengthMm.ToString("0", CultureInfo.InvariantCulture);
            TxtOutW.Text = AppSettings.Default.RefWidthMm.ToString("0", CultureInfo.InvariantCulture);
            TxtOutH.Text = AppSettings.Default.RefHeightMm.ToString("0", CultureInfo.InvariantCulture);
        }

        // ПАДДИНГ — только для предпросмотра
        private static CvRect InflateAndClip(CvRect r, int pad, int w, int h)
        {
            int x = Math.Max(0, r.X - pad);
            int y = Math.Max(0, r.Y - pad);
            int rw = Math.Min(r.Width + 2 * pad, w - x);
            int rh = Math.Min(r.Height + 2 * pad, h - y);
            return new CvRect(x, y, rw, rh);
        }

        private static double ParseD(string? s, double fallback)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static double RelErr(double a, double b)
        {
            if (a == 0 && b == 0) return 0;
            return Math.Abs(a - b) / Math.Max(Math.Abs(a), Math.Abs(b) + 1e-9);
        }

        private void LoadBinSettingsToUi()
        {
            TxtAdaptiveBlock.Text = (AppSettings.Default.AdaptiveBlockSize > 0
                                        ? AppSettings.Default.AdaptiveBlockSize : 41).ToString();
            TxtAdaptiveC.Text = (AppSettings.Default.AdaptiveC != 0
                                        ? AppSettings.Default.AdaptiveC : 3).ToString();
            ChkUseAdaptive.IsChecked = AppSettings.Default.UseAdaptive;

            TxtClaheClip.Text = (AppSettings.Default.ClaheClipLimit > 0
                                        ? AppSettings.Default.ClaheClipLimit : 3.5)
                                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            TxtClaheTiles.Text = (AppSettings.Default.ClaheTileGrid > 0
                                        ? AppSettings.Default.ClaheTileGrid : 4).ToString();
            ChkUseClahe.IsChecked = AppSettings.Default.UseClahe;

            TxtBlurKernel.Text = (AppSettings.Default.BlurKernel >= 0
                                        ? AppSettings.Default.BlurKernel : 5).ToString();
            ChkInvert.IsChecked = AppSettings.Default.Invert;

            TxtMinAreaPct.Text = (AppSettings.Default.MinAreaPct >= 0
                                        ? AppSettings.Default.MinAreaPct : 2.0)
                                        .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ========================== КАЛИБРОВАТЬ ==========================
        private async void BtnCalibrate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // «номинальные» размеры детали (мм) — используем как подсказку для выбора соответствия сторон
                double refL = ParseD(TxtOutL.Text, 100);
                double refW = ParseD(TxtOutW.Text, 50);
                double refH = ParseD(TxtOutH.Text, 50);

                // выбор камер как в основном приложении
                var all = CameraDiscoveryService.Instance.ListVideoDevices();
                string? camTop = CameraDiscoveryService.Instance.FindByMoniker(all, AppSettings.Default.Camera1Id)?.Moniker
                                  ?? all.ElementAtOrDefault(0)?.Moniker;
                string? camSide = CameraDiscoveryService.Instance.FindByMoniker(all, AppSettings.Default.Camera2Id)?.Moniker
                                  ?? all.ElementAtOrDefault(1)?.Moniker;

                var binOpt = new BinarizationService.Options
                {
                    UseAdaptive = AppSettings.Default.UseAdaptive,
                    AdaptiveBlockSize = AppSettings.Default.AdaptiveBlockSize,
                    AdaptiveC = AppSettings.Default.AdaptiveC,
                    UseClahe = AppSettings.Default.UseClahe,
                    ClaheClipLimit = AppSettings.Default.ClaheClipLimit,
                    ClaheTileGrid = new OpenCvSharp.Size(
                            Math.Max(1, AppSettings.Default.ClaheTileGrid),
                            Math.Max(1, AppSettings.Default.ClaheTileGrid)),
                    BlurKernel = AppSettings.Default.BlurKernel,
                    Invert = AppSettings.Default.Invert,
                    KeepSingleObject = false
                };

                if (binOpt.AdaptiveBlockSize < 3) binOpt.AdaptiveBlockSize = 3;
                if ((binOpt.AdaptiveBlockSize & 1) == 0) binOpt.AdaptiveBlockSize += 1;

                if (binOpt.BlurKernel < 0) binOpt.BlurKernel = 0;
                if (binOpt.BlurKernel != 0 && (binOpt.BlurKernel & 1) == 0) binOpt.BlurKernel += 1;

                if (binOpt.ClaheTileGrid.Width < 1 || binOpt.ClaheTileGrid.Height < 1)
                    binOpt.ClaheTileGrid = new OpenCvSharp.Size(1, 1);

                if (binOpt.ClaheClipLimit <= 0) binOpt.ClaheClipLimit = 3.5;

                int ComputeMinAreaPxFromSettings(int cols, int rows)
                {
                    double pct = Math.Max(0, AppSettings.Default.MinAreaPct);
                    int area = Math.Max(1, cols * rows);
                    int fromPct = (int)Math.Round(area * (pct / 100.0));
                    return Math.Max(500, fromPct);
                }

                // -------- TOP (длина+ширина) --------
                _topPxW = _topPxH = 0;
                if (camTop != null)
                {
                    using var frame = await Capture.CaptureOnceAsync(camTop);
                    using var bin = BinarizationService.Instance.Binarize(frame, binOpt);
                    int minAreaPx = ComputeMinAreaPxFromSettings(frame.Cols, frame.Rows);

                    if (BinarizationService.Instance.TryGetLargestObjectRoi(bin, out var roi, minAreaPx))

                    {
                        // предпросмотр с паддингом
                        var disp = InflateAndClip(roi, 12, frame.Cols, frame.Rows);
                        using (var crop = new Mat(frame, disp))
                        using (var bgra = new Mat())
                        {
                            Cv2.CvtColor(crop, bgra, ColorConversionCodes.BGR2BGRA);
                            ImgTop.Source = bgra.ToWriteableBitmap();
                        }

                        // «чистые» пиксели для расчёта (без паддинга)
                        _topPxW = roi.Width;
                        _topPxH = roi.Height;

                        // решаем, какая сторона — «длина»
                        double errAB = RelErr(refL / _topPxW, refW / _topPxH); // W→L, H→W
                        double errBA = RelErr(refL / _topPxH, refW / _topPxW); // H→L, W→W

                        if (errBA < errAB) _topMapping = TopMap.HeightIsLength;
                        else _topMapping = TopMap.WidthIsLength;

                        LblTop.Text = $"TOP ROI: {_topPxW}×{_topPxH} px  |  map={_topMapping}";
                    }
                    else
                    {
                        using var dbg = new Mat();
                        Cv2.CvtColor(bin, dbg, ColorConversionCodes.GRAY2BGRA);
                        ImgTop.Source = dbg.ToWriteableBitmap();
                        LblTop.Text = "TOP: ROI не найден (показана бинарка)";
                    }
                }
                else LblTop.Text = "TOP: камера не выбрана";

                // -------- SIDE (высота) --------
                _sidePxH = 0;
                if (camSide != null)
                {
                    using var frame = await Capture.CaptureOnceAsync(camSide);
                    using var bin = BinarizationService.Instance.Binarize(frame, binOpt);
                    int minAreaPx = ComputeMinAreaPxFromSettings(frame.Cols, frame.Rows);

                    if (BinarizationService.Instance.TryGetLargestObjectRoi(bin, out var roi, minAreaPx))
                    {
                        var disp = InflateAndClip(roi, 12, frame.Cols, frame.Rows);
                        using (var crop = new Mat(frame, disp))
                        using (var bgra = new Mat())
                        {
                            Cv2.CvtColor(crop, bgra, ColorConversionCodes.BGR2BGRA);
                            ImgSide.Source = bgra.ToWriteableBitmap();
                        }

                        _sidePxH = roi.Height;

                        LblSide.Text = $"SIDE ROI: {roi.Width}×{_sidePxH} px";
                    }
                    else
                    {
                        using var dbg = new Mat();
                        Cv2.CvtColor(bin, dbg, ColorConversionCodes.GRAY2BGRA);
                        ImgSide.Source = dbg.ToWriteableBitmap();
                        LblSide.Text = "SIDE: ROI не найден (показана бинарка)";
                    }
                }
                else LblSide.Text = "SIDE: камера не выбрана";

                // если пиксели получены — рассчитаем «текущие» размеры в мм и выведем их в редактируемые поля
                if (_topPxW > 0 && _topPxH > 0 && _sidePxH > 0)
                {
                    // если уже есть сохранённые мм/px — покажем размеры по ним,
                    // иначе возьмём стартовые из «номиналов», чтобы пользователь видел числа и мог подправить
                    double mmPerPxTop = AppSettings.Default.MmPerPxTop;
                    double mmPerPxSide = AppSettings.Default.MmPerPxSide;

                    double calcL, calcW, calcH;

                    if (mmPerPxTop > 0 && mmPerPxSide > 0)
                    {
                        // размеры по текущей калибровке
                        if (_topMapping == TopMap.WidthIsLength)
                        {
                            calcL = _topPxW * mmPerPxTop;
                            calcW = _topPxH * mmPerPxTop;
                        }
                        else
                        {
                            calcL = _topPxH * mmPerPxTop;
                            calcW = _topPxW * mmPerPxTop;
                        }
                        calcH = _sidePxH * mmPerPxSide;
                    }
                    else
                    {
                        // стартовые — просто «номиналы»
                        calcL = refL; calcW = refW; calcH = refH;
                    }

                    TxtOutL.Text = calcL.ToString("0.###", CultureInfo.InvariantCulture);
                    TxtOutW.Text = calcW.ToString("0.###", CultureInfo.InvariantCulture);
                    TxtOutH.Text = calcH.ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                _log.Error("Calibration", "Calibrate failed", ex);
                MessageBox.Show(ex.Message, "Калибровка: ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========================== СОХРАНИТЬ ==========================
        // Пользователь мог подправить L/W/H. Пересчитываем mm/px из последних пикселей.
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double Lmm = ParseD(TxtOutL.Text, 0);
                double Wmm = ParseD(TxtOutW.Text, 0);
                double Hmm = ParseD(TxtOutH.Text, 0);

                if (_topPxW <= 0 || _topPxH <= 0 || _sidePxH <= 0)
                {
                    MessageBox.Show("Сначала нажмите «Калибровать», чтобы получить пиксели ROI.", "Нет данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // mm/px TOP из отредактированных размеров
                double mmPerPxTop;
                if (_topMapping == TopMap.WidthIsLength)
                {
                    // W->L, H->W
                    double m1 = (Lmm > 0) ? (Lmm / _topPxW) : 0;
                    double m2 = (Wmm > 0) ? (Wmm / _topPxH) : 0;
                    if (m1 <= 0 || m2 <= 0) throw new InvalidOperationException("Введите положительные L/W.");
                    mmPerPxTop = 0.5 * (m1 + m2);
                }
                else
                {
                    // H->L, W->W
                    double m1 = (Lmm > 0) ? (Lmm / _topPxH) : 0;
                    double m2 = (Wmm > 0) ? (Wmm / _topPxW) : 0;
                    if (m1 <= 0 || m2 <= 0) throw new InvalidOperationException("Введите положительные L/W.");
                    mmPerPxTop = 0.5 * (m1 + m2);
                }

                // mm/px SIDE из H
                if (Hmm <= 0) throw new InvalidOperationException("Введите положительную высоту.");
                double mmPerPxSide = Hmm / _sidePxH;

                AppSettings.Default.MmPerPxTop = mmPerPxTop;
                AppSettings.Default.MmPerPxSide = mmPerPxSide;

                // заодно сохраним «номиналы» как подсказки для следующей сессии
                AppSettings.Default.RefLengthMm = Lmm;
                AppSettings.Default.RefWidthMm = Wmm;
                AppSettings.Default.RefHeightMm = Hmm;

                AppSettings.Default.Save();

                LblTop.Text = $"mm/px(top)={mmPerPxTop:0.###}";
                LblSide.Text = $"mm/px(side)={mmPerPxSide:0.###}";

                MessageBox.Show("Сохранено. Теперь обычный скан использует новые коэффициенты mm/px.", "Готово",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _log.Error("Calibration", "Save failed", ex);
                MessageBox.Show(ex.Message, "Сохранение: ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========================== СКАНИРОВАТЬ (проверка) ==========================
        private async void BtnTestScan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double mmPerPxTop = AppSettings.Default.MmPerPxTop;
                double mmPerPxSide = AppSettings.Default.MmPerPxSide;
                if (mmPerPxTop <= 0 || mmPerPxSide <= 0)
                {
                    MessageBox.Show("Коэффициенты mm/px не заданы. Сначала «Калибровать» → «Сохранить».", "Нет калибровки",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var all = CameraDiscoveryService.Instance.ListVideoDevices();
                string? camTop = CameraDiscoveryService.Instance.FindByMoniker(all, AppSettings.Default.Camera1Id)?.Moniker
                                  ?? all.ElementAtOrDefault(0)?.Moniker;
                string? camSide = CameraDiscoveryService.Instance.FindByMoniker(all, AppSettings.Default.Camera2Id)?.Moniker
                                  ?? all.ElementAtOrDefault(1)?.Moniker;

                var binOpt = BuildBinOptionsFromUi();


                double L = 0, W = 0, H = 0;

                int ComputeMinAreaPxFromSettings(int cols, int rows)
                {
                    double pct = Math.Max(0, AppSettings.Default.MinAreaPct);
                    int area = Math.Max(1, cols * rows);
                    int fromPct = (int)Math.Round(area * (pct / 100.0));
                    return Math.Max(500, fromPct);
                }

                // TOP
                if (camTop != null)
                {
                    using var frame = await Capture.CaptureOnceAsync(camTop);
                    using var bin = BinarizationService.Instance.Binarize(frame, binOpt);
                    int minAreaPx = ComputeMinAreaPxFromSettings(frame.Cols, frame.Rows);

                    if (BinarizationService.Instance.TryGetLargestObjectRoi(bin, out var roi, minAreaPx))
                    {
                        var disp = InflateAndClip(roi, 12, frame.Cols, frame.Rows);
                        using (var crop = new Mat(frame, disp))
                        using (var bgra = new Mat())
                        {
                            Cv2.CvtColor(crop, bgra, ColorConversionCodes.BGR2BGRA);
                            ImgTop.Source = bgra.ToWriteableBitmap();
                        }

                        if (_topMapping == TopMap.WidthIsLength)
                        {
                            L = roi.Width * mmPerPxTop;
                            W = roi.Height * mmPerPxTop;
                        }
                        else
                        {
                            L = roi.Height * mmPerPxTop;
                            W = roi.Width * mmPerPxTop;
                        }
                    }
                }

                // SIDE
                if (camSide != null)
                {
                    using var frame = await Capture.CaptureOnceAsync(camSide);
                    using var bin = BinarizationService.Instance.Binarize(frame, binOpt);
                    int minAreaPx = ComputeMinAreaPxFromSettings(frame.Cols, frame.Rows);

                    if (BinarizationService.Instance.TryGetLargestObjectRoi(bin, out var roi, minAreaPx))
                    {
                        var disp = InflateAndClip(roi, 12, frame.Cols, frame.Rows);
                        using (var crop = new Mat(frame, disp))
                        using (var bgra = new Mat())
                        {
                            Cv2.CvtColor(crop, bgra, ColorConversionCodes.BGR2BGRA);
                            ImgSide.Source = bgra.ToWriteableBitmap();
                        }
                        H = roi.Height * mmPerPxSide;
                    }
                }

                // покажем под полями (в этих же «выходных» полях — чтобы сравнить)
                TxtOutL.Text = L.ToString("0.###", CultureInfo.InvariantCulture);
                TxtOutW.Text = W.ToString("0.###", CultureInfo.InvariantCulture);
                TxtOutH.Text = H.ToString("0.###", CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                _log.Error("Calibration", "TestScan failed", ex);
                MessageBox.Show(ex.Message, "Сканирование: ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==== LIVE bin settings helpers (UI -> Options) ====
        // парсинг
        private static int ParseIntSafe(string? s, int fb)
            => int.TryParse(s, out var v) ? v : fb;

        private static double ParseDoubleSafe(string? s, double fb)
            => double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fb;

        // собрать опции бинаризации из контролов окна (если нет — дефолты)
        private BinarizationService.Options BuildBinOptionsFromUi()
        {
            bool useAdaptive = (ChkUseAdaptive?.IsChecked == true);
            int blockSize = ParseIntSafe(TxtAdaptiveBlock?.Text, 41);
            if (blockSize < 3) blockSize = 3;
            if ((blockSize & 1) == 0) blockSize++; // нечётное

            int adaptiveC = ParseIntSafe(TxtAdaptiveC?.Text, 3);

            bool useClahe = (ChkUseClahe?.IsChecked == true);
            double clip = ParseDoubleSafe(TxtClaheClip?.Text, 3.5);
            int tiles = Math.Max(1, ParseIntSafe(TxtClaheTiles?.Text, 4));

            int blur = Math.Max(0, ParseIntSafe(TxtBlurKernel?.Text, 5));
            if (blur % 2 == 0 && blur != 0) blur++; // нечётное ядро (или 0=off)

            bool invert = (ChkInvert?.IsChecked == true);

            return new BinarizationService.Options
            {
                UseAdaptive = useAdaptive,
                AdaptiveBlockSize = blockSize,
                AdaptiveC = adaptiveC,
                UseClahe = useClahe,
                ClaheClipLimit = clip,
                ClaheTileGrid = new OpenCvSharp.Size(tiles, tiles),
                BlurKernel = blur,
                Invert = invert,
                KeepSingleObject = false
            };
        }

        // минимальная площадь объекта в пикселях из UI-процента
        private int ComputeMinAreaPxFromUi(int cols, int rows)
        {
            double pct = Math.Max(0, ParseDoubleSafe(TxtMinAreaPct?.Text, 2.0)); // %
            int area = Math.Max(1, cols * rows);
            int fromPct = (int)Math.Round(area * (pct / 100.0));
            return Math.Max(500, fromPct); // предохранитель
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // читаем
                bool useAdaptive = ChkUseAdaptive.IsChecked == true;
                int blockSize = ParseIntSafe(TxtAdaptiveBlock.Text, 41);
                int adaptiveC = ParseIntSafe(TxtAdaptiveC.Text, 3);

                bool useClahe = ChkUseClahe.IsChecked == true;
                double clipLimit = ParseDoubleSafe(TxtClaheClip.Text, 3.5);
                int tileGrid = ParseIntSafe(TxtClaheTiles.Text, 4);

                int blurKernel = ParseIntSafe(TxtBlurKernel.Text, 5);
                bool invert = ChkInvert.IsChecked == true;

                double minAreaPct = ParseDoubleSafe(TxtMinAreaPct.Text, 2.0);

                // нормализация значений
                if (blockSize < 3) blockSize = 3;
                if ((blockSize & 1) == 0) blockSize += 1;     // adaptive block — нечётный

                if (blurKernel < 0) blurKernel = 0;
                if (blurKernel != 0 && (blurKernel & 1) == 0) // blur — нечётный (или 0 = выкл)
                    blurKernel += 1;

                if (tileGrid < 1) tileGrid = 1;               // хотя бы 1×1
                if (clipLimit <= 0) clipLimit = 3.5;          // безопасный дефолт

                if (minAreaPct < 0) minAreaPct = 0;
                if (minAreaPct > 20) minAreaPct = 20;         // здравый верхний предел

                // сохраняем
                AppSettings.Default.UseAdaptive = useAdaptive;
                AppSettings.Default.AdaptiveBlockSize = blockSize;
                AppSettings.Default.AdaptiveC = adaptiveC;

                AppSettings.Default.UseClahe = useClahe;
                AppSettings.Default.ClaheClipLimit = clipLimit;
                AppSettings.Default.ClaheTileGrid = tileGrid;

                AppSettings.Default.BlurKernel = blurKernel;
                AppSettings.Default.Invert = invert;

                AppSettings.Default.MinAreaPct = minAreaPct;

                AppSettings.Default.Save();

                MessageBox.Show("Настройки бинаризации сохранены.", "Сохранено",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка сохранения",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }




    }
}
