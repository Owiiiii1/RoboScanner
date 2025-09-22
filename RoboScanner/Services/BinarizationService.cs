using System;
using OpenCvSharp;

namespace RoboScanner.Services
{
    /// <summary>
    /// Бинаризация изображения (0/255). Возвращает Mat CV_8UC1.
    /// </summary>
    public sealed class BinarizationService
    {
        private static readonly Lazy<BinarizationService> _lazy = new(() => new BinarizationService());
        public static BinarizationService Instance => _lazy.Value;
        private BinarizationService() { }

        public sealed class Options
        {
            public bool UseAdaptive { get; set; } = false;        // true -> Adaptive Gaussian
            public int AdaptiveBlockSize { get; set; } = 31;     // нечётное >=3
            public int AdaptiveC { get; set; } = 5;
            public bool Invert { get; set; } = false;             // инвертировать ч/б
            public int BlurKernel { get; set; } = 3;             // 0 -> без размытия; иначе нечётное
            public double? ManualThreshold { get; set; } = null;  // если задан, вместо Otsu
        }

        /// <summary>Преобразует BGR/Gray/BGRA в бинарное изображение (CV_8UC1).</summary>
        public Mat Binarize(Mat srcBgr, Options? opt = null)
        {
            opt ??= new Options();

            // 1) В серый
            using var gray = ToGray(srcBgr);

            // 2) Размытие (опционально)
            Mat pre = gray;
            if (opt.BlurKernel >= 3 && (opt.BlurKernel % 2 == 1))
            {
                pre = new Mat();
                OpenCvSharp.Cv2.GaussianBlur(gray, pre, new Size(opt.BlurKernel, opt.BlurKernel), 0);
            }

            // 3) Порог -> dst (CV_8UC1)
            var dst = new Mat();
            if (opt.UseAdaptive)
            {
                int bs = opt.AdaptiveBlockSize;
                if (bs < 3) bs = 3;
                if (bs % 2 == 0) bs += 1;

                var type = opt.Invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                OpenCvSharp.Cv2.AdaptiveThreshold(pre, dst, 255,
                    AdaptiveThresholdTypes.GaussianC, type, bs, opt.AdaptiveC);
            }
            else
            {
                var type = opt.Invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                if (opt.ManualThreshold.HasValue)
                {
                    OpenCvSharp.Cv2.Threshold(pre, dst, opt.ManualThreshold.Value, 255, type);
                }
                else
                {
                    type |= ThresholdTypes.Otsu; // Otsu подбирает порог сам
                    OpenCvSharp.Cv2.Threshold(pre, dst, 0, 255, type);
                }
            }

            if (!ReferenceEquals(pre, gray)) pre.Dispose();
            return dst; // CV_8UC1 (grayscale 0/255)
        }

        /// <summary>Корректный многострочный вариант без switch-выражения.</summary>
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
                OpenCvSharp.Cv2.CvtColor(src, dst, ColorConversionCodes.BGR2GRAY);
                return dst;
            }
            else if (ch == 4)
            {
                var dst = new Mat();
                OpenCvSharp.Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2GRAY);
                return dst;
            }
            else
            {
                throw new NotSupportedException($"Unsupported channels: {ch}");
            }
        }
    }
}
