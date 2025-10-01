using System;
using OpenCvSharp;

namespace RoboScanner.Services
{
    /// <summary>
    /// Бинаризация изображения (0/255). Возвращает Mat CV_8UC1.
    /// + Опционально: оставляет в кадре только один объект (крупнейший, предпочтительно не касающийся краёв),
    ///   всё остальное заливает цветом фона.
    /// </summary>
    public sealed class BinarizationService
    {
        private static readonly Lazy<BinarizationService> _lazy = new(() => new BinarizationService());
        public static BinarizationService Instance => _lazy.Value;
        private BinarizationService() { }

        // ----------------------------- OPTIONS -----------------------------
        public sealed class Options
        {
            // Порогирование
            public bool UseAdaptive { get; set; } = false;   // true → Adaptive Gaussian, иначе Otsu/Manual
            public int AdaptiveBlockSize { get; set; } = 31; // нечётное >= 3
            public int AdaptiveC { get; set; } = 5;
            public bool Invert { get; set; } = false;        // инвертировать бинарку после порога

            // Препроцесс
            public int BlurKernel { get; set; } = 3;        // 0 → без блюра; иначе нечётное
            public bool UseClahe { get; set; } = false;      // локальное выравнивание контраста перед порогом
            public double ClaheClipLimit { get; set; } = 2.0;
            public Size ClaheTileGrid { get; set; } = new Size(8, 8);

            // Постпроцесс: оставить один объект
            public bool KeepSingleObject { get; set; } = false;
            public int MinAreaPx { get; set; } = 0;       // 0 → автопорог (процент площади кадра)
            public double InnerCropPercent { get; set; } = 0.0; // 0..0.1: лёгкий внутренний кроп, если надо

            public double? ManualThreshold { get; set; } = null;
        }
        // -------------------------------------------------------------------

        /// <summary>Преобразует BGR/Gray/BGRA в бинарное изображение (CV_8UC1).</summary>
        public Mat Binarize(Mat srcBgr, Options? opt = null)
        {
            opt ??= new Options();

            // 1) Серый
            using var gray = ToGray(srcBgr);

            // 2) Препроцесс: Blur (опционально)
            Mat pre = gray;
            if (opt.BlurKernel >= 3 && (opt.BlurKernel % 2 == 1))
            {
                pre = new Mat();
                Cv2.GaussianBlur(gray, pre, new Size(opt.BlurKernel, opt.BlurKernel), 0);
            }

            // 3) Препроцесс: CLAHE (опционально)
            if (opt.UseClahe)
            {
                var eq = new Mat();
                using (var clahe = Cv2.CreateCLAHE(opt.ClaheClipLimit, opt.ClaheTileGrid))
                    clahe.Apply(pre, eq);
                if (!ReferenceEquals(pre, gray)) pre.Dispose();
                pre = eq;
            }

            // 4) Порогирование → dst (CV_8UC1)
            var dst = new Mat();
            if (opt.UseAdaptive)
            {
                int bs = opt.AdaptiveBlockSize;
                if (bs < 3) bs = 3;
                if ((bs & 1) == 0) bs += 1;

                var thType = opt.Invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                Cv2.AdaptiveThreshold(pre, dst, 255, AdaptiveThresholdTypes.GaussianC, thType, bs, opt.AdaptiveC);
            }
            else
            {
                var thType = opt.Invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                if (opt.ManualThreshold.HasValue)
                    Cv2.Threshold(pre, dst, opt.ManualThreshold.Value, 255, thType);
                else
                    Cv2.Threshold(pre, dst, 0, 255, thType | ThresholdTypes.Otsu); // Otsu
            }

            if (!ReferenceEquals(pre, gray)) pre.Dispose();

            // 5) Постпроцесс: один объект
            if (opt.KeepSingleObject)
            {
                // мягкие значения по умолчанию: >= 2% площади кадра
                int imgArea = dst.Cols * dst.Rows;
                int minArea = (opt.MinAreaPx > 0) ? opt.MinAreaPx : Math.Max((int)(0.02 * imgArea), 500);
                KeepOnlyLargestNonBorderComponent(dst, minArea, opt.InnerCropPercent);
            }

            return dst; // CV_8UC1 (0/255)
        }


        /// <summary>
        /// Ищет крупнейший объект на бинарке и возвращает его прямоугольник в координатах полного кадра.
        /// Ничего НЕ меняет в bin. Пробует обе полярности (как есть и инверт) и берёт лучшую.
        /// Возвращает true, если ROI найден (rect.Width>0 && rect.Height>0).
        /// </summary>
        public bool TryGetLargestObjectRoi(Mat bin, out OpenCvSharp.Rect rect, int minAreaPx = 0, double _ = 0.0)
        {
            rect = new OpenCvSharp.Rect();
            if (bin.Empty() || bin.Type() != MatType.CV_8UC1) return false;

            // 0) нормализуем к 0/255
            using var norm = bin.Clone();
            Cv2.Threshold(norm, norm, 0, 255, ThresholdTypes.Binary);

            int W = norm.Cols, H = norm.Rows;
            int imgArea = W * H;
            int minArea = (minAreaPx > 0) ? minAreaPx : Math.Max((int)(0.01 * imgArea), 200); // ≥1% кадра

            // 1) хотим, чтобы объект был БЕЛЫМ
            bool bgWhite = Cv2.CountNonZero(norm) > (imgArea / 2); // много белого ⇒ фон белый
            using var work = norm.Clone();
            if (bgWhite) Cv2.BitwiseNot(work, work);

            // 2) лёгкая морфология, чтобы контур был сплошной
            using (var k5 = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)))
                Cv2.MorphologyEx(work, work, MorphTypes.Close, k5, iterations: 1);

            // 3) внешние контуры
            OpenCvSharp.Point[][] contours;
            OpenCvSharp.HierarchyIndex[] _hier;
            Cv2.FindContours(work, out contours, out _hier,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours == null || contours.Length == 0) return false;

            // 4) выбираем самый большой
            int bestIdx = -1;
            double bestArea = -1;
            for (int i = 0; i < contours.Length; i++)
            {
                double a = Cv2.ContourArea(contours[i]);
                if (a > bestArea) { bestArea = a; bestIdx = i; }
            }
            if (bestIdx < 0 || bestArea < minArea) return false;

            rect = Cv2.BoundingRect(contours[bestIdx]);
            // на всякий случай ограничим рамкой изображения
            rect.X = Math.Max(0, rect.X);
            rect.Y = Math.Max(0, rect.Y);
            rect.Width = Math.Min(rect.Width, W - rect.X);
            rect.Height = Math.Min(rect.Height, H - rect.Y);

            return (rect.Width > 0 && rect.Height > 0);
        }


        /// <summary>
        /// Вспомогательный: из «рабочей» бинарки (объект белый) строит bbox крупнейшей компоненты.
        /// Выполняет агрессивную склейку (blur + dilate + close). Возвращает rect в координатах полного кадра.
        /// </summary>
        private static (Rect rect, int area) BuildRectFromComponents(Mat work, int minAreaPx, double innerCropPct)
        {
            Cv2.Threshold(work, work, 0, 255, ThresholdTypes.Binary);

            // Уберём одиночные точки и склеим разрывы (чтобы контур стал сплошным)
            Cv2.GaussianBlur(work, work, new Size(5, 5), 0);
            using (var k15 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(15, 15)))
            using (var k7 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(7, 7)))
            {
                Cv2.Dilate(work, work, k15, iterations: 2);
                Cv2.MorphologyEx(work, work, MorphTypes.Close, k7, iterations: 2);
            }

            // Внутренний кроп (по желанию), чтобы игнорировать соприкосновения с рамкой
            Rect roi = new Rect(0, 0, work.Cols, work.Rows);
            if (innerCropPct > 0)
            {
                int padX = Math.Max(1, (int)Math.Round(work.Cols * innerCropPct));
                int padY = Math.Max(1, (int)Math.Round(work.Rows * innerCropPct));
                roi = new Rect(padX, padY, Math.Max(1, work.Cols - 2 * padX), Math.Max(1, work.Rows - 2 * padY));
            }
            using var crop = work.SubMat(roi);

            // Связные компоненты (объект белый)
            using var labels = new Mat();
            using var stats = new Mat();
            using var cents = new Mat();
            int n = Cv2.ConnectedComponentsWithStats(crop, labels, stats, cents,
                                                     PixelConnectivity.Connectivity8, MatType.CV_32S);

            if (n <= 1) return (new Rect(), 0);

            // Автопорог по площади, если не задан
            int imgArea = crop.Cols * crop.Rows;
            int minArea = minAreaPx > 0 ? minAreaPx : Math.Max((int)(0.02 * imgArea), 500); // ≥2% кадра
            

            // Хелпер: не касается границы ROI
            bool NotTouching(Rect r, Rect R)
                => r.X > 1 && r.Y > 1 && (r.X + r.Width) < (R.Width - 1) && (r.Y + r.Height) < (R.Height - 1);

            int bestLbl = -1, bestArea = -1;
            int bestAnyLbl = -1, bestAnyArea = -1;
            var R = new Rect(0, 0, roi.Width, roi.Height);

            for (int lbl = 1; lbl < n; lbl++)
            {
                int area = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Area);

                if (area > bestAnyArea) { bestAnyArea = area; bestAnyLbl = lbl; }

                if (area < minArea) continue;

                int x = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Left);
                int y = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Top);
                int w = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Width);
                int h = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Height);

                if (!NotTouching(new Rect(x, y, w, h), R)) continue;

                if (area > bestArea) { bestArea = area; bestLbl = lbl; }
            }

            if (bestLbl < 0) { bestLbl = bestAnyLbl; bestArea = bestAnyArea; }
            if (bestLbl < 0 || bestArea <= 0) return (new Rect(), 0);

            // Получаем bbox в пределах ROI
            int bx = stats.Get<int>(bestLbl, (int)ConnectedComponentsTypes.Left);
            int by = stats.Get<int>(bestLbl, (int)ConnectedComponentsTypes.Top);
            int bw = stats.Get<int>(bestLbl, (int)ConnectedComponentsTypes.Width);
            int bh = stats.Get<int>(bestLbl, (int)ConnectedComponentsTypes.Height);

            // Переносим в координаты полного кадра
            var fullRect = new Rect(roi.X + bx, roi.Y + by, bw, bh);
            return (fullRect, bestArea);
        }


        // ===================== POSTPROCESS: KEEP SINGLE OBJECT =====================
        /// <summary>
        /// Оставляет только один объект на бинарке.
        /// Пробует обе полярности (как есть и инвертированную), строит маску по связным компонентам
        /// после агрессивной склейки (dilate+close), берёт маску с максимальной площадью.
        /// Вне маски кадр заливается цветом фона. Если адекватной маски нет — изображение не меняется.
        /// </summary>
        private static void KeepOnlyLargestNonBorderComponent(Mat bin, int minAreaPx, double innerCropPct = 0.0)
        {
            if (bin.Empty() || bin.Type() != MatType.CV_8UC1) return;

            // нормализуем в 0/255 (на случай, если пришло что-то «почти бинарное»)
            Cv2.Threshold(bin, bin, 0, 255, ThresholdTypes.Binary);

            // Две рабочие версии: как есть (A) и инверсия (B)
            using var workA = bin.Clone();
            using var workB = bin.Clone();
            Cv2.BitwiseNot(workB, workB);

            var candA = BuildMaskFromComponents(workA, minAreaPx, innerCropPct);
            var candB = BuildMaskFromComponents(workB, minAreaPx, innerCropPct);

            // Если обе пустые — выходим, ничего не меняем (без «белых/чёрных экранов»)
            int nzA = Cv2.CountNonZero(candA.mask);
            int nzB = Cv2.CountNonZero(candB.mask);
            if (nzA == 0 && nzB == 0) { candA.mask.Dispose(); candB.mask.Dispose(); return; }

            // Выбираем по площади
            var best = (candB.area > candA.area) ? candB : candA;

            // Цвет фона берём из исходной бинарки по доле белых пикселей
            int imgArea = bin.Cols * bin.Rows;
            bool bgWhite = Cv2.CountNonZero(bin) > (imgArea / 2);
            byte bg = (byte)(bgWhite ? 255 : 0);

            using var invMask = new Mat();
            Cv2.BitwiseNot(best.mask, invMask);

            // Вне маски → чистый фон, внутри маски остаётся исходная бинарка
            bin.SetTo(new Scalar(bg), invMask);

            if (best.mask != candA.mask) candA.mask.Dispose();
            if (best.mask != candB.mask) candB.mask.Dispose();
        }

        /// <summary>
        /// Строит маску крупнейшей компоненты на «рабочей» бинарке.
        /// Перед этим:
        ///  - слегка размываем, чтобы убрать одиночные точки;
        ///  - сильно расширяем и закрываем (dilate + close), чтобы линии/точки «слиплись» в силуэт;
        /// Компонента выбирается как крупнейшая, предпочтительно не касающаяся краёв ROI.
        /// Если ничего подходящего — возвращается пустая маска (не null).
        /// </summary>
        private static (Mat mask, int area) BuildMaskFromComponents(Mat work, int minAreaPx, double innerCropPct)
        {
            // 0) подстрахуемся: бинарное 0/255
            Cv2.Threshold(work, work, 0, 255, ThresholdTypes.Binary);

            // 1) GaussianBlur — убираем редкие пиксели
            Cv2.GaussianBlur(work, work, new Size(5, 5), 0);

            // 2) Агрессивная склейка
            using (var k15 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(15, 15)))
            using (var k7 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(7, 7)))
            {
                Cv2.Dilate(work, work, k15, iterations: 2);
                Cv2.MorphologyEx(work, work, MorphTypes.Close, k7, iterations: 2);
            }

            // 3) ROI (внутренний кроп по желанию)
            Rect roi = new Rect(0, 0, work.Cols, work.Rows);
            if (innerCropPct > 0)
            {
                int padX = Math.Max(1, (int)Math.Round(work.Cols * innerCropPct));
                int padY = Math.Max(1, (int)Math.Round(work.Rows * innerCropPct));
                roi = new Rect(padX, padY, Math.Max(1, work.Cols - 2 * padX), Math.Max(1, work.Rows - 2 * padY));
            }
            using var crop = work.SubMat(roi);

            // 4) Связные компоненты
            using var labels = new Mat();
            using var stats = new Mat();
            using var cents = new Mat();
            int n = Cv2.ConnectedComponentsWithStats(
                crop, labels, stats, cents, PixelConnectivity.Connectivity8, MatType.CV_32S);

            if (n <= 1)
                return (new Mat(work.Size(), MatType.CV_8UC1, Scalar.All(0)), 0);

            // Хелпер «не касается края ROI»
            bool NotTouching(Rect r, Rect R)
                => r.X > 1 && r.Y > 1 && (r.X + r.Width) < (R.Width - 1) && (r.Y + r.Height) < (R.Height - 1);

            int bestLbl = -1, bestArea = -1;
            int bestAnyLbl = -1, bestAnyArea = -1;
            var R = new Rect(0, 0, roi.Width, roi.Height);

            for (int lbl = 1; lbl < n; lbl++)
            {
                int area = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Area);

                // запоминаем вообще самый крупный на случай, если «правильных» нет
                if (area > bestAnyArea) { bestAnyArea = area; bestAnyLbl = lbl; }

                if (area < Math.Max(minAreaPx, 1)) continue;

                int x = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Left);
                int y = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Top);
                int w = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Width);
                int h = stats.Get<int>(lbl, (int)ConnectedComponentsTypes.Height);

                if (!NotTouching(new Rect(x, y, w, h), R)) continue;
                if (area > bestArea) { bestArea = area; bestLbl = lbl; }
            }

            if (bestLbl < 0) { bestLbl = bestAnyLbl; bestArea = bestAnyArea; }
            if (bestLbl < 0 || bestArea <= 0)
                return (new Mat(work.Size(), MatType.CV_8UC1, Scalar.All(0)), 0);

            // 5) Маска выбранной метки в пределах ROI
            using var maskCrop = new Mat(crop.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.InRange(labels, new Scalar(bestLbl), new Scalar(bestLbl), maskCrop);

            // 6) Переносим маску на полный размер
            var maskFull = new Mat(work.Size(), MatType.CV_8UC1, Scalar.All(0));
            maskCrop.CopyTo(new Mat(maskFull, roi));
            return (maskFull, bestArea);
        }
        // ==========================================================================

        /// <summary>Переводит в оттенки серого. Поддерживает вход 1/3/4 каналов.</summary>
        private static Mat ToGray(Mat src)
        {
            int ch = src.Channels();
            if (ch == 1)
            {
                return src.Clone();
            }
            else if (ch == 3)
            {
                var dst = new Mat();
                Cv2.CvtColor(src, dst, ColorConversionCodes.BGR2GRAY);
                return dst;
            }
            else if (ch == 4)
            {
                var dst = new Mat();
                Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2GRAY);
                return dst;
            }
            else
            {
                throw new NotSupportedException($"Unsupported channels: {ch}");
            }
        }
    }
}
